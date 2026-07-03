using PaceDesktop.Core.Services;

namespace PaceDesktop.Tests;

// Serialized with other Credential-Manager-backed tests: concurrent access to the real
// Windows Credential Manager (even with unique keys) races and fails intermittently.
[Collection("CredentialManager")]
public class CredentialStoreTests
{
    [Fact]
    public void SaveLoadDelete_RoundTrips()
    {
        var store = new CredentialStore();
        var org = "unittest-" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.Null(store.LoadToken(org));
            store.SaveToken(org, "secret-token-123");
            Assert.Equal("secret-token-123", store.LoadToken(org));
        }
        finally
        {
            store.DeleteToken(org);
        }
        Assert.Null(store.LoadToken(org));
    }
}
