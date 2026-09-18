using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Rix.Repository;

/// <summary>The REST half of talking to GitHub: one authenticated client scoped to one repo, plus
/// the request/status-check/parse sequence every endpoint would otherwise repeat. Split from the
/// hosts so the read and write paths share a connection pool and an error shape by construction
/// rather than by one host holding a reference to another's <see cref="HttpClient"/>.</summary>
internal sealed class GitHubApi
{
    private readonly HttpClient _http;

    /// <summary>The repo every path is resolved against, exposed because callers build query
    /// parameters from it (e.g. the <c>owner:branch</c> head filter).</summary>
    internal RepoIdentifier Repo { get; }

    internal GitHubApi(RepoIdentifier repo, GitReadToken token, HttpMessageHandler? handler = null)
    {
        Repo = repo;
        _http = BuildHttpClient(token, handler);
    }

    private static HttpClient BuildHttpClient(GitReadToken token, HttpMessageHandler? handler)
    {
        var client = handler switch
        {
            null => new HttpClient(),
            var h => new HttpClient(h),
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("rix/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    /// <summary>GETs <paramref name="path"/> without judging the status, for the callers that read
    /// one themselves (a 404 meaning "no such branch") or that need the response as a stream rather
    /// than a parsed body. Everyone else wants <see cref="GetJsonAsync"/>.</summary>
    internal Task<HttpResponseMessage> GetAsync
    (
        string path,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken
    )
    => _http.GetAsync(Url(path), completionOption, cancellationToken);

    /// <summary>GETs <paramref name="path"/> and parses the JSON body, collapsing the
    /// request/status-check/parse sequence every read endpoint would otherwise repeat.
    /// <paramref name="operation"/> names the call in the <see cref="RepositoryHostException"/> a
    /// failed status produces.</summary>
    internal async Task<T> GetJsonAsync<T>(string path, JsonTypeInfo<T> typeInfo, string operation, CancellationToken cancellationToken)
    {
        using var response = await GetAsync(path, HttpCompletionOption.ResponseContentRead, cancellationToken);
        EnsureSuccess(response, operation);
        return await ReadJsonAsync(response, typeInfo, cancellationToken);
    }

    /// <summary>POSTs <paramref name="body"/> to <paramref name="path"/> and parses the response,
    /// so the write path reports a bad status and a malformed body exactly as the read path
    /// does.</summary>
    internal async Task<TResponse> PostJsonAsync<TRequest, TResponse>
    (
        string path,
        TRequest body,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        string operation,
        CancellationToken cancellationToken
    )
    {
        using var content = JsonContent.Create(body, requestTypeInfo);
        using var response = await _http.PostAsync(Url(path), content, cancellationToken);
        EnsureSuccess(response, operation);
        return await ReadJsonAsync(response, responseTypeInfo, cancellationToken);
    }

    /// <summary>Turns any non-2xx response into a <see cref="RepositoryHostException"/> naming the
    /// operation, so every REST call reports an error status the same way instead of leaking
    /// <see cref="HttpRequestException"/> from a bare <c>EnsureSuccessStatusCode</c>. Public to the
    /// assembly because callers of <see cref="GetAsync"/> check the status themselves and still
    /// want this shape for the statuses they don't handle.</summary>
    internal static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            throw new RepositoryHostException($"{operation} failed: {ex.Message}", ex);
        }
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        try
        {
            var value = await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken);
            if (value is null)
                throw new RepositoryHostException($"{typeof(T).Name} response body was empty");
            return value;
        }
        catch (JsonException ex)
        {
            throw new RepositoryHostException($"could not parse {typeof(T).Name} response", ex);
        }
    }

    /// <summary>Builds a URL for <paramref name="path"/> under this api's repo, so the API base
    /// address is written once rather than at every call site.</summary>
    private string Url(string path) => $"https://api.github.com/repos/{Repo.Value}/{path}";
}
