using System.Net;
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
}
