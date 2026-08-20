namespace NarsApi.Infrastructure;

/// <summary>
/// Shared cookie options factory for auth cookies.
/// Used by both controllers and services that set auth cookies.
/// </summary>
public static class CookieHelper
{
#pragma warning disable S2092 // Intentional: Secure is conditional on the caller's environment/HTTPS state
    public static CookieOptions MakeCookieOptions(TimeSpan maxAge, bool isSecure) => new()
    {
        HttpOnly = true,
        Secure = isSecure,
        SameSite = SameSiteMode.Lax,
        MaxAge = maxAge,
        Path = "/",
        IsEssential = true,
    };
#pragma warning restore S2092
}
