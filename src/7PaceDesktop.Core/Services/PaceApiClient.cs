// CONTRACT: verified against 7Pace's official API docs (support.7pace.com / appfire wiki), 2026-07-03.
// Still pending a live 200 from iCore's instance, but the shape below matches the documentation:
//   POST https://{account}.timehub.7pace.com/api/rest/workLogs?api-version=3.2
//   Header: Authorization: Bearer {token}   (token from 7Pace Settings > Reporting and API)
//   Body: { "workItemId": int, "timeStamp": ISO-8601 string, "length": seconds (int) }
//   {account} is the Azure DevOps organization name (e.g. "icore"), NOT the project.
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using PaceDesktop.Core.Models;

namespace PaceDesktop.Core.Services;

public sealed class PaceApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed partial class PaceApiClient(HttpClient http, string organization, string token)
    : IWorkLogClient, IWorkLogReader
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

    private const int PageSize = 500;

    public async Task<IReadOnlyList<ExistingWorkLog>> GetWorkLogsAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var account = NormalizeAccount(organization);
        var all = new List<ExistingWorkLog>();

        for (var skip = 0; ; skip += PageSize)
        {
            // Both bounds are exclusive in the 7Pace API, so the upper bound is the day after
            // the range end. The result is filtered below regardless, so the exact semantics
            // of the bounds cannot change what the caller sees.
            var url = $"https://{account}.timehub.7pace.com/api/rest/workLogs?api-version=3.2"
                    + $"&$fromTimestamp={from:yyyy-MM-dd}T00:00:00"
                    + $"&$toTimestamp={to.AddDays(1):yyyy-MM-dd}T00:00:00"
                    + $"&$count={PageSize}&$skip={skip}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                throw new PaceApiException((int)response.StatusCode,
                    $"7Pace API error {(int)response.StatusCode}: {error}");
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var page = ParseWorkLogs(doc.RootElement);
            all.AddRange(page);
            if (page.Count < PageSize) break;
        }

        return all.Where(l => l.Date >= from && l.Date <= to).ToList();
    }

    /// <summary>
    /// Reads worklogs out of a response body. The envelope is UNVERIFIED against a live
    /// instance, so all three plausible shapes are accepted: {data:{workLogs:[]}}, {data:[]}
    /// and a bare array, with case-insensitive property lookup. Confirm before release.
    /// </summary>
    public static IReadOnlyList<ExistingWorkLog> ParseWorkLogs(JsonElement root)
    {
        if (FindArray(root) is not { } items) return [];

        var logs = new List<ExistingWorkLog>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!TryGet(item, "timeStamp", out var stamp)) continue;
            var text = stamp.GetString();
            if (text is null || text.Length < 10) continue;
            if (!DateOnly.TryParse(text[..10], out var date)) continue;

            var seconds = TryGet(item, "length", out var len) && len.ValueKind == JsonValueKind.Number
                ? len.GetDouble()
                : 0;
            var workItemId = TryGet(item, "workItemId", out var wi) && wi.ValueKind == JsonValueKind.Number
                ? wi.GetInt32()
                : 0;
            var id = TryGet(item, "id", out var idEl)
                ? idEl.ValueKind == JsonValueKind.String ? idEl.GetString()! : idEl.ToString()
                : string.Empty;
            var comment = TryGet(item, "comment", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;

            logs.Add(new ExistingWorkLog(id, date, seconds / 3600.0, workItemId, comment));
        }
        return logs;
    }

    private static JsonElement? FindArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) return root;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!TryGet(root, "data", out var data)) return null;
        if (data.ValueKind == JsonValueKind.Array) return data;
        if (data.ValueKind == JsonValueKind.Object && TryGet(data, "workLogs", out var logs)
            && logs.ValueKind == JsonValueKind.Array) return logs;
        return null;
    }

    // 7Pace's casing is not guaranteed, so property lookup is case-insensitive.
    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
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
