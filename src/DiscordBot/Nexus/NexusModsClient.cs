using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordBot.Nexus;

public interface INexusModsClient
{
    /// <summary>The newest mods for the configured game, newest first.</summary>
    Task<IReadOnlyList<NexusMod>> GetLatestModsAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>All mods for the configured game created at or after <paramref name="since"/>, newest first.</summary>
    Task<IReadOnlyList<NexusMod>> GetModsCreatedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);
}

public sealed class NexusApiException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Client for the Nexus Mods v2 GraphQL API (https://graphql.nexusmods.com).</summary>
public sealed class NexusModsClient : INexusModsClient
{
    private const string ModsQuery = """
        query RecentMods($filter: ModsFilter, $offset: Int, $count: Int) {
          mods(filter: $filter, sort: [{ createdAt: { direction: DESC } }], offset: $offset, count: $count) {
            totalCount
            nodes {
              modId
              name
              summary
              author
              createdAt
              pictureUrl
              adultContent
              uploader { name }
              modCategory { name }
            }
          }
        }
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly NexusOptions _options;
    private readonly ILogger<NexusModsClient> _logger;

    public NexusModsClient(HttpClient http, IOptions<NexusOptions> options, ILogger<NexusModsClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Add("Application-Name", "DiscordBot");
        _http.DefaultRequestHeaders.Add("Application-Version", "1.0.0");
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            _http.DefaultRequestHeaders.Add("apikey", _options.ApiKey);
    }

    public async Task<IReadOnlyList<NexusMod>> GetLatestModsAsync(int count, CancellationToken cancellationToken = default)
    {
        var page = await QueryPageAsync(0, Math.Clamp(count, 1, _options.PageSize), cancellationToken);
        return page.Nodes;
    }

    public async Task<IReadOnlyList<NexusMod>> GetModsCreatedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        var result = new List<NexusMod>();
        for (var pageIndex = 0; pageIndex < _options.MaxPages; pageIndex++)
        {
            var page = await QueryPageAsync(pageIndex * _options.PageSize, _options.PageSize, cancellationToken);
            result.AddRange(page.Nodes.Where(m => m.CreatedAt >= since));

            // Results are sorted newest first, so once we see an older mod (or run out) we're done.
            if (page.Nodes.Count < _options.PageSize || page.Nodes[^1].CreatedAt < since)
                return result;
        }

        _logger.LogWarning("Stopped paging Nexus mods after {Pages} pages; results since {Since} may be incomplete", _options.MaxPages, since);
        return result;
    }

    private async Task<ModsPage> QueryPageAsync(int offset, int count, CancellationToken cancellationToken)
    {
        var request = new GraphQlRequest(ModsQuery, new
        {
            filter = new
            {
                gameDomainName = new[] { new { value = _options.GameDomain, op = "EQUALS" } },
            },
            offset,
            count,
        });

        GraphQlResponse? response;
        try
        {
            using var httpResponse = await _http.PostAsJsonAsync(_options.GraphQlEndpoint, request, JsonOptions, cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                throw new NexusApiException($"Nexus API returned HTTP {(int)httpResponse.StatusCode}: {Truncate(body)}");
            }

            response = await httpResponse.Content.ReadFromJsonAsync<GraphQlResponse>(JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new NexusApiException($"Nexus API request failed: {ex.Message}", ex);
        }

        if (response?.Errors is { Count: > 0 } errors)
            throw new NexusApiException("Nexus API error: " + string.Join("; ", errors.Select(e => e.Message)));

        var mods = response?.Data?.Mods ?? throw new NexusApiException("Nexus API returned no data.");
        return new ModsPage(mods.Nodes.Select(ToMod).ToList());
    }

    internal static NexusMod ToMod(ModNode node) => new(
        node.ModId,
        node.Name ?? $"Mod #{node.ModId}",
        node.Summary,
        node.Author,
        node.Uploader?.Name,
        node.ModCategory?.Name,
        node.CreatedAt,
        node.PictureUrl,
        node.AdultContent);

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "…";

    private sealed record ModsPage(IReadOnlyList<NexusMod> Nodes);

    private sealed record GraphQlRequest(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("variables")] object Variables);

    internal sealed record GraphQlResponse(ModsData? Data, List<GraphQlError>? Errors);

    internal sealed record GraphQlError(string Message);

    internal sealed record ModsData(ModsConnection? Mods);

    internal sealed record ModsConnection(int TotalCount, List<ModNode> Nodes);

    internal sealed record ModNode(
        int ModId,
        string? Name,
        string? Summary,
        string? Author,
        DateTimeOffset CreatedAt,
        string? PictureUrl,
        bool AdultContent,
        NamedNode? Uploader,
        NamedNode? ModCategory);

    internal sealed record NamedNode(string? Name);
}
