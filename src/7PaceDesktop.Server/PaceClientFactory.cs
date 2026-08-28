using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.Server;

/// <summary>
/// Builds 7Pace clients from the CURRENT settings and stored token, so changing the
/// organization or token in the UI takes effect without restarting the server.
/// </summary>
public interface IPaceClientFactory
{
    IWorkLogReader CreateReader();
    IWorkLogClient CreateClient();
}

/// <summary>
/// Reads and writes the 7Pace token. Production uses Windows Credential Manager; tests
/// substitute an in-memory one so no test writes to the developer's real credential store.
/// </summary>
public interface ITokenSource
{
    string? Load(string organization);
    void Save(string organization, string token);
}

public sealed class CredentialTokenSource(CredentialStore credentials) : ITokenSource
{
    public string? Load(string organization) =>
        string.IsNullOrWhiteSpace(organization) ? null : credentials.LoadToken(organization);

    public void Save(string organization, string token) => credentials.SaveToken(organization, token);
}

public sealed class PaceClientFactory(HttpClient http, SettingsStore settings, ITokenSource tokens)
    : IPaceClientFactory
{
    public IWorkLogReader CreateReader() => Build();
    public IWorkLogClient CreateClient() => Build();

    private PaceApiClient Build()
    {
        var current = settings.Load();
        return new PaceApiClient(http, current.OrganizationName,
            tokens.Load(current.OrganizationName) ?? string.Empty);
    }
}
