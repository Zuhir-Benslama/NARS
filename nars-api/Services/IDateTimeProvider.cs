namespace NarsApi.Services;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
