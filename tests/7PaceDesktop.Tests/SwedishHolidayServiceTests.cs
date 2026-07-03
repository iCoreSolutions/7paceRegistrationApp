using System.Net;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.Tests;

public class SwedishHolidayServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "7PaceDesktopTests", Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; return Task.FromResult(responder(request)); }
    }

    private const string NagerJson =
        """[{"date":"2026-06-24","localName":"Midsommarafton","name":"Midsummer Eve"},{"date":"2026-12-25","localName":"Juldagen","name":"Christmas Day"}]""";

    [Fact]
    public async Task Fetch_ParsesAndCaches()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(NagerJson) });
        var store = new SettingsStore(_dir);
        var service = new SwedishHolidayService(new HttpClient(handler), store);

        var result = await service.GetHolidaysAsync(2026, 2026);

        Assert.False(result.IsIncomplete);
        Assert.Contains(new DateOnly(2026, 6, 24), result.Dates);
        Assert.Contains(new DateOnly(2026, 12, 25), result.Dates);
        Assert.True(store.Load().HolidayCache.ContainsKey(2026));
    }

    [Fact]
    public async Task SecondCall_UsesCache_NoSecondHttpCall()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(NagerJson) });
        var store = new SettingsStore(_dir);
        var service = new SwedishHolidayService(new HttpClient(handler), store);

        await service.GetHolidaysAsync(2026, 2026);
        await service.GetHolidaysAsync(2026, 2026);

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task FetchFails_NoCache_ReturnsIncompleteEmpty()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var service = new SwedishHolidayService(new HttpClient(handler), new SettingsStore(_dir));

        var result = await service.GetHolidaysAsync(2026, 2026);

        Assert.True(result.IsIncomplete);
        Assert.Empty(result.Dates);
    }

    [Fact]
    public async Task YearBoundary_FetchesBothYears()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(NagerJson) });
        var service = new SwedishHolidayService(new HttpClient(handler), new SettingsStore(_dir));

        await service.GetHolidaysAsync(2026, 2027);

        Assert.Equal(2, handler.Calls);
    }
}
