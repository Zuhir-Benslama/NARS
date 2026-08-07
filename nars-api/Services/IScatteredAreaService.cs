namespace NarsApi.Services;

public interface IScatteredAreaService
{
    /// <summary>
    /// Recomputes scattered areas for the given user and commune.
    /// Returns <c>false</c> when the recomputation failed; details are in
    /// <see cref="LastError"/>. OperationCanceledException is not caught.
    /// </summary>
    Task<bool> RefreshAsync(Guid userId, int communeId, CancellationToken cancellationToken = default);

    /// <summary>Gets the last error encountered during scattered area computation, if any.</summary>
    (DateTimeOffset Timestamp, string Message)? LastError { get; }
}
