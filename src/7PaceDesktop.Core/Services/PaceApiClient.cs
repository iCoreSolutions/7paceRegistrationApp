// CONTRACT: UNVERIFIED against a live 7Pace instance — to be confirmed in Task 10 (live verification).
// Best-effort shape carried from the plan:
//   POST https://{org}.timetracker.7pace.com/api/rest/workLogs?api-version=3.2
//   Header: Authorization: Bearer {token}
//   Body: { "workItemId": int, "timestamp": "yyyy-MM-ddTHH:mm:ss", "length": seconds (int) }
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PaceDesktop.Core.Models;

namespace PaceDesktop.Core.Services;

public sealed class PaceApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class PaceApiClient(HttpClient http, string organization, string token) : IWorkLogClient
{
    public async Task SubmitAsync(TimeEntry entry, CancellationToken ct = default)
    {
        var url = $"https://{organization}.timetracker.7pace.com/api/rest/workLogs?api-version=3.2";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new
            {
                workItemId = entry.WorkItemId,
                timestamp = entry.Date.ToDateTime(new TimeOnly(9, 0)).ToString("yyyy-MM-ddTHH:mm:ss"),
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
}
