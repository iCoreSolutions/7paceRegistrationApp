using System.Net;
using System.Text;
using System.Text.Json;
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Services;

namespace PaceDesktop.Tests;

public class PaceApiClientTests
{
    private sealed class CapturingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent("{}") };
        }
    }

    [Fact]
    public async Task Submit_SendsExpectedRequest()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK);
        var client = new PaceApiClient(new HttpClient(handler), "icore", "tok123");

        await client.SubmitAsync(new TimeEntry(new DateOnly(2026, 7, 1), 7.5, 79023));

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.StartsWith("https://icore.timehub.7pace.com/api/rest/workLogs", handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("tok123", handler.Request.Headers.Authorization.Parameter);

        using var doc = JsonDocument.Parse(handler.Body!);
        Assert.Equal(79023, doc.RootElement.GetProperty("workItemId").GetInt32());
        Assert.Equal(27000, doc.RootElement.GetProperty("length").GetInt32()); // 7.5h in seconds
        Assert.StartsWith("2026-07-01T", doc.RootElement.GetProperty("timeStamp").GetString());
    }

    [Theory]
    [InlineData("icore", "icore")]
    [InlineData("  icore  ", "icore")]
    [InlineData("https://icore.timehub.7pace.com/api", "icore")]
    [InlineData("icore.timehub.7pace.com", "icore")]
    public void NormalizeAccount_ExtractsAccountLabel(string input, string expected)
    {
        Assert.Equal(expected, PaceApiClient.NormalizeAccount(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("iCore v3")] // project name with a space is not a valid account
    public void NormalizeAccount_RejectsInvalidInput(string input)
    {
        Assert.Throws<ArgumentException>(() => PaceApiClient.NormalizeAccount(input));
    }

    [Fact]
    public async Task Submit_NonSuccess_ThrowsWithStatusCode()
    {
        var handler = new CapturingHandler(HttpStatusCode.Unauthorized);
        var client = new PaceApiClient(new HttpClient(handler), "icore", "bad");

        var ex = await Assert.ThrowsAsync<PaceApiException>(() =>
            client.SubmitAsync(new TimeEntry(new DateOnly(2026, 7, 1), 8, 79023)));
        Assert.Equal(401, ex.StatusCode);
    }

    // A handler that answers a scripted queue of bodies and records every request URI.
    private sealed class SequenceHandler(params string[] bodies) : HttpMessageHandler
    {
        public readonly List<string> Urls = [];
        private int _index;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            var body = _index < bodies.Length ? bodies[_index] : "{\"data\":{\"workLogs\":[]}}";
            _index++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private static string PageOf(int count)
    {
        var logs = Enumerable.Range(0, count).Select(i =>
            $"{{\"id\":\"w{i}\",\"timeStamp\":\"2026-06-{1 + (i % 28):00}T09:00:00\",\"length\":3600,\"workItemId\":42}}");
        return "{\"data\":{\"workLogs\":[" + string.Join(",", logs) + "]}}";
    }

    [Fact]
    public async Task GetWorkLogs_SendsExpectedRequest()
    {
        var handler = new SequenceHandler(PageOf(1));
        var client = new PaceApiClient(new HttpClient(handler), "icore", "tok123");

        await client.GetWorkLogsAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        var url = handler.Urls.Single();
        Assert.StartsWith("https://icore.timehub.7pace.com/api/rest/workLogs?api-version=3.2", url);
        Assert.Contains("$fromTimestamp=2026-06-01T00:00:00", url);
        // Both bounds are exclusive in the 7Pace API, so the day after the range end is sent.
        Assert.Contains("$toTimestamp=2026-07-01T00:00:00", url);
        Assert.Contains("$count=500", url);
        Assert.Contains("$skip=0", url);
    }

    [Fact]
    public async Task GetWorkLogs_ParsesFields()
    {
        const string body = """
            {"data":{"workLogs":[
              {"id":"abc","timeStamp":"2026-06-03T09:00:00","length":21600,"workItemId":12345,"comment":"hej"}
            ]}}
            """;
        var client = new PaceApiClient(new HttpClient(new SequenceHandler(body)), "icore", "t");

        var logs = await client.GetWorkLogsAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        var log = Assert.Single(logs);
        Assert.Equal("abc", log.Id);
        Assert.Equal(new DateOnly(2026, 6, 3), log.Date);
        Assert.Equal(6, log.Hours);          // 21600 seconds
        Assert.Equal(12345, log.WorkItemId);
        Assert.Equal("hej", log.Comment);
    }

    [Fact]
    public async Task GetWorkLogs_PagesUntilShortPage()
    {
        // 500 rows means "there may be more"; the second page is short and ends the loop.
        var handler = new SequenceHandler(PageOf(500), PageOf(3));
        var client = new PaceApiClient(new HttpClient(handler), "icore", "t");

        var logs = await client.GetWorkLogsAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        Assert.Equal(503, logs.Count);
        Assert.Equal(2, handler.Urls.Count);
        Assert.Contains("$skip=0", handler.Urls[0]);
        Assert.Contains("$skip=500", handler.Urls[1]);
    }

    [Fact]
    public async Task GetWorkLogs_FiltersToRequestedRange()
    {
        // The API's bounds are exclusive; the client filters client-side so boundary
        // semantics cannot leak a neighbouring day into the result.
        const string body = """
            {"data":{"workLogs":[
              {"id":"a","timeStamp":"2026-05-31T09:00:00","length":3600,"workItemId":1},
              {"id":"b","timeStamp":"2026-06-01T00:00:00","length":3600,"workItemId":1},
              {"id":"c","timeStamp":"2026-06-30T23:30:00","length":3600,"workItemId":1},
              {"id":"d","timeStamp":"2026-07-01T09:00:00","length":3600,"workItemId":1}
            ]}}
            """;
        var client = new PaceApiClient(new HttpClient(new SequenceHandler(body)), "icore", "t");

        var logs = await client.GetWorkLogsAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        Assert.Equal(["b", "c"], logs.Select(l => l.Id));
    }

    [Fact]
    public async Task GetWorkLogs_ThrowsOnError()
    {
        var client = new PaceApiClient(new HttpClient(new CapturingHandler(HttpStatusCode.Unauthorized)), "icore", "t");

        var ex = await Assert.ThrowsAsync<PaceApiException>(
            () => client.GetWorkLogsAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));
        Assert.Equal(401, ex.StatusCode);
    }

    [Theory]
    // The response envelope is unverified, so the parser accepts the three plausible shapes.
    [InlineData("""{"data":{"workLogs":[{"id":"x","timeStamp":"2026-06-02T09:00:00","length":3600,"workItemId":7}]}}""")]
    [InlineData("""{"data":[{"id":"x","timeStamp":"2026-06-02T09:00:00","length":3600,"workItemId":7}]}""")]
    [InlineData("""[{"id":"x","timeStamp":"2026-06-02T09:00:00","length":3600,"workItemId":7}]""")]
    public void ParseWorkLogs_AcceptsEachEnvelope(string json)
    {
        using var doc = JsonDocument.Parse(json);

        var log = Assert.Single(PaceApiClient.ParseWorkLogs(doc.RootElement));
        Assert.Equal("x", log.Id);
        Assert.Equal(new DateOnly(2026, 6, 2), log.Date);
        Assert.Equal(1, log.Hours);
        Assert.Equal(7, log.WorkItemId);
        Assert.Null(log.Comment);
    }

    [Fact]
    public void ParseWorkLogs_IsCaseInsensitiveAndSkipsUnusableRows()
    {
        const string json = """
            {"Data":{"WorkLogs":[
              {"Id":"x","TimeStamp":"2026-06-02T09:00:00","Length":3600,"WorkItemId":7},
              {"id":"broken","length":3600,"workItemId":7}
            ]}}
            """;
        using var doc = JsonDocument.Parse(json);

        var log = Assert.Single(PaceApiClient.ParseWorkLogs(doc.RootElement));
        Assert.Equal("x", log.Id);
    }
}
