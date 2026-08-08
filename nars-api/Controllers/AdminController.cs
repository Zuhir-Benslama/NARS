using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NarsApi.Infrastructure;
using NarsApi.Services;

namespace NarsApi.Controllers;

[ApiController]
[Route("/api")]
[Tags("Admin")]
public class AdminController(
    IAdminOverviewService overviewService,
    IWebHostEnvironment webHost) : NarsControllerBase(webHost)
{
    /// <summary>Returns a role-scoped administrative overview of the hierarchy.</summary>
    /// <param name="skip">Number of wilayas to skip (national overview only, default 0).</param>
    /// <param name="take">Maximum wilayas to return (national overview only, clamped 1-500, default 500).</param>
    [HttpGet("admin/overview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Overview(
        [FromQuery] int skip = 0, [FromQuery] int take = 500,
        CancellationToken cancellationToken = default)
    {
        var role = CurrentUserRole;
        var dairaId = CurrentDairaId;
        return role switch
        {
            UserRoles.DairaAdmin when dairaId is null =>
                Problem(detail: "daira_id missing on account. Contact your administrator.", statusCode: 403),
            UserRoles.WilayaAdmin when CurrentWilayaId is null =>
                Problem(detail: "wilaya_id missing on account. Contact your administrator.", statusCode: 403),
            UserRoles.DairaAdmin => await DairaOverview(dairaId!.Value, cancellationToken),
            UserRoles.WilayaAdmin => await WilayaOverview(CurrentWilayaId!.Value, cancellationToken),
            UserRoles.NationalAdmin => await NationalOverview(skip, take, cancellationToken),
            _ => Forbid(),
        };
    }

    /// <summary>Returns a detailed report for a specific wilaya (national admin only).</summary>
    [HttpGet("admin/wilaya/{wilayaId:int}")]
    [Authorize(Roles = UserRoles.NationalAdmin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWilaya(int wilayaId, CancellationToken cancellationToken = default)
    {
        var result = await overviewService.GetWilayaReportAsync(wilayaId, cancellationToken);
        return result is null ? Problem(detail: "Wilaya not found.", statusCode: 404) : Ok(result);
    }

    /// <summary>Returns a detailed report for a specific daira (wilaya/national admin).</summary>
    [HttpGet("admin/daira/{dairaId:int}")]
    [Authorize(Roles = UserRoles.WilayaOrNationalAdmin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDaira(int dairaId, CancellationToken cancellationToken = default)
    {
        // Enforce the caller's wilaya scope inside the report query for wilaya
        // admins to avoid a separate round-trip for the daira entity.
        int? expectedWilayaId = CurrentUserRole == UserRoles.WilayaAdmin ? CurrentWilayaId : null;

        var result = await overviewService.GetDairaReportAsync(dairaId, expectedWilayaId, cancellationToken);
        return result is null ? Problem(detail: "Daira not found.", statusCode: 404) : Ok(result);
    }

    private async Task<IActionResult> NationalOverview(int skip, int take, CancellationToken cancellationToken)
    {
        (skip, take) = Pagination.Clamp(skip, take);
        var (wilayas, total) = await overviewService.GetNationalOverviewAsync(skip, take, cancellationToken);
        return Ok(new { level = "national", wilayas, total, skip, take });
    }

    private async Task<IActionResult> WilayaOverview(int wilayaId, CancellationToken cancellationToken)
    {
        var report = await overviewService.GetWilayaReportAsync(wilayaId, cancellationToken);
        return report is null ? Problem(detail: "Wilaya not found.", statusCode: 404) : Ok(report);
    }

    private async Task<IActionResult> DairaOverview(int dairaId, CancellationToken cancellationToken)
    {
        var report = await overviewService.GetDairaReportAsync(dairaId, cancellationToken: cancellationToken);
        return report is null ? Problem(detail: "Daira not found.", statusCode: 404) : Ok(report);
    }
}
