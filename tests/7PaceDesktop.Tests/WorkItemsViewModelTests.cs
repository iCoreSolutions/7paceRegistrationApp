using PaceDesktop.App.ViewModels;
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.Tests;

public class WorkItemsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "7PaceDesktopTests", Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private WorkItemsViewModel CreateVm()
    {
        var store = new WorkItemStore(_dir);
        store.Save([new WorkItem(1, "First", true)]);
        return new WorkItemsViewModel(store);
    }

    [Fact]
    public void Add_AppendsAndPersists()
    {
        var vm = CreateVm();
        vm.NewIdText = "2";
        vm.NewName = "Second";
        vm.AddCommand.Execute(null);

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(2, new WorkItemStore(_dir).Load().Count);
    }

    [Fact]
    public void SetFavorite_MovesFavoriteExclusively()
    {
        var vm = CreateVm();
        vm.NewIdText = "2"; vm.NewName = "Second"; vm.AddCommand.Execute(null);

        vm.SetFavoriteCommand.Execute(vm.Items.First(i => i.Id == 2));

        Assert.True(vm.Items.Single(i => i.Id == 2).IsFavorite);
        Assert.False(vm.Items.Single(i => i.Id == 1).IsFavorite);
        Assert.Single(new WorkItemStore(_dir).Load(), i => i.IsFavorite);
    }

    [Fact]
    public void Remove_LastItem_IsBlocked()
    {
        var vm = CreateVm();
        vm.RemoveCommand.Execute(vm.Items[0]);
        Assert.Single(vm.Items); // still there
    }

    [Fact]
    public void Remove_FavoriteItem_PromotesAnother()
    {
        var vm = CreateVm();
        vm.NewIdText = "2"; vm.NewName = "Second"; vm.AddCommand.Execute(null);

        vm.RemoveCommand.Execute(vm.Items.Single(i => i.Id == 1)); // remove the favorite

        var remaining = Assert.Single(vm.Items);
        Assert.True(remaining.IsFavorite);
    }
}
