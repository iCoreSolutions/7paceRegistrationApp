using System.Net;
using System.Net.Http.Json;
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Storage;
using PaceDesktop.Server;

namespace PaceDesktop.Tests;

public class ConfigEndpointTests
{
    [Fact]
    public async Task GetConfig_ReportsNotConfiguredOnAFreshInstall()
    {
        using var server = new ServerFixture();

        var config = await server.Client.GetFromJsonAsync<ConfigDto>("/api/config");

        Assert.NotNull(config);
        Assert.False(config.Configured);
        Assert.Equal(string.Empty, config.Organization);
        Assert.Equal(8, config.DailyHours);
        Assert.Equal("System", config.Theme);
    }

    [Fact]
    public async Task GetConfig_NeverReturnsTheToken()
    {
        using var server = new ServerFixture();
        server.Settings.Save(new AppSettings { OrganizationName = "icore", DailyHours = 7 });

        var body = await server.Client.GetStringAsync("/api/config");

        Assert.DoesNotContain("test-token", body);
        Assert.DoesNotContain("\"token\"", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PutConfig_PersistsOrganizationDailyHoursAndTheme()
    {
        using var server = new ServerFixture();

        var response = await server.Client.PutAsJsonAsync("/api/config",
            new ConfigUpdateDto("icore", null, 6, "Dark"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = server.Settings.Load();
        Assert.Equal("icore", saved.OrganizationName);
        Assert.Equal(6, saved.DailyHours);
        Assert.Equal(ThemePreference.Dark, saved.Theme);
    }

    [Fact]
    public async Task PutConfig_NormalisesAPastedUrlToTheAccountLabel()
    {
        using var server = new ServerFixture();

        await server.Client.PutAsJsonAsync("/api/config",
            new ConfigUpdateDto("https://icore.timehub.7pace.com/api", null, 8, "System"));

        Assert.Equal("icore", server.Settings.Load().OrganizationName);
    }

    [Fact]
    public async Task PutConfig_RejectsAnInvalidOrganization()
    {
        using var server = new ServerFixture();

        var response = await server.Client.PutAsJsonAsync("/api/config",
            new ConfigUpdateDto("iCore v3", null, 8, "System"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(25)]
    public async Task PutConfig_RejectsAnImpossibleDailyTarget(double hours)
    {
        using var server = new ServerFixture();

        var response = await server.Client.PutAsJsonAsync("/api/config",
            new ConfigUpdateDto("icore", null, hours, "System"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutConfig_OmittingTheTokenPreservesTheStoredOne()
    {
        // FakeTokenSource only returns its "test-token" fallback for an organization it has
        // never seen a Save call for; once Save has run for "icore", a second PUT that skips
        // the token must leave that entry alone rather than overwriting it with an empty one -
        // which would flip HasToken to false below.
        using var server = new ServerFixture();
        await server.Client.PutAsJsonAsync("/api/config", new ConfigUpdateDto("icore", "secret-token", 8, "System"));

        var response = await server.Client.PutAsJsonAsync("/api/config",
            new ConfigUpdateDto("icore", null, 8, "System"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var config = await server.Client.GetFromJsonAsync<ConfigDto>("/api/config");
        Assert.True(config!.HasToken);
    }

    [Fact]
    public async Task PutConfig_RejectsAnUnknownTheme()
    {
        using var server = new ServerFixture();

        var response = await server.Client.PutAsJsonAsync("/api/config",
            new ConfigUpdateDto("icore", null, 8, "Neon"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutWorkItems_RequiresExactlyOneFavourite()
    {
        using var server = new ServerFixture();

        var none = await server.Client.PutAsJsonAsync("/api/workitems",
            new[] { new WorkItemDto(1, "A", false), new WorkItemDto(2, "B", false) });
        var two = await server.Client.PutAsJsonAsync("/api/workitems",
            new[] { new WorkItemDto(1, "A", true), new WorkItemDto(2, "B", true) });

        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, two.StatusCode);
    }

    [Fact]
    public async Task PutWorkItems_RejectsAnEmptyList()
    {
        using var server = new ServerFixture();

        var response = await server.Client.PutAsJsonAsync("/api/workitems", Array.Empty<WorkItemDto>());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutWorkItems_RejectsANonPositiveId()
    {
        using var server = new ServerFixture();

        var response = await server.Client.PutAsJsonAsync("/api/workitems",
            new[] { new WorkItemDto(0, "A", true) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutWorkItems_RejectsDuplicateIds()
    {
        using var server = new ServerFixture();

        var response = await server.Client.PutAsJsonAsync("/api/workitems",
            new[] { new WorkItemDto(1, "A", true), new WorkItemDto(1, "A again", false) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutWorkItems_ThenGetWorkItems_RoundTrips()
    {
        using var server = new ServerFixture();

        await server.Client.PutAsJsonAsync("/api/workitems",
            new[] { new WorkItemDto(12345, "Sprintarbete", true), new WorkItemDto(12401, "Support", false) });
        var items = await server.Client.GetFromJsonAsync<List<WorkItemDto>>("/api/workitems");

        Assert.NotNull(items);
        Assert.Equal([12345, 12401], items.Select(i => i.Id));
        Assert.Single(items, i => i.IsFavorite);
        Assert.Equal([12345, 12401], server.WorkItems.Load().Select(i => i.Id));
    }

    [Fact]
    public async Task PutWorkItems_RejectsRequestsWithoutTheClientHeader()
    {
        // ServerSmokeTests already covers PUT /api/config; this pins the same guarantee for
        // PUT /api/workitems since ClientHeaderFilter is attached per endpoint, not globally.
        using var server = new ServerFixture();
        using var bare = server.CreateBareClient();

        var response = await bare.PutAsJsonAsync("/api/workitems",
            new[] { new WorkItemDto(1, "A", true) });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetConfig_IsConfiguredOnceOrganizationTokenAndWorkItemsExist()
    {
        using var server = new ServerFixture();
        server.Settings.Save(new AppSettings { OrganizationName = "icore" });
        server.WorkItems.Save([new WorkItem(1, "A", true)]);

        var config = await server.Client.GetFromJsonAsync<ConfigDto>("/api/config");

        Assert.True(config!.Configured);
        Assert.True(config.HasToken);
    }
}
