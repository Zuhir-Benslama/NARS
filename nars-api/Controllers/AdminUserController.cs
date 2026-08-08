using Microsoft.AspNetCore.Mvc;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Services;

namespace NarsApi.Controllers;

[ApiController]
[Route("/api")]
[Tags("Admin")]
public class AdminUserController(
    ILogger<AdminUserController> logger,
    IUserAuthorizationService authorizationService,
    IUserCreationService userCreationService,
    IWebHostEnvironment webHost) : NarsControllerBase(webHost)
{
    /// <summary>Creates a lower-tier managed account (e.g. commune_user, daira_admin, field_worker).</summary>
    [HttpPost("admin/users")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateManagedUser([FromBody] CreateAdminRequest body, CancellationToken cancellationToken = default)
    {
        if (body is null)
        {
            return Problem(detail: "Request body is required.", statusCode: 400);
        }

        var result = await userCreationService.CreateUserAsync(
            CurrentUserRole, CurrentCommuneId, CurrentDairaId, CurrentWilayaId,
            body.Name, body.Email, body.Phone, body.Username, body.Password,
            body.Role, body.CommuneId, body.DairaId, body.WilayaId,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return result.IsAuthorizationFailure
                ? Forbid()
                : Problem(detail: result.Error, statusCode: result.StatusCode);
        }

        var newUser = result.User!;

        logger.LogInformation("[Admin] {CallerRole} {CallerId} created {Role} {UserId}",
            CurrentUserRole, CurrentUserId, body.Role, newUser.Id);

        return StatusCode(201, new CreateAdminResponse(
            Success: true,
            UserId: newUser.Id.ToString(),
            Message: $"{body.Role} account created successfully."
        ));
    }

    /// <summary>Lists users that the caller has authority to manage.</summary>
    [HttpGet("admin/users")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetManageableUsers(
        [FromQuery] int skip = 0, [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        (skip, take) = Pagination.Clamp(skip, take);
        var response = await authorizationService.GetManageableUsersAsync(
            CurrentUserRole, CurrentCommuneId, CurrentDairaId, CurrentWilayaId,
            skip, take,
            cancellationToken);

        return Ok(response);
    }

    /// <summary>Updates a managed user's profile, role, or geographic scope.</summary>
    [HttpPut("admin/users/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateManagedUser(
        Guid userId, [FromBody] UpdateAdminRequest body,
        CancellationToken cancellationToken = default)
    {
        if (body is null)
        {
            return Problem(detail: "Request body is required.", statusCode: 400);
        }

        var result = await authorizationService.UpdateManagedUserAsync(
            RequiredCurrentUserId, CurrentUserRole,
            CurrentCommuneId, CurrentDairaId, CurrentWilayaId,
            userId, body, cancellationToken);

        if (!result.IsSuccess)
        {
            return result.Code switch
            {
                UserUpdateErrorCode.NotFound => Problem(detail: result.Detail, statusCode: 404),
                UserUpdateErrorCode.Forbidden => Forbid(),
                UserUpdateErrorCode.PasswordRequired => Problem(detail: result.Detail, statusCode: 400),
                UserUpdateErrorCode.InvalidPassword => Problem(detail: result.Detail, statusCode: 403),
                UserUpdateErrorCode.EmailConflict => Problem(detail: result.Detail, statusCode: 409),
                _ => Problem(detail: result.Detail, statusCode: 400),
            };
        }

        logger.LogInformation("[Admin] {CallerRole} {CallerId} updated user {UserId}",
            CurrentUserRole, CurrentUserId, userId);

        return Ok(ApiResponse.Ok());
    }

    /// <summary>Deletes a managed user account.</summary>
    [HttpDelete("admin/users/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteManagedUser(
        Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == RequiredCurrentUserId)
        {
            return Problem(detail: "Cannot delete your own account.", statusCode: 400);
        }

        var target = await authorizationService.FindUserByIdAsync(userId, cancellationToken);
        if (target is null)
        {
            return Problem(detail: "User not found.", statusCode: 404);
        }

        if (!authorizationService.CanCreateRole(CurrentUserRole, target.Role))
        {
            return Forbid();
        }

        var scopeResult = await authorizationService.ValidateManagedUserScopeAsync(
            CurrentUserRole, CurrentCommuneId, CurrentDairaId, CurrentWilayaId,
            target.Role, target.CommuneId, target.DairaId, target.WilayaId,
            cancellationToken);
        if (scopeResult.Error is not null)
        {
            return scopeResult.IsAuthorizationFailure
                ? Forbid()
                : Problem(detail: scopeResult.Error, statusCode: 400);
        }

        await authorizationService.DeleteUserAsync(userId, cancellationToken);

        logger.LogInformation("[Admin] {CallerRole} {CallerId} deleted user {UserId} ({Username})",
            CurrentUserRole, CurrentUserId, userId, target.Username);

        return NoContent();
    }
}
