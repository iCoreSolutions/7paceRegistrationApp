using PaceDesktop.Core.Models;

namespace PaceDesktop.Core.Services;

public interface IWorkLogClient
{
    Task SubmitAsync(TimeEntry entry, CancellationToken ct = default);
}
