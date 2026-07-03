// CONTRACT: verified against 7Pace's official API docs (support.7pace.com / appfire wiki), 2026-07-03.
// Still pending a live 200 from iCore's instance, but the shape below matches the documentation:
//   POST https://{account}.timehub.7pace.com/api/rest/workLogs?api-version=3.2
//   Header: Authorization: Bearer {token}   (token from 7Pace Settings > Reporting and API)
//   Body: { "workItemId": int, "timeStamp": ISO-8601 string, "length": seconds (int) }
//   {account} is the Azure DevOps organization name (e.g. "icore"), NOT the project.
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using PaceDesktop.Core.Models;

namespace PaceDesktop.Core.Services;

public sealed class PaceApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed partial class PaceApiClient(HttpClient http, string organization, string token) : IWorkLogClient
{
    public async Task SubmitAsync(TimeEntry entry, CancellationToken ct = default)
    {
        var url = $"https://{NormalizeAccount(organization)}.timehub.7pace.com/api/rest/workLogs?api-version=3.2";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new
            {
                workItemId = entry.WorkItemId,
                timeStamp = entry.Date.ToDateTime(new TimeOnly(9, 0)).ToString("yyyy-MM-ddTHH:mm:ss"),
                length = (int)(entry.Hours * 3600)
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new PaceApiException((int)response.StatusCode,
                $"7Pace API error {(int)response.StatusCode}: {body}");
        }
    }

    // The 7Pace cloud REST API lives at {account}.timehub.7pace.com, where {account} is the
    // Azure DevOps organization (e.g. "icore"). Tolerate a pasted full host or URL, and reject
    // anything that isn't a valid host label with a clear message — otherwise a stray space or
    // the project name produces an opaque "hostname could not be parsed" URI error.
    public static string NormalizeAccount(string organization)
    {
        var account = (organization ?? string.Empty).Trim();
        if (account.Length == 0)
            throw new ArgumentException("7Pace account name is empty.", nameof(organization));

        account = account.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
                         .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase);
        account = account.Split('/', 2)[0];   // drop any path if a URL was pasted
        account = account.Split('.', 2)[0];   // take the first label if a full host was pasted
        account = account.Trim();

        if (!AccountPattern().IsMatch(account))
            throw new ArgumentException(
                $"'{organization}' is not a valid 7Pace account name. Enter just the account/organization " +
                "(the part before .timehub.7pace.com), e.g. 'icore' — no spaces, project name, or URL.",
                nameof(organization));

        return account;
    }

    [GeneratedRegex("^[A-Za-z0-9-]+$")]
    private static partial Regex AccountPattern();
}
