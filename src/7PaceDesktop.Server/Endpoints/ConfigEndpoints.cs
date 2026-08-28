using PaceDesktop.Core.Models;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.Server;

public static class ConfigEndpoints
{
    private const double MaxDailyHours = 24;

    public static void MapConfigEndpoints(this WebApplication app)
    {
        app.MapGet("/api/config", (SettingsStore store, WorkItemStore items, ITokenSource tokens) =>
        {
            var settings = store.Load();
            var hasToken = !string.IsNullOrWhiteSpace(settings.OrganizationName)
                           && !string.IsNullOrWhiteSpace(tokens.Load(settings.OrganizationName));

            return Results.Ok(new ConfigDto(
                Configured: hasToken && items.Load().Count > 0,
                Organization: settings.OrganizationName,
                DailyHours: settings.DailyHours,
                Theme: settings.Theme.ToString(),
                HasToken: hasToken));
        });

        app.MapPut("/api/config", (ConfigUpdateDto body, SettingsStore store, ITokenSource tokens) =>
        {
            string account;
            try
            {
                account = PaceApiClient.NormalizeAccount(body.Organization);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            if (body.DailyHours is <= 0 or > MaxDailyHours)
                return Results.BadRequest(new { error = "Timmar per dag måste vara mellan 0 och 24." });

            if (!Enum.TryParse<ThemePreference>(body.Theme, ignoreCase: true, out var theme))
                return Results.BadRequest(new { error = $"Okänt tema '{body.Theme}'." });

            var settings = store.Load();
            settings.OrganizationName = account;
            settings.DailyHours = body.DailyHours;
            settings.Theme = theme;
            store.Save(settings);

            // An omitted token means "leave the stored one alone", so the settings view never
            // has to round-trip a secret it is not allowed to read.
            if (!string.IsNullOrWhiteSpace(body.Token)) tokens.Save(account, body.Token);

            return Results.Ok();
        }).AddEndpointFilter<ClientHeaderFilter>();

        app.MapGet("/api/workitems", (WorkItemStore items) =>
            Results.Ok(items.Load().Select(i => new WorkItemDto(i.Id, i.Name, i.IsFavorite))));

        app.MapPut("/api/workitems", (List<WorkItemDto> body, WorkItemStore items) =>
        {
            if (body.Count == 0)
                return Results.BadRequest(new { error = "Minst ett work item krävs." });
            if (body.Count(i => i.IsFavorite) != 1)
                return Results.BadRequest(new { error = "Exakt ett work item måste vara favorit." });
            if (body.Any(i => i.Id <= 0))
                return Results.BadRequest(new { error = "Work item-ID måste vara positivt." });
            if (body.Select(i => i.Id).Distinct().Count() != body.Count)
                return Results.BadRequest(new { error = "Samma work item förekommer flera gånger." });

            items.Save(body.Select(i => new WorkItem(i.Id, i.Name, i.IsFavorite)));
            return Results.Ok();
        }).AddEndpointFilter<ClientHeaderFilter>();
    }
}
