using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PaceDesktop.Tests;

public class ServerSmokeTests
{
    [Fact]
    public async Task Health_IsReachable()
    {
        using var server = new ServerFixture();

        var response = await server.Client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MutatingEndpoint_RejectsRequestsWithoutTheClientHeader()
    {
        // A page on another origin cannot set a custom header without a preflight, and no CORS
        // policy is configured, so this header is what keeps the local API private to the SPA.
        using var server = new ServerFixture();
        using var bare = server.CreateBareClient();

        var response = await bare.PutAsJsonAsync("/api/config",
            new { organization = "icore", token = (string?)null, dailyHours = 8.0, theme = "System" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReadEndpoint_DoesNotRequireTheClientHeader()
    {
        using var server = new ServerFixture();
        using var bare = server.CreateBareClient();

        Assert.Equal(HttpStatusCode.OK, (await bare.GetAsync("/api/health")).StatusCode);
    }

    [Fact]
    public void PortSetting_RoundTripsThroughConfiguration()
    {
        // Kestrel's real Listen() never binds a socket under WebApplicationFactory (it hosts the
        // app in-memory via TestServer), so a real bound port cannot be observed here. This
        // instead asserts the value Program.cs actually reads: a "Port" configuration key set
        // the same way the real launcher sets it (a command-line-style setting), round-tripping
        // through to builder.Configuration inside the running app.
        using var server = new ServerFixture(settings: new Dictionary<string, string?> { ["Port"] = "5111" });

        var configuration = server.Services.GetRequiredService<IConfiguration>();

        Assert.Equal("5111", configuration["Port"]);
    }
}
