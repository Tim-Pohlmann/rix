using System.Reflection;
using System.Text.RegularExpressions;

namespace Rix.Initialize;

/// <summary>The git ref of the rix repo that the workflows written by <c>rix initialize</c> name
/// after the <c>@</c> in their <c>uses:</c> lines — a tag, branch, or commit SHA. Validated because
/// it is interpolated into a YAML file the caller then commits: anything with whitespace or a quote
/// in it would produce a workflow GitHub rejects at parse time, and the error would surface in the
/// target repo's Actions tab rather than here.</summary>
internal sealed record WorkflowRef
{
    // Deliberately narrower than what git itself accepts: every ref anyone would reasonably pin to
    // (v1, v1.2.3, main, release/2.x, a SHA) fits, and nothing that needs YAML quoting does.
    private static readonly Regex Pattern = new(@"^[A-Za-z0-9][A-Za-z0-9._/-]*$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>The ref <c>rix initialize</c> pins to unless the caller says otherwise: the floating
    /// major-version tag of this very binary's release (<c>v0</c> for any 0.x build), which the
    /// release workflow moves to each new release of that major. Matching the binary's own major
    /// means the workflows a given rix writes call the reusable workflows it was released with,
    /// while still picking up fixes within the major — the same version-skew rule the reusable
    /// workflows already follow when they download their binary, applied one level up.</summary>
    internal static WorkflowRef ForThisBuild { get; } =
        new($"v{MajorOf(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)}");

    internal string Value { get; }

    internal WorkflowRef(string value)
    {
        if (!Pattern.IsMatch(value))
            throw new InvalidInputException($"'{value}' is not a valid git ref; expected a tag, branch, or commit SHA.");
        Value = value;
    }

    public override string ToString() => Value;

    /// <summary>The major of a version stamped into an assembly by <c>Rix.csproj</c>, which arrives
    /// here as <c>0.5.0+&lt;sha&gt;</c>. A build missing or mangling it is a broken build rather than
    /// bad caller input, so it fails the same way a missing embedded template does.</summary>
    internal static string MajorOf(string? informationalVersion)
    {
        var major = informationalVersion?.Split('.')[0];
        if (string.IsNullOrEmpty(major) || !major.All(char.IsAsciiDigit))
            throw new InvalidOperationException($"assembly informational version is not a semantic version: {informationalVersion ?? "<missing>"}");
        return major;
    }
}
