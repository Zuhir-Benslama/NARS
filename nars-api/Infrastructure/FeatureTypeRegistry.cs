using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NarsApi.Data;
using NarsApi.Models;

namespace NarsApi.Infrastructure;

/// <summary>
/// Maps a feature type string (e.g. "area") to its concrete entity type
/// and creates new instances with common fields pre-populated.
/// 
/// Centralizes the type→entity mapping that was previously duplicated
/// across 5 switch statements in FeaturesController (Save, Load, Delete,
/// Update, Stats). Adding a new feature type now requires changes here only.
/// </summary>
public sealed class FeatureTypeDescriptor
{
    public string Type { get; init; } = string.Empty;
    public required Type EntityType { get; init; }

    /// <summary>
    /// PostgreSQL table name for this feature type.
    /// Used to build dynamic registry-cleanup SQL without hardcoding table names.
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// Factory that returns the typed DbSet as an IQueryable&lt;FeatureBase&gt;.
    /// </summary>
    public required Func<AppDbContext, IQueryable<FeatureBase>> DbSetAccessor { get; init; }

    /// <summary>
    /// Adds the entity to the correct DbSet and returns its tracked EntityEntry.
    /// Must be provided alongside each type registration — no separate switch needed.
    /// </summary>
    public required Func<AppDbContext, FeatureBase, EntityEntry> AddToContext { get; init; }

    /// <summary>
    /// Optional post-update action for type-specific column updates.
    /// Called after the common fields (UpdatedAt, Label, Data) are updated.
    /// Parameters: (AppDbContext, featureId, userId, data, CancellationToken).
    /// data is body.Data from the update request (the nullable JsonElement).
    /// </summary>
    public Func<AppDbContext, Guid, Guid, System.Text.Json.JsonElement?, CancellationToken, Task>? PostUpdateAction { get; init; }

    /// <summary>
    /// Creates a new entity instance with common fields populated.
    /// </summary>
    public FeatureBase CreateEntity(Guid id, Guid userId, string layer, string label, string data)
    {
        var entity = (FeatureBase)Activator.CreateInstance(EntityType)!;
        entity.Id = id;
        entity.UserId = userId;
        entity.Layer = layer;
        entity.Label = label;
        entity.Data = data;
        entity.CreatedAt = DateTime.UtcNow;
        return entity;
    }

    /// <summary>
    /// Gets the DbSet for this feature type as an IQueryable&lt;FeatureBase&gt;.
    /// </summary>
    public IQueryable<FeatureBase> GetDbSet(AppDbContext db) => DbSetAccessor(db);
}

/// <summary>
/// Registry of all feature type descriptors.
/// </summary>
public static class FeatureTypeRegistry
{
    private static readonly IReadOnlyDictionary<string, FeatureTypeDescriptor> _registry =
        new Dictionary<string, FeatureTypeDescriptor>
        {
            [FeatureTypes.Area] = new() { Type = FeatureTypes.Area, TableName = "areas", EntityType = typeof(Area), DbSetAccessor = db => db.Areas, AddToContext = (db, e) => db.Entry(db.Areas.Add((Area)e).Entity) },
            [FeatureTypes.District] = new() { Type = FeatureTypes.District, TableName = "districts", EntityType = typeof(District), DbSetAccessor = db => db.Districts, AddToContext = (db, e) => db.Entry(db.Districts.Add((District)e).Entity) },
            [FeatureTypes.CityCenter] = new() { Type = FeatureTypes.CityCenter, TableName = "city_centers", EntityType = typeof(CityCenter), DbSetAccessor = db => db.CityCenters, AddToContext = (db, e) => db.Entry(db.CityCenters.Add((CityCenter)e).Entity) },
            [FeatureTypes.Road] = new() { Type = FeatureTypes.Road, TableName = "roads", EntityType = typeof(Road), DbSetAccessor = db => db.Roads, AddToContext = (db, e) => db.Entry(db.Roads.Add((Road)e).Entity) },
            [FeatureTypes.HouseEntrance] = new() { Type = FeatureTypes.HouseEntrance, TableName = "house_entrances", EntityType = typeof(HouseEntrance), DbSetAccessor = db => db.HouseEntrances, AddToContext = (db, e) => db.Entry(db.HouseEntrances.Add((HouseEntrance)e).Entity), PostUpdateAction = UpdateHouseEntranceRoadId },
            [FeatureTypes.PublicBuilding] = new() { Type = FeatureTypes.PublicBuilding, TableName = "public_buildings", EntityType = typeof(PublicBuilding), DbSetAccessor = db => db.PublicBuildings, AddToContext = (db, e) => db.Entry(db.PublicBuildings.Add((PublicBuilding)e).Entity) },
            [FeatureTypes.PublicSpace] = new() { Type = FeatureTypes.PublicSpace, TableName = "public_spaces", EntityType = typeof(PublicSpace), DbSetAccessor = db => db.PublicSpaces, AddToContext = (db, e) => db.Entry(db.PublicSpaces.Add((PublicSpace)e).Entity) },
            [FeatureTypes.NamingPanel] = new() { Type = FeatureTypes.NamingPanel, TableName = "naming_panels", EntityType = typeof(NamingPanel), DbSetAccessor = db => db.NamingPanels, AddToContext = (db, e) => db.Entry(db.NamingPanels.Add((NamingPanel)e).Entity) },
        };

