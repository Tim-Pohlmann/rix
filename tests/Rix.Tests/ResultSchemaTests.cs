using Rix.CiFailure;
using Rix.Job;
using Rix.Submit;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace Rix.Tests;

/// <summary>
/// Keeps <c>schemas/</c> - the JSON rix prints for the workflow bash to read - in step with the
/// result types it is serialized from. The bash side reads these results with <c>jq</c> and a
/// <c>// ""</c> fallback, so a renamed field there reads as blank instead of failing; the committed
/// schemas are what its tests validate their fixtures against, and this is what stops those
/// schemas from falling behind the C#.
/// </summary>
/// <remarks>Run with <c>RIX_UPDATE_SCHEMAS=1</c> to rewrite the committed files after an
/// intentional change to a result type.</remarks>
[TestClass]
public class ResultSchemaTests
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [TestMethod]
    [DataRow("job-result.schema.json")]
    [DataRow("submit-result.schema.json")]
    [DataRow("ci-failure-output.schema.json")]
    public void CommittedSchema_MatchesTheResultTypes(string fileName)
    {
        var path = Path.Combine(RepoRoot(), "schemas", fileName);
        var generated = Generate(fileName).ToJsonString(Indented) + "\n";

        if (Environment.GetEnvironmentVariable("RIX_UPDATE_SCHEMAS") == "1")
        {
            File.WriteAllText(path, generated);
            return;
        }

        Assert.IsTrue(File.Exists(path), $"{path} is missing - run the tests with RIX_UPDATE_SCHEMAS=1 to create it.");
        // Compared line-ending-agnostic: a Windows checkout may have converted the committed file.
        Assert.AreEqual
        (
            generated,
            File.ReadAllText(path).ReplaceLineEndings("\n"),
            $"schemas/{fileName} no longer matches the result types it describes. If the change is intended, " +
            "run the tests with RIX_UPDATE_SCHEMAS=1 and update the workflow bash that reads the changed fields."
        );
    }

    [TestMethod]
    public void CiFailureOutput_OffersEveryStatusThatReachesStdout_AndNotDetected()
    {
        var statuses = Generate("ci-failure-output.schema.json")["anyOf"]!.AsArray()
            .Select(variant => variant!["properties"]!["status"]!["const"]!.GetValue<string>())
            .Order()
            .ToArray();

        CollectionAssert.AreEqual
        (
            new[] { "error", "failure", "loopGuarded", "setupFailure", "skipped", "success", "untrustedRun" },
            statuses
        );
    }

    /// <summary>The schemas claim every property is always written; this holds them to it by
    /// serializing one of each variant exactly the way <see cref="Startup"/> does and comparing the
    /// fields that come out with the ones its schema requires.</summary>
    [TestMethod]
    public void EveryVariant_WritesExactlyTheFieldsItsSchemaRequires()
    {
        const string job = "job-result.schema.json", submit = "submit-result.schema.json", ciFailure = "ci-failure-output.schema.json";
        (string Schema, string Json)[] written =
        [
            (job, JsonSerializer.Serialize<IJobResult>(new JobSuccess([], [], 0m, TimeSpan.Zero), JobJsonContext.Default.IJobResult)),
            (job, JsonSerializer.Serialize<IJobResult>(new JobFailure("boom", 0m, TimeSpan.Zero), JobJsonContext.Default.IJobResult)),
            (job, JsonSerializer.Serialize<IJobResult>(new SetupFailure("boom"), JobJsonContext.Default.IJobResult)),
            (submit, JsonSerializer.Serialize<ISubmitResult>(new SubmitSuccess([], []), SubmitJsonContext.Default.ISubmitResult)),
            (submit, JsonSerializer.Serialize<ISubmitResult>(new SubmitFailure("boom"), SubmitJsonContext.Default.ISubmitResult)),
            (ciFailure, JsonSerializer.Serialize<ICiFailureResult>(new CiFailureSkipped("succeeded"), CiFailureJsonContext.Default.ICiFailureResult)),
            (ciFailure, JsonSerializer.Serialize<ICiFailureResult>(new CiFailureLoopGuarded("rix/fix", 3), CiFailureJsonContext.Default.ICiFailureResult)),
            (ciFailure, JsonSerializer.Serialize<ICiFailureResult>(new CiFailureUntrustedRun("someone/fork", "main"), CiFailureJsonContext.Default.ICiFailureResult)),
            (ciFailure, JsonSerializer.Serialize<ICiFailureResult>(new CiFailureError("boom"), CiFailureJsonContext.Default.ICiFailureResult)),
        ];

        foreach (var (schema, json) in written)
        {
            var result = JsonNode.Parse(json)!.AsObject();
            var status = result["status"]!.GetValue<string>();
            var variant = Generate(schema)["anyOf"]!.AsArray()
                .Single(v => v!["properties"]!["status"]!["const"]!.GetValue<string>() == status)!;
            CollectionAssert.AreEqual
            (
                variant["required"]!.AsArray().Select(r => r!.GetValue<string>()).Order().ToArray(),
                result.Select(p => p.Key).Order().ToArray(),
                $"{json} does not match the {schema} variant for status '{status}'"
            );
        }
    }

    private static JsonNode Generate(string fileName) => fileName switch
    {
        "job-result.schema.json" => Document(
            "The result rix job prints and writes to result.json.",
            Variants(JobJsonContext.Default, typeof(IJobResult))),
        "submit-result.schema.json" => Document(
            "The result rix submit prints.",
            Variants(SubmitJsonContext.Default, typeof(ISubmitResult))),
        // What `rix ci-failure` prints is not one C# type: a run it does not act on prints an
        // ICiFailureResult, and one it does act on prints the job's IJobResult instead. "detected"
        // is the one variant that never reaches stdout - it always leads to the job running - so it
        // is left out rather than letting a fixture carrying it pass as valid.
        "ci-failure-output.schema.json" => Document(
            "The result rix ci-failure prints: a check that never reached the agent, or the job it ran.",
            Variants(CiFailureJsonContext.Default, typeof(ICiFailureResult))
                .Where(variant => variant["properties"]!["status"]!["const"]!.GetValue<string>() != "detected")
                .Concat(Variants(JobJsonContext.Default, typeof(IJobResult)))),
        _ => throw new ArgumentOutOfRangeException(nameof(fileName), fileName, null),
    };

    private static JsonObject Document(string description, IEnumerable<JsonNode> variants) => new()
    {
        ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
        ["description"] = description,
        ["type"] = "object",
        ["required"] = new JsonArray("status"),
        ["anyOf"] = new JsonArray([.. variants]),
    };

    /// <summary>One schema per concrete result type of a polymorphic result, tightened to what rix
    /// actually writes: every property, every time.</summary>
    private static IEnumerable<JsonNode> Variants(IJsonTypeInfoResolver context, Type resultType)
    {
        var options = new JsonSerializerOptions { TypeInfoResolver = context };
        var exporterOptions = new JsonSchemaExporterOptions
        {
            // Generic arguments (the list item types) carry no nullability annotations; rix never
            // writes a null into those lists.
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = Tighten,
        };
        return options.GetJsonSchemaAsNode(resultType, exporterOptions)["anyOf"]!.AsArray()
            .Select(variant => variant!.DeepClone());
    }

    private static JsonNode Tighten(JsonSchemaExporterContext context, JsonNode schema)
    {
        // Value objects like BranchName serialize through a custom converter, which the exporter
        // can only describe as "anything" - but they all write a plain string.
        if (IsStringValue(context.TypeInfo.Converter.GetType()))
            return new JsonObject { ["type"] = "string" };

        if (schema is JsonObject obj && obj["properties"] is JsonObject properties)
        {
            // The serializer writes every property, nulls included (no ignore conditions are set),
            // so a fixture missing one is describing output rix cannot produce. The exporter only
            // requires constructor parameters, which misses inherited and computed ones.
            obj["required"] = new JsonArray([.. properties.Select(p => (JsonNode)p.Key)]);
            // And a fixture with a field rix never writes is most likely carrying a stale name.
            obj["additionalProperties"] = false;
        }
        return schema;
    }

    private static bool IsStringValue(Type? converterType)
    {
        for (; converterType is not null; converterType = converterType.BaseType)
        {
            if (converterType.IsGenericType && converterType.GetGenericTypeDefinition() == typeof(StringValueJsonConverter<>))
                return true;
        }
        return false;
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rix.slnx")))
                return dir.FullName;
        }
        throw new InvalidOperationException("Could not find the repo root (Rix.slnx) above the test binaries.");
    }
}
