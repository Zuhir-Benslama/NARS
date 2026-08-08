namespace NarsApi.Services;

public interface IScatteredAreaService
{
    /// <summary>
    /// Recomputes scattered areas for the given user and commune.
    /// Returns <c>false</c> when the recomputation failed; details are in
    /// <see cref="GetLastError"/>. OperationCanceledException is not caught.
    /// </summary>
    Task<bool> RefreshAsync(Guid userId, int communeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the last error encountered during scattered area computation for the
    /// given user and commune, if any. Error state is keyed per user+commune so
    /// one account's failure is never surfaced to another.
    /// </summary>
    (DateTimeOffset Timestamp, string Message)? GetLastError(Guid userId, int communeId);
}
