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

    /// <summary>Mods whose name contains <paramref name="text"/>, most downloaded first.</summary>
    Task<IReadOnlyList<NexusMod>> SearchModsAsync(string text, int count, CancellationToken cancellationToken = default);

    /// <summary>The given mods of the configured game. IDs that don't exist (or were removed) are left out.</summary>
    Task<IReadOnlyList<NexusMod>> GetModsAsync(IReadOnlyCollection<int> modIds, CancellationToken cancellationToken = default);

    /// <summary>When each of the mod's files was uploaded, including archived and removed files.</summary>
    Task<IReadOnlyList<DateTimeOffset>> GetFileUploadTimesAsync(int modId, CancellationToken cancellationToken = default);
}

public sealed class NexusApiException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Client for the Nexus Mods v2 GraphQL API (https://graphql.nexusmods.com).</summary>
public sealed class NexusModsClient : INexusModsClient
{
    private const string ModFields = """
        modId
        name
        summary
        author
        createdAt
        updatedAt
        version
        pictureUrl
        adultContent
        uploader { name }
        modCategory { name }
        """;

    private const string ModsQuery = $$"""
        query Mods($filter: ModsFilter, $sort: [ModsSort!], $offset: Int, $count: Int) {
          mods(filter: $filter, sort: $sort, offset: $offset, count: $count) {
            totalCount
            nodes { {{ModFields}} }
          }
        }
        """;

    private const string ModsByUidQuery = $$"""
        query ModsByUid($uids: [ID!]!, $count: Int) {
          modsByUid(uids: $uids, count: $count) {
            totalCount
            nodes { {{ModFields}} }
          }
        }
        """;

    private const string ModFilesQuery = """
        query ModFiles($modId: ID!, $gameId: ID!) {
          modFiles(modId: $modId, gameId: $gameId) { date }
        }
        """;

    private const string GameQuery = """
        query Game($domain: String!) {
          game(domainName: $domain) { id }
        }
        """;

    /// <summary>Most mods requested in one <c>modsByUid</c> call.</summary>
    private const int UidBatchSize = 50;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly NexusOptions _options;
    private readonly ILogger<NexusModsClient> _logger;
    private int? _gameId;

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

    public Task<IReadOnlyList<NexusMod>> GetLatestModsAsync(int count, CancellationToken cancellationToken = default) =>
        QueryNewestPageAsync(0, Math.Clamp(count, 1, _options.PageSize), cancellationToken);

    public async Task<IReadOnlyList<NexusMod>> GetModsCreatedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        var result = new List<NexusMod>();
        for (var pageIndex = 0; pageIndex < _options.MaxPages; pageIndex++)
        {
            var page = await QueryNewestPageAsync(pageIndex * _options.PageSize, _options.PageSize, cancellationToken);
            result.AddRange(page.Where(m => m.CreatedAt >= since));

            // Results are sorted newest first, so once we see an older mod (or run out) we're done.
            if (page.Count < _options.PageSize || page[^1].CreatedAt < since)
                return result;
        }

        _logger.LogWarning("Stopped paging Nexus mods after {Pages} pages; results since {Since} may be incomplete", _options.MaxPages, since);
        return result;
    }

    public async Task<IReadOnlyList<NexusMod>> SearchModsAsync(string text, int count, CancellationToken cancellationToken = default)
    {
        var data = await QueryAsync<ModsData>(ModsQuery, new
        {
            filter = new
            {
                gameDomainName = new[] { new { value = _options.GameDomain, op = "EQUALS" } },
                name = new[] { new { value = text.Trim(), op = "WILDCARD" } },
            },
            sort = new[] { new { downloads = new { direction = "DESC" } } },
            offset = 0,
            count = Math.Clamp(count, 1, _options.PageSize),
        }, cancellationToken);

        return ToMods(data.Mods);
    }

    public async Task<IReadOnlyList<NexusMod>> GetModsAsync(IReadOnlyCollection<int> modIds, CancellationToken cancellationToken = default)
    {
        if (modIds.Count == 0)
            return [];

        // modsByUid is the only batch lookup; a UID is the game ID in the upper 32 bits and the mod ID in the lower ones.
        long gameId = await GetGameIdAsync(cancellationToken);
        var result = new List<NexusMod>();
        foreach (var chunk in modIds.Distinct().Chunk(UidBatchSize))
        {
            var data = await QueryAsync<ModsByUidData>(ModsByUidQuery, new
            {
                uids = chunk.Select(id => ((gameId << 32) | (uint)id).ToString()).ToArray(),
                count = chunk.Length,
            }, cancellationToken);
            result.AddRange(ToMods(data.ModsByUid));
        }

        return result;
    }

    public async Task<IReadOnlyList<DateTimeOffset>> GetFileUploadTimesAsync(int modId, CancellationToken cancellationToken = default)
    {
        var gameId = await GetGameIdAsync(cancellationToken);
        var data = await QueryAsync<ModFilesData>(ModFilesQuery, new { modId = modId.ToString(), gameId = gameId.ToString() }, cancellationToken);
        return (data.ModFiles ?? []).Select(f => DateTimeOffset.FromUnixTimeSeconds(f.Date)).ToList();
    }

    private async Task<IReadOnlyList<NexusMod>> QueryNewestPageAsync(int offset, int count, CancellationToken cancellationToken)
    {
        var data = await QueryAsync<ModsData>(ModsQuery, new
        {
            filter = new
            {
                gameDomainName = new[] { new { value = _options.GameDomain, op = "EQUALS" } },
            },
            sort = new[] { new { createdAt = new { direction = "DESC" } } },
            offset,
            count,
        }, cancellationToken);

        return ToMods(data.Mods);
    }

    /// <summary>The numeric ID of the configured game, looked up once.</summary>
    private async Task<int> GetGameIdAsync(CancellationToken cancellationToken)
    {
        if (_gameId is { } cached)
            return cached;

        var data = await QueryAsync<GameData>(GameQuery, new { domain = _options.GameDomain }, cancellationToken);
        _gameId = data.Game?.Id ?? throw new NexusApiException($"Nexus doesn't know the game \"{_options.GameDomain}\".");
        return _gameId.Value;
    }

    private async Task<TData> QueryAsync<TData>(string query, object variables, CancellationToken cancellationToken)
        where TData : class
    {
        GraphQlResponse<TData>? response;
        try
        {
            using var httpResponse = await _http.PostAsJsonAsync(_options.GraphQlEndpoint, new GraphQlRequest(query, variables), JsonOptions, cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                throw new NexusApiException($"Nexus API returned HTTP {(int)httpResponse.StatusCode}: {Truncate(body)}");
            }

            response = await httpResponse.Content.ReadFromJsonAsync<GraphQlResponse<TData>>(JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new NexusApiException($"Nexus API request failed: {ex.Message}", ex);
        }

        if (response?.Errors is { Count: > 0 } errors)
            throw new NexusApiException("Nexus API error: " + string.Join("; ", errors.Select(e => e.Message)));

        return response?.Data ?? throw new NexusApiException("Nexus API returned no data.");
    }

    private static List<NexusMod> ToMods(ModsConnection? connection) =>
        connection?.Nodes.Select(ToMod).ToList() ?? throw new NexusApiException("Nexus API returned no data.");

    internal static NexusMod ToMod(ModNode node) => new(
        node.ModId,
        node.Name ?? $"Mod #{node.ModId}",
        node.Summary,
        node.Author,
        node.Uploader?.Name,
        node.ModCategory?.Name,
        node.CreatedAt,
        node.PictureUrl,
        node.AdultContent,
        node.UpdatedAt,
        node.Version);

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "…";

    private sealed record GraphQlRequest(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("variables")] object Variables);

    internal sealed record GraphQlResponse<TData>(TData? Data, List<GraphQlError>? Errors);

    internal sealed record GraphQlError(string Message);

    internal sealed record ModsData(ModsConnection? Mods);

    internal sealed record ModsByUidData(ModsConnection? ModsByUid);

    internal sealed record ModFilesData(List<ModFileNode>? ModFiles);

    internal sealed record ModFileNode(long Date);

    internal sealed record GameData(GameNode? Game);

    internal sealed record GameNode(int Id);

    internal sealed record ModsConnection(int TotalCount, List<ModNode> Nodes);

    internal sealed record ModNode(
        int ModId,
        string? Name,
        string? Summary,
        string? Author,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string? Version,
        string? PictureUrl,
        bool AdultContent,
        NamedNode? Uploader,
        NamedNode? ModCategory);

    internal sealed record NamedNode(string? Name);
}
