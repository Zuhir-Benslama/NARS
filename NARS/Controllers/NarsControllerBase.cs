using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NarsApi.Controllers;

/// <summary>
/// Base class for all controllers that require an authenticated user.
/// Applying [Authorize] here routes unauthenticated requests through the
/// JWT bearer pipeline (OnMessageReceived → 401) instead of duplicating
/// manual cookie→token→principal validation in every controller.
/// Fixes security issues #2 and #9.
/// </summary>
[Authorize]
public abstract class NarsControllerBase : ControllerBase
{
    /// <summary>
    /// The authenticated user's database ID.
    /// Throws <see cref="InvalidOperationException"/> if the <c>user_id</c> claim is absent
    /// or non-numeric — this indicates a server-side token misconfiguration and should surface
    /// loudly (caught by the global exception handler → HTTP 500) rather than silently
    /// executing queries with a phantom ID of -1.
    /// </summary>
    protected int CurrentUserId =>
        int.TryParse(User.FindFirstValue("user_id"), out int id) && id > 0
            ? id
            : throw new InvalidOperationException("user_id claim missing or invalid in authenticated token.");

    /// <summary>The authenticated user's username.</summary>
    protected string CurrentUsername =>
        User.FindFirstValue("username") ?? string.Empty;

    /// <summary>
    /// The commune ID the authenticated user is registered to.
    /// Throws <see cref="InvalidOperationException"/> if the claim is absent or non-numeric.
    /// </summary>
    protected int CurrentCommuneId =>
        int.TryParse(User.FindFirstValue("commune_id"), out int id) && id > 0
            ? id
            : throw new InvalidOperationException("commune_id claim missing or invalid in authenticated token.");

    /// <summary>Adds a named parameter to an ADO.NET command.</summary>
    protected static void AddParam(IDbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value         = value;
        cmd.Parameters.Add(p);
    }
}
