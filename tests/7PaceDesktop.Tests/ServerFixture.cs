using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;
using PaceDesktop.Server;

namespace PaceDesktop.Tests;

/// <summary>A fake 7Pace whose reads and writes are scripted by the test.</summary>
public sealed class FakePace : IPaceClientFactory, IWorkLogReader, IWorkLogClient
{
    public List<ExistingWorkLog> Existing { get; } = [];
    public List<TimeEntry> Submitted { get; } = [];
    public Exception? ReadThrows { get; set; }
    public Func<TimeEntry, Exception?>? SubmitThrows { get; set; }
    public int ReadCount;

    public IWorkLogReader CreateReader() => this;
    public IWorkLogClient CreateClient() => this;

    public Task<IReadOnlyList<ExistingWorkLog>> GetWorkLogsAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        Interlocked.Increment(ref ReadCount);
        if (ReadThrows is not null) throw ReadThrows;
        return Task.FromResult<IReadOnlyList<ExistingWorkLog>>(
            Existing.Where(l => l.Date >= from && l.Date <= to).ToList());
    }

    public Task SubmitAsync(TimeEntry entry, CancellationToken ct = default)
    {
        if (SubmitThrows?.Invoke(entry) is { } ex) throw ex;
        lock (Submitted) Submitted.Add(entry);
        return Task.CompletedTask;
    }
}

/// <summary>In-memory token store, so no test touches Windows Credential Manager.</summary>
public sealed class FakeTokenSource : ITokenSource
{
    private readonly Dictionary<string, string> _tokens = [];

    public string? Load(string organization) =>
        _tokens.TryGetValue(organization, out var token) ? token : "test-token";

    public void Save(string organization, string token) => _tokens[organization] = token;
}

/// <summary>
/// Hosts the real server against a per-test data directory and a fake 7Pace, so no test
/// touches the user's real settings, work items or credential store.
/// </summary>
public sealed class ServerFixture : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public string DataDir { get; }
    public FakePace Pace { get; } = new();
    public HttpClient Client { get; }
    public SettingsStore Settings { get; }
    public WorkItemStore WorkItems { get; }

    public ServerFixture()
    {
        DataDir = Path.Combine(Path.GetTempPath(), "7pace-server-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataDir);
        Settings = new SettingsStore(DataDir);
        WorkItems = new WorkItemStore(DataDir);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("DataDir", DataDir);
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IPaceClientFactory>(Pace);
                services.AddSingleton<ITokenSource>(new FakeTokenSource());
            });
        });

        Client = _factory.CreateClient();
        Client.DefaultRequestHeaders.Add(ClientHeaderFilter.HeaderName, "1");
    }

    /// <summary>A client without the anti-CSRF header, to prove mutating endpoints reject it.</summary>
    public HttpClient CreateBareClient() => _factory.CreateClient();

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
        try { Directory.Delete(DataDir, recursive: true); } catch (IOException) { }
    }
}
