using PaceDesktop.App.ViewModels;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.Tests;

// Serialized with other Credential-Manager-backed tests (see CredentialStoreTests).
[Collection("CredentialManager")]
public class SetupViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "7PaceDesktopTests", Guid.NewGuid().ToString("N"));
    private readonly CredentialStore _creds = new();
    private string? _createdOrg;

    public void Dispose()
    {
        if (_createdOrg is not null) _creds.DeleteToken(_createdOrg);
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void CanSave_FalseUntilAllFieldsValid()
    {
        var vm = new SetupViewModel(new SettingsStore(_dir), new WorkItemStore(_dir), _creds);
        Assert.False(vm.CanSave);
        vm.OrganizationName = "unittest-org";
        vm.Token = "tok";
        vm.WorkItemIdText = "not-a-number";
        vm.WorkItemName = "PD";
        Assert.False(vm.CanSave);
        vm.WorkItemIdText = "79023";
        Assert.True(vm.CanSave);
    }

    [Fact]
    public void TrySave_PersistsEverything()
    {
        _createdOrg = "unittest-" + Guid.NewGuid().ToString("N");
        var vm = new SetupViewModel(new SettingsStore(_dir), new WorkItemStore(_dir), _creds)
        {
            OrganizationName = _createdOrg,
            Token = "tok123",
            WorkItemIdText = "79023",
            WorkItemName = "Product Development"
        };

        Assert.True(vm.TrySave());

        Assert.Equal(_createdOrg, new SettingsStore(_dir).Load().OrganizationName);
        Assert.Equal("tok123", _creds.LoadToken(_createdOrg));
        var item = Assert.Single(new WorkItemStore(_dir).Load());
        Assert.True(item.IsFavorite);
        Assert.Equal(79023, item.Id);
    }
}
