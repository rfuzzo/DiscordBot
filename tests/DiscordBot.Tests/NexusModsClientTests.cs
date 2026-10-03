using System.Net;
using System.Text;
using System.Text.Json;
using DiscordBot.Nexus;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests;

public class NexusModsClientTests
{
    private static NexusModsClient CreateClient(StubHandler handler, int pageSize = 2) =>
        new(new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new NexusOptions { ApiKey = "secret", PageSize = pageSize }),
            NullLogger<NexusModsClient>.Instance);

    private static string Page(params (int Id, string Name, string CreatedAt)[] mods) => JsonSerializer.Serialize(new
    {
        data = new
        {
            mods = new
            {
                totalCount = 100,
                nodes = mods.Select(m => new
                {
                    modId = m.Id,
                    name = m.Name,
                    summary = "summary",
                    author = "Author",
                    createdAt = m.CreatedAt,
                    pictureUrl = (string?)null,
                    adultContent = false,
                    uploader = new { name = "Uploader" },
                    modCategory = new { name = "Gameplay" },
                }),
            },
        },
    });

    [Fact]
    public async Task ParsesModsAndSendsTheGameFilter()
    {
        var handler = new StubHandler(Page((42, "Johnny Jacket", "2026-10-01T10:00:00Z")));
        var mods = await CreateClient(handler, pageSize: 5).GetLatestModsAsync(5);

        var mod = Assert.Single(mods);
        Assert.Equal(42, mod.ModId);
        Assert.Equal("Johnny Jacket", mod.Name);
        Assert.Equal("Gameplay", mod.Category);
        Assert.Equal("Uploader", mod.Uploader);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero), mod.CreatedAt);
        Assert.Equal("https://www.nexusmods.com/cyberpunk2077/mods/42", mod.Url("cyberpunk2077"));

        var request = Assert.Single(handler.Requests);
        Assert.Contains("\"gameDomainName\"", request.Body);
        Assert.Contains("\"cyberpunk2077\"", request.Body);
        Assert.Equal("secret", request.ApiKey);
    }

    [Fact]
    public async Task PagesUntilModsAreOlderThanTheCutoff()
    {
        var handler = new StubHandler(
            Page((5, "e", "2026-10-03T00:00:00Z"), (4, "d", "2026-10-02T00:00:00Z")),
            Page((3, "c", "2026-10-01T12:00:00Z"), (2, "b", "2026-09-30T00:00:00Z")),
            Page((1, "a", "2026-09-29T00:00:00Z")));

        var mods = await CreateClient(handler).GetModsCreatedSinceAsync(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal([5, 4, 3], mods.Select(m => m.ModId));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GraphQlErrorsBecomeNexusApiExceptions()
    {
        var handler = new StubHandler("""{"errors":[{"message":"Field 'nope' doesn't exist"}]}""");
        var ex = await Assert.ThrowsAsync<NexusApiException>(() => CreateClient(handler).GetLatestModsAsync(1));
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public async Task HttpErrorsBecomeNexusApiExceptions()
    {
        var handler = new StubHandler("rate limited") { Status = HttpStatusCode.TooManyRequests };
        var ex = await Assert.ThrowsAsync<NexusApiException>(() => CreateClient(handler).GetLatestModsAsync(1));
        Assert.Contains("429", ex.Message);
    }

    private sealed class StubHandler(params string[] responses) : HttpMessageHandler
    {
        private int _index;

        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public List<(string Body, string? ApiKey)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Requests.Add((body, request.Headers.TryGetValues("apikey", out var v) ? v.Single() : null));
            var response = responses[Math.Min(_index++, responses.Length - 1)];
            return new HttpResponseMessage(Status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
