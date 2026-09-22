using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

/// <summary>A single materialized road created from an AI draft.</summary>
public sealed record GeneratedRoad(Guid DbId, string Layer, string Label, JsonObject Data);

/// <summary>
/// Number of seeds dropped by each cadastre rule (and unparseable geometry),
/// so tuning the next pass is evidence-based instead of guesswork. Sums to at
/// most <see cref="RoadGenerationSummary.Dropped"/> — the first failing piece
/// of each dropped seed is counted once, matching how the drop itself is
/// attributed.
/// </summary>
public sealed record RoadDropBreakdown(
    int TooShort,
    int LowConfidence,
    int ExcessiveTurnAngle,
    int OutsideUrbanArea,
    int InvalidGeometry);

internal sealed class RoadDropTally
{
    public int TooShort;
    public int LowConfidence;
    public int ExcessiveTurnAngle;
    public int OutsideUrbanArea;
    public int InvalidGeometry;

    public void Count(RoadPhaseViolation violation) =>
        _ = violation switch
        {
            RoadPhaseViolation.TooShort => TooShort++,
            RoadPhaseViolation.LowConfidence => LowConfidence++,
            RoadPhaseViolation.ExcessiveTurnAngle => ExcessiveTurnAngle++,
            RoadPhaseViolation.OutsideUrbanArea => OutsideUrbanArea++,
            _ => 0,
        };

    public RoadDropBreakdown ToBreakdown() =>
        new(TooShort, LowConfidence, ExcessiveTurnAngle, OutsideUrbanArea, InvalidGeometry);
}

/// <summary>
/// Results of a generation pass. <see cref="Created"/> holds the roads written
/// to the production tables after the post-accept weld + merge passes (a seed
/// split at crossings may yield several pieces that are then fused back into
/// one road); <see cref="Dropped"/> counts the seeds that produced no road
/// (rejected by the cadastre rules and left pending), so the UI can report an
/// honest created/rejected breakdown. <see cref="Welded"/> counts the dangling
/// endpoints a road's creation pass re-connected onto the network; <see cref="Merged"/>
/// counts the roads fused back together after being split at shared junctions.
/// </summary>
public sealed record RoadGenerationSummary(
    IReadOnlyList<GeneratedRoad> Created,
    int Dropped,
    RoadDropBreakdown Breakdown,
    int Welded = 0,
    int Merged = 0);

public interface IRoadGenerationService
{
    /// <summary>
    /// Materializes pending AI road drafts for a commune into production road
    /// features, enforcing the roads-phase cadastre rules (urban containment,
    /// turn angle, minimum length/confidence once a network exists, and endpoint
    /// snapping onto the growing road network) entirely in pure C#. Rejected
    /// seeds are counted and left pending; roads that reach no network simply
    /// seed it. The caller must have access to the commune.
    /// </summary>
    Task<RoadGenerationSummary> GenerateAsync(
        string callerRole,
        int? callerCommuneId,
        int? callerDairaId,
        int? callerWilayaId,
        Guid userId,
        int communeId,
        IReadOnlyList<Guid> draftIds,
        CancellationToken ct = default);
}

