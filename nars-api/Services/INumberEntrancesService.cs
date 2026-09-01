using NarsApi.DTOs;

namespace NarsApi.Services;

/// <summary>
/// Atomically assigns house-entrance numbers to an ordered list of entrances on
/// a single road. The whole batch is numbered inside one locked transaction so
/// two concurrent clients number the same road without colliding.
/// </summary>
public interface INumberEntrancesService
{
    /// <summary>
    /// Assigns a dense, collision-free sequence of entrance numbers to
    /// <paramref name="entranceIds"/> (ordered by the caller) on
    /// <paramref name="roadId"/>, for the given user.
    /// </summary>
    /// <returns>
    /// The authoritative numbers per entrance. Returns null when the road does
    /// not exist or no entrance belongs to the user+road, so the caller can
    /// respond 404.
    /// </returns>
    /// <exception cref="NumberSeriesExhaustedException">
    /// Thrown when a side's parity series is exhausted (no collision-free number
    /// available), leaving no writes committed and the batch rolled back.
    /// </exception>
    Task<IReadOnlyList<NumberedEntrance>?> NumberAsync(
        Guid userId, Guid roadId, IReadOnlyList<Guid> entranceIds, CancellationToken ct);
}