    /// <summary>
    /// Returns all registered feature types.
    /// </summary>
    public static IReadOnlyList<string> GetAllTypes() => _registry.Keys.ToList();

    /// <summary>
    /// Returns all registered feature type descriptors.
    /// Used to build dynamic UNION ALL SQL queries that stay in sync with the registry.
    /// </summary>
    public static IReadOnlyList<FeatureTypeDescriptor> GetAllDescriptors() =>
        _registry.Values.ToList();

    /// <summary>
    /// Returns the table names for all registered feature types.
    /// Used to build dynamic SQL that must stay in sync with the registry.
    /// </summary>
    public static IReadOnlyList<string> GetAllTableNames() =>
        _registry.Values.Select(d => d.TableName).ToList();

    /// <summary>
    /// Looks up a descriptor by type. Returns null if unknown.
    /// </summary>
    public static FeatureTypeDescriptor? GetDescriptor(string type) =>
        _registry.GetValueOrDefault(type);

    /// <summary>
    /// Creates a new entity for the given type with common fields populated.
    /// Returns null if the type is unknown.
    /// </summary>
    public static FeatureBase? CreateEntity(string type, Guid id, Guid userId, string layer, string label, string data)
    {
        var descriptor = GetDescriptor(type);
        return descriptor?.CreateEntity(id, userId, layer, label, data);
    }

    /// <summary>
    /// Returns the feature set as IQueryable&lt;FeatureBase&gt; for the given type.
    /// Returns null if the type is unknown.
    /// </summary>
    public static IQueryable<FeatureBase>? GetDbSet(AppDbContext db, string type) =>
        GetDescriptor(type)?.GetDbSet(db);

    /// <summary>
    /// Adds an entity to the correct DbSet via its type's descriptor.
    /// Returns the tracked entity entry on success, null if the type is unknown.
    /// </summary>
    public static EntityEntry? AddToDbContext(AppDbContext db, FeatureBase entity)
    {
        var descriptor = GetDescriptor(entity);
        return descriptor?.AddToContext(db, entity);
    }

    private static FeatureTypeDescriptor? GetDescriptor(FeatureBase entity) =>
        _registry.Values.FirstOrDefault(d => d.EntityType == entity.GetType());

    private static async Task UpdateHouseEntranceRoadId(AppDbContext db, Guid featureId, Guid userId, JsonElement? data, CancellationToken ct)
    {
        if (data is not { ValueKind: JsonValueKind.Object } obj)
            return;

        if (obj.TryGetProperty("roadDbId", out var ridEl) && ridEl.ValueKind == JsonValueKind.String && Guid.TryParse(ridEl.GetString(), out var rid))
        {
            await db.HouseEntrances
                .Where(f => f.Id == featureId && f.UserId == userId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(f => f.RoadId, rid)
                , ct);
        }
    }
}
