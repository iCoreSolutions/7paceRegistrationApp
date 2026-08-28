using PaceDesktop.Core.Models;

namespace PaceDesktop.Core.Services;

public interface IWorkLogReader
{
    /// <summary>Worklogs for the token owner, inclusive of both bounds.</summary>
    Task<IReadOnlyList<ExistingWorkLog>> GetWorkLogsAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
