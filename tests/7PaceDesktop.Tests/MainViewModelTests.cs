using System.Net;
using PaceDesktop.App.ViewModels;
using PaceDesktop.Core.Models;
using PaceDesktop.Core.Services;
using PaceDesktop.Core.Storage;

namespace PaceDesktop.Tests;

public class MainViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "7PaceDesktopTests", Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class FakeWorkLogClient : IWorkLogClient
    {
        public List<TimeEntry> Submitted = [];
        public Func<TimeEntry, Exception?>? FailWhen;
        public Task SubmitAsync(TimeEntry entry, CancellationToken ct = default)
        {
            if (FailWhen?.Invoke(entry) is { } ex) throw ex;
            Submitted.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyHolidayHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
    }

    private MainViewModel CreateVm(FakeWorkLogClient client)
    {
        var settingsStore = new SettingsStore(_dir);
        var workItemStore = new WorkItemStore(_dir);
        workItemStore.Save([new WorkItem(79023, "Product Development", true), new WorkItem(79055, "Admin & internal", false)]);
        var holidays = new SwedishHolidayService(new HttpClient(new EmptyHolidayHandler()), settingsStore);
        return new MainViewModel(holidays, client, workItemStore, settingsStore);
    }

    [Fact]
    public async Task Generate_PopulatesRows_WithFavoriteWorkItem_AndTotal()
    {
        var vm = CreateVm(new FakeWorkLogClient());
        vm.StartDate = new DateTime(2026, 7, 6);
        vm.EndDate = new DateTime(2026, 7, 10);
        vm.HoursPerDay = 8;

        await vm.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(5, vm.Entries.Count);
        Assert.All(vm.Entries, r => Assert.Equal(79023, r.SelectedWorkItem.Id));
        Assert.Equal(40, vm.TotalHours);
    }

    [Fact]
    public async Task Register_Simulate_SubmitsNothing_MarksOk()
    {
        var client = new FakeWorkLogClient();
        var vm = CreateVm(client);
        vm.StartDate = new DateTime(2026, 7, 6);
        vm.EndDate = new DateTime(2026, 7, 6);
        vm.HoursPerDay = 8;
        await vm.GenerateCommand.ExecuteAsync(null);

        vm.Simulate = true;
        await vm.RegisterCommand.ExecuteAsync(null);

        Assert.Empty(client.Submitted);
        Assert.All(vm.Entries, r => Assert.Equal(RowStatus.Ok, r.Status));
    }

    [Fact]
    public async Task Register_SubmitsAllRows_AndMarksStatus()
    {
        var client = new FakeWorkLogClient();
        var vm = CreateVm(client);
        vm.StartDate = new DateTime(2026, 7, 6);
        vm.EndDate = new DateTime(2026, 7, 10);
        vm.HoursPerDay = 8;
        await vm.GenerateCommand.ExecuteAsync(null);

        await vm.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(5, client.Submitted.Count);
        Assert.All(vm.Entries, r => Assert.Equal(RowStatus.Ok, r.Status));
    }

    [Fact]
    public async Task Register_FailedRow_IsMarkedFailed_AndRetryable()
    {
        var client = new FakeWorkLogClient();
        var failDate = new DateOnly(2026, 7, 8);
        client.FailWhen = e => e.Date == failDate ? new PaceApiException(500, "boom") : null;
        var vm = CreateVm(client);
        vm.StartDate = new DateTime(2026, 7, 6);
        vm.EndDate = new DateTime(2026, 7, 10);
        vm.HoursPerDay = 8;
        await vm.GenerateCommand.ExecuteAsync(null);

        await vm.RegisterCommand.ExecuteAsync(null);

        var failed = Assert.Single(vm.Entries, r => r.Status == RowStatus.Failed);
        Assert.Equal(failDate, failed.Date);
        Assert.Equal(4, client.Submitted.Count);

        client.FailWhen = null;
        await vm.RetryRowCommand.ExecuteAsync(failed);
        Assert.Equal(RowStatus.Ok, failed.Status);
        Assert.Equal(5, client.Submitted.Count);
    }

    [Fact]
    public async Task Generate_ThenEditRowHours_UpdatesTotal()
    {
        var vm = CreateVm(new FakeWorkLogClient());
        vm.StartDate = new DateTime(2026, 7, 6);
        vm.EndDate = new DateTime(2026, 7, 7);
        vm.HoursPerDay = 8;

        await vm.GenerateCommand.ExecuteAsync(null);

        Assert.Equal(16, vm.TotalHours);

        vm.Entries[0].Hours = 4;

        Assert.Equal(12, vm.TotalHours);
    }

    [Fact]
    public async Task RemoveRow_UnsubscribesRow()
    {
        var vm = CreateVm(new FakeWorkLogClient());
        vm.StartDate = new DateTime(2026, 7, 6);
        vm.EndDate = new DateTime(2026, 7, 7);
        vm.HoursPerDay = 8;
        await vm.GenerateCommand.ExecuteAsync(null);

        var removed = vm.Entries[0];
        vm.RemoveRow(removed);
        var totalAfterRemove = vm.TotalHours;

        removed.Hours = 100;

        Assert.Equal(totalAfterRemove, vm.TotalHours);
    }
}
