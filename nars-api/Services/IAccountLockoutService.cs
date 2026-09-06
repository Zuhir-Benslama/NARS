using NarsApi.Models;

namespace NarsApi.Services;

/// <summary>
/// Tracks failed sign-in attempts and applies/clears account lockouts. Split
/// out of <see cref="RefreshTokenService"/>: token lifecycle and failed-login
/// accounting are unrelated concerns, and consumers like
/// <see cref="UserAuthorizationService"/> previously had to inject the whole
/// token service just to record a failed login.
/// </summary>
public interface IAccountLockoutService
{
    /// <summary>Records a failed login attempt and applies lockout if threshold reached.</summary>
    Task RecordFailedLoginAsync(User user, int maxFailedAttempts, int lockoutMinutes, DateTimeOffset utcNow, CancellationToken cancellationToken = default);

    /// <summary>Resets failed login attempts and clears lockout.</summary>
    Task ResetFailedAttemptsIfNeededAsync(User user, CancellationToken cancellationToken = default);
}
