using System.Net;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;
using PaceDesktop.Server;

var builder = WebApplication.CreateBuilder(args);

// Bind loopback-only by IP literal, never 0.0.0.0 and never a hostname (WebApplicationFactory
// ignores this in tests - it hosts the app in-memory via TestServer instead of real Kestrel).
// Loopback-only is the security property and is not configurable. The PORT is, so the Vite dev
// proxy can target a known one; 0 asks the OS for a free port, which is what a real run uses.
var port = int.TryParse(builder.Configuration["Port"], out var configured) ? configured : 0;
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port));

// Tests point this at a temp directory; production uses %AppData%\7PaceDesktop.
var dataDir = builder.Configuration["DataDir"] ?? AppPaths.DefaultBaseDir;

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new HttpClient());
builder.Services.AddSingleton(new SettingsStore(dataDir));
builder.Services.AddSingleton(new WorkItemStore(dataDir));
builder.Services.AddSingleton<CredentialStore>();
builder.Services.AddSingleton<ITokenSource, CredentialTokenSource>();
builder.Services.AddSingleton(sp => new SwedishHolidayService(
    sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<SettingsStore>()));
builder.Services.AddSingleton<IPaceClientFactory, PaceClientFactory>();

var app = builder.Build();

// No CORS policy is configured on purpose - see ClientHeaderFilter.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Text("ok"));
app.MapConfigEndpoints();
app.MapMonthEndpoints();
app.MapRegisterEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Named entry point so WebApplicationFactory&lt;Program&gt; can host the app in tests.</summary>
public partial class Program;
