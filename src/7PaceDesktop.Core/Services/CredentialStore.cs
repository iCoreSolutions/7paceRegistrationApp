using Meziantou.Framework.Win32;

namespace PaceDesktop.Core.Services;

public sealed class CredentialStore
{
    private static string Key(string organization) => $"7PaceDesktop:{organization}";

    public void SaveToken(string organization, string token) =>
        CredentialManager.WriteCredential(Key(organization), "token", token,
            comment: "7Pace Timetracker API token", CredentialPersistence.LocalMachine);

    public string? LoadToken(string organization) =>
        CredentialManager.ReadCredential(Key(organization))?.Password;

    public void DeleteToken(string organization)
    {
        if (CredentialManager.ReadCredential(Key(organization)) is not null)
            CredentialManager.DeleteCredential(Key(organization));
    }
}
