using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Controllers;

[ApiController]
[Route("/api")]
[Tags("Admin")]
public class AdminController(
    AppDbContext db,
    IAdminOverviewService overviewService,
    IWebHostEnvironment webHost) : NarsControllerBase(webHost)
{
    /// <summary>Returns a role-scoped administrative overview of the hierarchy.</summary>
    [HttpGet("admin/overview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Overview(CancellationToken cancellationToken = default)
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
            UserRoles.NationalAdmin => await NationalOverview(cancellationToken),
            _ => Forbid(),
        };
    }

    /// <summary>Returns a detailed report for a specific wilaya (national admin only).</summary>
    [HttpGet("admin/wilaya/{wilayaId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWilaya(int wilayaId, CancellationToken cancellationToken = default)
    {
        if (CurrentUserRole != UserRoles.NationalAdmin)
        {
            return Forbid();
        }

        var result = await overviewService.GetWilayaReportAsync(wilayaId, cancellationToken);
        return result is null ? Problem(detail: "Wilaya not found.", statusCode: 404) : Ok(result);
    }

    /// <summary>Returns a detailed report for a specific daira (wilaya/national admin).</summary>
    [HttpGet("admin/daira/{dairaId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDaira(int dairaId, CancellationToken cancellationToken = default)
    {
        switch (CurrentUserRole)
        {
            case UserRoles.WilayaAdmin:
                {
                    var daira = await db.Dairas.FindAsync([dairaId], cancellationToken);
                    if (daira is null || daira.WilayaId != CurrentWilayaId)
                    {
                        return Forbid();
                    }

                    break;
                }
            case UserRoles.NationalAdmin:
                break;
            default:
                return Forbid();
        }

        var result = await overviewService.GetDairaReportAsync(dairaId, cancellationToken);
        return result is null ? Problem(detail: "Daira not found.", statusCode: 404) : Ok(result);
    }

    private async Task<IActionResult> NationalOverview(CancellationToken cancellationToken)
    {
        var wilayas = await overviewService.GetNationalOverviewAsync(cancellationToken);
        return Ok(new { level = "national", wilayas });
    }

    private async Task<IActionResult> WilayaOverview(int wilayaId, CancellationToken cancellationToken)
    {
        var report = await overviewService.GetWilayaReportAsync(wilayaId, cancellationToken);
        return report is null ? Problem(detail: "Wilaya not found.", statusCode: 404) : Ok(report);
    }

    private async Task<IActionResult> DairaOverview(int dairaId, CancellationToken cancellationToken)
    {
        var report = await overviewService.GetDairaReportAsync(dairaId, cancellationToken);
        return report is null ? Problem(detail: "Daira not found.", statusCode: 404) : Ok(report);
    }
}
