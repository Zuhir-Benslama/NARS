using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Models;

namespace NarsApi.Infrastructure;

public sealed record IndexDefinition(string PropertyName, string IndexName, string? Filter = null);

public sealed record CompositeIndexDefinition(string[] PropertyNames, string IndexName, string? Filter = null);

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
    /// Factory that creates new entity instances.
    /// </summary>
    public required Func<FeatureBase> CreateInstance { get; init; }

    /// <summary>
    /// Optional post-update action for type-specific column updates.
    /// Called after the common fields (UpdatedAt, Label, Data) are updated.
    /// Parameters: (AppDbContext, featureId, userId, data, CancellationToken).
    /// data is body.Data from the update request (the nullable JsonElement).
    /// </summary>
    public Func<AppDbContext, Guid, Guid, System.Text.Json.JsonElement?, CancellationToken, Task>? PostUpdateAction { get; init; }

    /// <summary>
    /// Index definitions applied during OnModelCreating.
    /// </summary>
    public IReadOnlyList<IndexDefinition> Indexes { get; init; } = [];

    /// <summary>
    /// Composite index definitions applied during OnModelCreating.
    /// </summary>
    public IReadOnlyList<CompositeIndexDefinition> CompositeIndexes { get; init; } = [];

    /// <summary>
    /// Creates a new entity instance with common fields populated.
    /// </summary>
    public FeatureBase CreateEntity(Guid id, Guid userId, string layer, string label, string data, DateTime createdAt)
    {
        var entity = CreateInstance();
        entity.Id = id;
        entity.UserId = userId;
        entity.Layer = layer;
        entity.Label = label;
        entity.Data = data;
        entity.CreatedAt = createdAt;
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
    private static FeatureTypeDescriptor Descriptor<T>(string type, string tableName, Func<AppDbContext, Microsoft.EntityFrameworkCore.DbSet<T>> dbSet, Func<AppDbContext, Guid, Guid, System.Text.Json.JsonElement?, CancellationToken, Task>? postUpdateAction = null, IReadOnlyList<IndexDefinition>? indexes = null, IReadOnlyList<CompositeIndexDefinition>? compositeIndexes = null) where T : FeatureBase, new() =>
        new()
        {
            Type = type,
            TableName = tableName,
            EntityType = typeof(T),
            DbSetAccessor = db => dbSet(db),
            AddToContext = (db, e) => db.Entry(dbSet(db).Add((T)e).Entity),
            CreateInstance = static () => new T(),
            PostUpdateAction = postUpdateAction,
            Indexes = indexes ?? [],
            CompositeIndexes = compositeIndexes ?? [],
        };

    private static readonly FrozenDictionary<string, FeatureTypeDescriptor> _registry =
        new Dictionary<string, FeatureTypeDescriptor>
        {
            [FeatureTypes.Area] = Descriptor<Area>(FeatureTypes.Area, "areas", db => db.Areas,
                indexes: [new("UserId", "ix_areas_user_id")],
                compositeIndexes: [new(["UserId", "Layer"], "ix_areas_user_layer")]),
            [FeatureTypes.District] = Descriptor<District>(FeatureTypes.District, "districts", db => db.Districts,
                indexes: [new("UserId", "ix_districts_user_id")]),
            [FeatureTypes.CityCenter] = Descriptor<CityCenter>(FeatureTypes.CityCenter, "city_centers", db => db.CityCenters,
                indexes: [new("UserId", "ix_city_centers_user_id")]),
            [FeatureTypes.Road] = Descriptor<Road>(FeatureTypes.Road, "roads", db => db.Roads,
                indexes: [new("UserId", "ix_roads_user_id")],
                compositeIndexes: [new(["UserId", "Layer"], "ix_roads_user_layer")]),
            [FeatureTypes.HouseEntrance] = Descriptor<HouseEntrance>(FeatureTypes.HouseEntrance, "house_entrances", db => db.HouseEntrances,
                postUpdateAction: UpdateHouseEntranceRoadId,
                indexes: [new("UserId", "ix_house_entrances_user_id")],
                compositeIndexes:
                [
                    new(["UserId", "Layer"], "ix_house_entrances_user_layer"),
                    new(["RoadId"], "ix_house_entrances_road_id", "road_id IS NOT NULL"),
                ]),
            [FeatureTypes.PublicBuilding] = Descriptor<PublicBuilding>(FeatureTypes.PublicBuilding, "public_buildings", db => db.PublicBuildings,
                indexes: [new("UserId", "ix_public_buildings_user_id")]),
            [FeatureTypes.PublicSpace] = Descriptor<PublicSpace>(FeatureTypes.PublicSpace, "public_spaces", db => db.PublicSpaces,
                indexes: [new("UserId", "ix_public_spaces_user_id")]),
            [FeatureTypes.NamingPanel] = Descriptor<NamingPanel>(FeatureTypes.NamingPanel, "naming_panels", db => db.NamingPanels,
                indexes: [new("UserId", "ix_naming_panels_user_id")]),
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<Type, FeatureTypeDescriptor> _entityTypeMap =
        _registry.Values.ToDictionary(d => d.EntityType).ToFrozenDictionary();

    private static readonly IReadOnlyList<string> _allTypes = [.. _registry.Keys];

    /// <summary>
    /// Returns all registered feature types.
    /// </summary>
    public static IReadOnlyList<string> GetAllTypes() => _allTypes;

    // ── Catalog (UI metadata for each feature type) ──────────────────────
    // Kept beside the registry so adding a feature type updates one file.
    // The catalog order matches the original controller list order.

    private const string IconArea = "\u2B1F";
    private const string IconRoad = "\U0001F6E3\uFE0F";
    private const string IconDistrict = "\U0001F3D8\uFE0F";
    private const string IconHouseEntrance = "\U0001F6AA";
    private const string IconPublicBuilding = "\U0001F3DB\uFE0F";
    private const string IconPublicSpace = "\U0001F333";
    private const string IconCityCenter = "\U0001F3D9\uFE0F";
    private const string IconNamingPanel = "\U0001FAB5";

    /// <summary>Returns the full catalog of feature types with their available layers.</summary>
    public static IReadOnlyList<FeatureTypeDefinition> GetCatalog() => _catalog;

    private static readonly List<FeatureTypeDefinition> _catalog =
    [
        new(Key: FeatureTypes.Area, Label: "Area", Icon: IconArea,
            Layers:
            [
                new LayerOption(FeatureTypes.AreaLayers.CentralUrban,   "Central Urban Area"),
                new LayerOption(FeatureTypes.AreaLayers.SecondaryUrban, "Secondary Urban Area"),
                new LayerOption(FeatureTypes.AreaLayers.Scattered,      "Scattered Area"),
            ]),
        new(Key: FeatureTypes.Road, Label: "Road", Icon: IconRoad,
            Layers:
            [
                new LayerOption(FeatureTypes.RoadLayers.Boulevard, "Boulevard", "primary"),
                new LayerOption(FeatureTypes.RoadLayers.Avenue,    "Avenue",    "primary"),
                new LayerOption(FeatureTypes.RoadLayers.Street,    "Street",    "secondary"),
                new LayerOption(FeatureTypes.RoadLayers.Drive,     "Drive",     "tertiary"),
                new LayerOption(FeatureTypes.RoadLayers.Lane,      "Lane",      "tertiary"),
                new LayerOption(FeatureTypes.RoadLayers.CulDeSac,  "Cul-de-sac","tertiary"),
                new LayerOption(FeatureTypes.RoadLayers.Way,       "Way",       "tertiary"),
            ]),
        new(Key: FeatureTypes.District, Label: "District", Icon: IconDistrict,
            Layers:
            [
                new LayerOption(FeatureTypes.DistrictLayers.HousingEstate,      "Housing Estate"),
                new LayerOption(FeatureTypes.DistrictLayers.UrbanPole,          "Urban Pole"),
                new LayerOption(FeatureTypes.DistrictLayers.DistrictLayer,      "District"),
                new LayerOption(FeatureTypes.DistrictLayers.TradActivitiesZone, "Trad. Activities Zone"),
                new LayerOption(FeatureTypes.DistrictLayers.IndustryZone,       "Industry Zone"),
            ]),
        new(Key: FeatureTypes.HouseEntrance, Label: "House Entrance", Icon: IconHouseEntrance,
            Layers:
            [
                new LayerOption(FeatureTypes.HouseEntranceLayers.Main,      "Main Entrance"),
                new LayerOption(FeatureTypes.HouseEntranceLayers.Secondary, "Secondary Entrance"),
            ]),
        new(Key: FeatureTypes.PublicBuilding, Label: "Public Building", Icon: IconPublicBuilding,
            Layers:
            [
                new LayerOption(FeatureTypes.PublicBuildingLayers.Default,                        "Public Building"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.Bank,                          "Bank"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.PostOffice,                    "Post Office"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.ConventionCentre,              "Convention Centre"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.PublicMarket,                  "Public Market"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.TradeCentre,                  "Trade Centre"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.Library,                       "Library"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.Museum,                        "Museum"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.Theater,                       "Theater"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.BordersGuard,                  "Borders Guard"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.Customs,                       "Customs"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.FireStation,                   "Fire Station"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.Gendarmes,                    "Gendarmes"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.MilitaryBarrack,               "Military Barrack"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.PoliceStation,                 "Police Station"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.AdministrativeBranch,          "Administrative Branch"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.PublicHospital,               "Public Hospital"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.NeighborhoodHealth,            "Neighborhood Health"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.SpecializedHospital,           "Specialized Hospital"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.TreatmentRoom,                "Treatment Room"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.UniversityHospital,            "University Hospital"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.ResearchInstitute,             "Research Institute"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.University,                   "University"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.College,                       "College"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.School,                        "School"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.Cemetery,                      "Cemetery"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.Mosque,                        "Mosque"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.Hostel,                        "Hostel"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.Hotel,                         "Hotel"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.Motel,                         "Motel"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.Airport,                       "Airport"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.BusStation,                   "Bus Station"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.TrainStation,                  "Train Station"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.SpecializedVocationalInstitute, "Specialized Vocational Institute"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.VocationalEducationInstitute,   "Vocational Education Institute"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.VocationalApprenticeshipCenter, "Vocational Apprenticeship Center"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.VocationalTrainingInstitute,    "Vocational Training Institute"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.IndoorArena,                   "Indoor Arena"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.LeisureCenter,                 "Leisure Center"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.SportsComplex,                 "Sports Complex"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.Stadium,                       "Stadium"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.SwimmingPool,                 "Swimming Pool"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.YouthClubs,                    "Youth Clubs"),
                new LayerOption(FeatureTypes.PublicBuildingLayers.YouthHostel,                  "Youth Hostel"),
            ]),
        new(Key: FeatureTypes.PublicSpace, Label: "Public Space", Icon: IconPublicSpace,
            Layers:
            [
                new LayerOption(FeatureTypes.PublicSpaceLayers.Garden, "Garden"),
                new LayerOption(FeatureTypes.PublicSpaceLayers.Square, "Square"),
            ]),
        new(Key: FeatureTypes.CityCenter, Label: "City Center", Icon: IconCityCenter,
            Layers: [new LayerOption(FeatureTypes.CityCenterLayers.Default, "City Center")]),
        new(Key: FeatureTypes.NamingPanel, Label: "Naming Panel", Icon: IconNamingPanel,
            Layers: [new LayerOption(FeatureTypes.NamingPanelLayers.Default, "Naming Panel")]),
    ];

    private static readonly IReadOnlyList<FeatureTypeDescriptor> _allDescriptors = [.. _registry.Values];

    /// <summary>
    /// Returns all registered feature type descriptors.
    /// Used to build dynamic UNION ALL SQL queries that stay in sync with the registry.
    /// </summary>
    public static IReadOnlyList<FeatureTypeDescriptor> GetAllDescriptors() => _allDescriptors;

    private static readonly IReadOnlyList<string> _allTableNames = [.. _registry.Values.Select(d => d.TableName)];

    /// <summary>
    /// Returns the table names for all registered feature types.
    /// Used to build dynamic SQL that must stay in sync with the registry.
    /// </summary>
    public static IReadOnlyList<string> GetAllTableNames() => _allTableNames;

    /// <summary>
    /// Validates a table name against the known allowlist to prevent SQL injection
    /// from any future dynamic table name sources.
    /// </summary>
    public static bool IsValidTableName(string tableName) => _allTableNames.Contains(tableName);

    /// <summary>
    /// Validates a table name and throws if not in the allowlist.
    /// </summary>
    public static string ValidateTableName(string tableName) =>
        IsValidTableName(tableName)
            ? tableName
            : throw new InvalidOperationException($"Unknown table name '{tableName}'. Table must be a registered feature type.");

    /// <summary>
    /// Looks up a descriptor by type. Returns null if unknown.
    /// </summary>
    public static FeatureTypeDescriptor? GetDescriptor(string type) =>
        _registry.GetValueOrDefault(type);

    /// <summary>
    /// Tries to look up a descriptor by type. Returns false if unknown.
    /// </summary>
    public static bool TryGetDescriptor(string type, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out FeatureTypeDescriptor? descriptor)
    {
        descriptor = GetDescriptor(type);
        return descriptor is not null;
    }

    /// <summary>
    /// Creates a new entity for the given type with common fields populated.
    /// Returns null if the type is unknown.
    /// </summary>
    public static FeatureBase? CreateEntity(string type, Guid id, Guid userId, string layer, string label, string data, DateTime createdAt)
    {
        var descriptor = GetDescriptor(type);
        return descriptor?.CreateEntity(id, userId, layer, label, data, createdAt);
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
        _entityTypeMap.GetValueOrDefault(entity.GetType());

    /// <summary>Parses the "roadDbId" property (a UUID string) out of a feature's JSON data.</summary>
    public static bool TryGetRoadDbId(JsonElement data, out Guid roadDbId)
    {
        if (data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("roadDbId", out var ridEl)
            && ridEl.ValueKind == JsonValueKind.String
            && Guid.TryParse(ridEl.GetString(), out var rid))
        {
            roadDbId = rid;
            return true;
        }

        roadDbId = Guid.Empty;
        return false;
    }

    private static async Task UpdateHouseEntranceRoadId(AppDbContext db, Guid featureId, Guid userId, JsonElement? data, CancellationToken ct)
    {
        if (data is not { ValueKind: JsonValueKind.Object } obj)
        {
            return;
        }

        if (TryGetRoadDbId(obj, out var rid))
        {
            await db.HouseEntrances
                .Where(f => f.Id == featureId && f.UserId == userId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(f => f.RoadId, rid)
                , ct);
        }
    }
}
