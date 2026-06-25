namespace NarsApi.Services;

public interface IScatteredAreaService
{
    Task RefreshAsync(Guid userId, int communeId, CancellationToken cancellationToken = default);

    (DateTimeOffset Timestamp, string Message)? LastError { get; }
}
