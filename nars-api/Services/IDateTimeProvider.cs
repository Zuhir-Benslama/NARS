namespace NarsApi.Services;

/// <summary>Abstracts DateTime.UtcNow for testability.</summary>
public interface IDateTimeProvider
{
    /// <summary>Gets the current UTC date and time.</summary>
    DateTime UtcNow { get; }
}