/// <summary>
/// AI road draft → production road pipeline. Consumes the draft ids returned by
/// the segmentation endpoint and keeps only the drafts that obey the roads
/// phase rules, snapping endpoints onto the commune's road network. Roads
/// are owned by the calling user (the same convention DraftFeaturesService
/// uses when a road draft is accepted) and start on the default "street" layer.
/// </summary>
public class RoadGenerationService(
    IDbContextFactory<AppDbContext> dbFactory,
    ICommuneScopeService communeScope,
    IDateTimeProvider timeProvider,
    IOptions<ValidationOptions> validationOptions,
    IOptions<RoadRulesOptions> roadRulesOptions) : IRoadGenerationService
{
    public async Task<RoadGenerationSummary> GenerateAsync(
        string callerRole,
        int? callerCommuneId,
        int? callerDairaId,
        int? callerWilayaId,
        Guid userId,
        int communeId,
        IReadOnlyList<Guid> draftIds,
        CancellationToken ct = default)
    {
        if (!await communeScope.CanAccessCommuneAsync(
                callerRole, callerCommuneId, callerDairaId, callerWilayaId, communeId, ct))
        {
            throw new UnauthorizedAccessException("Caller has no access to the commune.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Drafts are commune-scoped, road-typed and pending; requested ids that
        // don't match (already reviewed, another commune, wrong type) are simply
        // absent rather than counted as dropped.
        var drafts = await db.AiDraftFeatures
            .Where(f => f.CommuneId == communeId
                && f.FeatureType == AiDraftFeature.TypeRoad
                && f.Status == AiDraftFeature.StatusPending
                && draftIds.Contains(f.Id))
            .ToListAsync(ct);
        if (drafts.Count == 0)
        {
            return new RoadGenerationSummary([], 0, new RoadDropBreakdown(0, 0, 0, 0, 0));
        }

        var areaRings = await RoadPhaseRules.LoadUrbanAreaRingsAsync(db, communeId, ct);
        var roadNetwork = await RoadPhaseRules.LoadRoadNetworkAsync(db, communeId, ct);

        var validation = validationOptions.Value;
        var rules = roadRulesOptions.Value;

        var now = timeProvider.UtcNow;
        var acceptedIds = new List<Guid>(drafts.Count);
        var dropped = 0;
        var tally = new RoadDropTally();

        // The connected network grows during the pass: existing mapped roads,
        // then each accepted piece. Later drafts split at and snap onto it, so
        // generated roads never pierce each other and become one connected graph
        // instead of isolated fragments. The candidate values are the SAME
        // mutable List objects referenced here, so the post-accept weld pass can
        // mutate them and the network snapshot stays coherent.
        var acceptedPolylines = new List<IReadOnlyList<(double Lat, double Lng)>>(roadNetwork);
        var candidates = new List<List<(double Lat, double Lng)>>();
        var candidateDrafts = new List<AiDraftFeature>();

        // Higher-confidence seeds go first: they win the crossings and become
        // the network the weaker drafts must connect to.
        foreach (var draft in drafts.OrderByDescending(d => d.Confidence).ThenBy(d => d.Id))
        {
            if (!DraftGeometry.TryGetLineCoordinates(draft.GeometryGeoJson, out var seedVertices))
            {
                dropped++;
                tally.InvalidGeometry++;
                continue;
            }

            // Split the seed at every point where it crosses the accepted
            // network, so each crossing becomes a topology node shared by the
            // resulting pieces. Endpoint touches/T-junctions are not cut — the
            // same snapping step in Evaluate merges them onto the network.
            var pieces = RoadGenerationGeometry.SplitLineAtCrossings(seedVertices, acceptedPolylines);
            var createdForSeed = 0;
            foreach (var piece in pieces)
            {
                // The roads-phase rules (length + isolation, confidence, turn
                // angle, urban containment, connectivity + endpoint snapping)
                // live in RoadPhaseRules — the same engine as the single-accept
                // path and the segmentation draft pre-filter, so every piece
                // obeys the identical rule set.
                var outcome = RoadPhaseRules.Evaluate(
                    piece, draft.Confidence, areaRings, acceptedPolylines, validation, rules,
                    snapEndpoints: true);
                if (outcome.Violation != RoadPhaseViolation.None || outcome.Coordinates.Count < 2)
                {
                    if (outcome.Violation != RoadPhaseViolation.None)
                    {
                        tally.Count(outcome.Violation);
                    }
                    else
                    {
                        tally.InvalidGeometry++;
                    }

                    continue;
                }

                var coords = new List<(double Lat, double Lng)>(outcome.Coordinates);
                candidates.Add(coords);
                candidateDrafts.Add(draft);
                acceptedPolylines.Add(coords);
                createdForSeed++;
            }

            if (createdForSeed == 0)
            {
                dropped++;
                continue;
            }

            acceptedIds.Add(draft.Id);
        }

        if (candidates.Count == 0)
        {
            return new RoadGenerationSummary([], dropped, tally.ToBreakdown());
        }

        // Post-accept topology: weld dangling endpoints onto the network (both
        // endpoints of every candidate; straight extension preferred, else
        // nearest point), then fuse collinear roads that were split at shared
        // junctions back into single features. The weld mutates the candidate
        // List objects in place, which the caller's network snapshot sees too.
        var welded = RoadNetworkTopology.WeldEndpoints(
            candidates, acceptedPolylines, areaRings, validation, rules);
        var merged = RoadNetworkTopology.MergeCollinearRoads(candidates);
        var mergedCount = candidates.Count - merged.Count;

        var created = new List<GeneratedRoad>(merged.Count);
        foreach (var component in merged)
        {
            // The merged road is owned by the commune scope of its first source
            // draft (all candidates share the caller's commune by construction).
            var source = candidateDrafts[component.MemberIndices[0]];
            var roadId = Guid.CreateVersion7();
            var roadData = DraftGeometry.ToRoadData(source.GeometryGeoJson);
            roadData["coordinates"] = RoadPhaseRules.ToJsonCoordinates(component.Vertices);
            var entity = FeatureTypeRegistry.CreateEntity(
                FeatureTypes.Road, roadId, userId, FeatureTypes.RoadLayers.Street, string.Empty, roadData.ToJsonString(), now)
                ?? throw new InvalidOperationException("FeatureTypeRegistry has no Road descriptor");
            FeatureTypeRegistry.AddToDbContext(db, entity);
            db.FeatureRegistry.Add(new FeatureRegistry { Id = roadId, FeatureType = FeatureTypes.Road });

            created.Add(new GeneratedRoad(roadId, FeatureTypes.RoadLayers.Street, string.Empty, roadData));
        }

        if (created.Count == 0)
        {
            return new RoadGenerationSummary([], dropped, tally.ToBreakdown());
        }

        var affected = await TransitionDraftsToAcceptedAsync(db, acceptedIds, userId, now, ct);
        if (affected < acceptedIds.Count)
        {
            // A concurrent review transitioned some drafts between our read and
            // here; do not let their roads commit as duplicates.
            return new RoadGenerationSummary([], dropped + acceptedIds.Count - affected, tally.ToBreakdown());
        }

        await db.SaveChangesAsync(ct);
        return new RoadGenerationSummary(created, dropped, tally.ToBreakdown(), welded, mergedCount);
    }

    /// <summary>
    /// Marks the consumed drafts accepted as a single conditional UPDATE ...
    /// WHERE Status = 'pending'. Kept virtual so unit tests can substitute an
    /// equivalent tracked update for the InMemory provider, which does not
    /// implement ExecuteUpdateAsync.
    /// </summary>
    protected virtual async Task<int> TransitionDraftsToAcceptedAsync(
        AppDbContext db, IReadOnlyList<Guid> draftIds, Guid userId, DateTime reviewedAt, CancellationToken ct)
        => await db.AiDraftFeatures
            .Where(f => draftIds.Contains(f.Id) && f.Status == AiDraftFeature.StatusPending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(f => f.Status, AiDraftFeature.StatusAccepted)
                .SetProperty(f => f.ReviewedBy, userId)
                .SetProperty(f => f.ReviewedAt, reviewedAt), ct);
}
