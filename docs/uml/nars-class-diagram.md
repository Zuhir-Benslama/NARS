# NARS Backend - Class Diagram

```mermaid
classDiagram
    %% ===== ABSTRACT BASE =====
    class FeatureBase {
        <<abstract>>
        +Guid Id
        +Guid UserId
        +string Layer
        +string Label
        +string Data
        +DateTime CreatedAt
        +DateTime? UpdatedAt
        +uint Version
    }

    %% ===== CONCRETE FEATURES =====
    class Area
    class District
    class CityCenter
    class Road
    class HouseEntrance {
        +Guid? RoadId
    }
    class PublicBuilding
    class PublicSpace
    class NamingPanel

    FeatureBase <|-- Area
    FeatureBase <|-- District
    FeatureBase <|-- CityCenter
    FeatureBase <|-- Road
    FeatureBase <|-- HouseEntrance
    FeatureBase <|-- PublicBuilding
    FeatureBase <|-- PublicSpace
    FeatureBase <|-- NamingPanel

    %% ===== FEATURE REGISTRY =====
    class FeatureRegistry {
        +Guid Id
        +string FeatureType
    }

    %% ===== LOCATION MODELS =====
    class User {
        +Guid Id
        +string Name
        +string Email
        +string Phone
        +string Username
        +string PasswordHash
        +int? CommuneId
        +int? DairaId
        +int? WilayaId
        +string Role
        +DateTime CreatedAt
        +int? FailedLoginAttempts
        +DateTime? LockedUntil
    }

    class Wilaya {
        +int WilayaId
        +string? WilayaAr
        +string? WilayaFr
        +double? WilayaLatitude
        +double? WilayaLongitude
    }

    class Daira {
        +int DairaId
        +int WilayaId
        +string DairaAr
        +string DairaFr
        +double? DairaLatitude
        +double? DairaLongitude
        +string? DairaName
    }

    class Commune {
        +int CommuneId
        +int DairaId
        +int? CommuneCode
        +string CommuneAr
        +string CommuneFr
        +double? CommuneLatitude
        +double? CommuneLongitude
        +string? CommuneName
    }

    class CommuneBoundary {
        +int CommuneId
        +Geometry Geometry
    }

    class RefreshToken {
        +Guid Id
        +Guid UserId
        +string TokenHash
        +DateTime ExpiresAt
        +DateTime CreatedAt
        +bool Revoked
    }

    User "1" --> "many" RefreshToken

    Wilaya "1" --> "many" Daira
    Daira "1" --> "many" Commune
    Commune "1" --> "1" CommuneBoundary

    %% ===== DATABASE CONTEXT =====
    class AppDbContext {
        +DbSet~User~ Users
        +DbSet~Wilaya~ Wilayas
        +DbSet~Daira~ Dairas
        +DbSet~Commune~ Communes
        +DbSet~Area~ Areas
        +DbSet~Road~ Roads
        +DbSet~District~ Districts
        +DbSet~HouseEntrance~ HouseEntrances
        +DbSet~PublicBuilding~ PublicBuildings
        +DbSet~PublicSpace~ PublicSpaces
        +DbSet~NamingPanel~ NamingPanels
        +DbSet~CityCenter~ CityCenters
        +DbSet~FeatureRegistry~ FeatureRegistry
        +DbSet~RefreshToken~ RefreshTokens
        +DbSet~CommuneBoundary~ CommuneBoundaries
        +OnModelCreating(ModelBuilder)
    }

    AppDbContext o-- FeatureBase
    AppDbContext o-- User
    AppDbContext o-- Wilaya
    AppDbContext o-- Daira
    AppDbContext o-- Commune
    AppDbContext o-- RefreshToken

    %% ===== SERVICES =====
    class IScatteredAreaService {
        <<interface>>
        +Task~bool~ RefreshAsync(Guid userId, int communeId, CancellationToken)
        +GetLastError(Guid, int) (DateTimeOffset, string)?
    }

    class ScatteredAreaService {
        +Task~bool~ RefreshAsync(Guid userId, int communeId, CancellationToken)
        +GetLastError(Guid, int) (DateTimeOffset, string)?
    }

    IScatteredAreaService <|.. ScatteredAreaService

    class JwtService {
        +string GenerateToken(User user)
        +ClaimsPrincipal ValidateToken(string token)
    }

    class IBackgroundTaskQueue {
        <<interface>>
        +ValueTask~bool~ QueueBackgroundWorkItemAsync(workItem)
        +ValueTask~Func~ DequeueAsync(CancellationToken)
    }

    class BackgroundTaskQueue {
        +ValueTask~bool~ QueueBackgroundWorkItemAsync(workItem)
        +ValueTask~Func~ DequeueAsync(CancellationToken)
    }

    class BackgroundQueueProcessor {
        +Task ExecuteAsync(CancellationToken)
    }

    IBackgroundTaskQueue <|.. BackgroundTaskQueue
    BackgroundQueueProcessor --> IBackgroundTaskQueue

    %% ===== INFRASTRUCTURE =====
    class FeatureTypeRegistry {
        <<static>>
        +GetDescriptor(string) FeatureTypeDescriptor
    }

    class FeatureTypeDescriptor {
        +string FeatureType
        +Type EntityType
        +string TableName
        +Func~DbSet~ DbSetAccessor
    }

    class FeatureQueryHelper {
        <<static>>
        +LoadAllFeaturesAsync(conn, userId, skip, take, ct)
        +LoadByLayerAsync(conn, userId, layer, skip, take, ct)
    }

    class PasswordValidator {
        <<static>>
        +Validate(string password) string?
    }

    class SqlFragments {
        <<static>>
        +PolygonFromDataTemplate
        +LineStringFromDataTemplate
        +PolygonFromData
        +LineStringFromData
    }

    class UserRoles {
        <<static>>
        +CommuneUser
        +DairaAdmin
        +WilayaAdmin
        +NationalAdmin
    }

    ScatteredAreaService --> FeatureQueryHelper
    ScatteredAreaService --> SqlFragments
    FeatureQueryHelper --> SqlFragments
    FeatureQueryHelper --> FeatureTypeRegistry

    %% ===== CONTROLLERS =====
    class ControllerBase {
        <<abstract>>
    }

    class NarsControllerBase {
        <<abstract>>
        +Guid CurrentUserId
        +string CurrentUserRole
        +int? CurrentCommuneId
        +int? CurrentDairaId
        +int? CurrentWilayaId
    }

    class AuthController {
        +Task SignIn(SignInRequest)
        +Task Logout()
        +Task Refresh()
        +Task CurrentUser()
    }

    class AdminSignupController {
        +Task AdminSignup(AuthorizedAdminSignupRequest)
    }

    class AdminController {
        +Task Overview(int skip, int take)
        +Task GetWilaya(int wilayaId)
        +Task GetDaira(int dairaId)
    }

    class AdminUserController {
        +Task CreateManagedUser(CreateAdminRequest)
        +Task GetManageableUsers()
        +Task UpdateManagedUser(Guid userId)
        +Task DeleteManagedUser(Guid userId)
    }

    class FeaturesController {
        +Task Save(FeatureSaveRequest)
        +Task Update(Guid id, FeatureUpdateRequest)
        +Task Delete(Guid id)
        +Task Load()
        +Task Clear(ClearFeaturesRequest)
        +Task GetStats()
    }

    class FeatureCatalogController {
        +Task GetFeatureTypes()
        +Task LoadByLayer(string layerType)
    }

    class ValidationController {
        +Task CheckMainUrbanExists()
        +Task CheckDistrictCoverage()
        +Task ValidateRoad(ValidateRoadRequest)
        +Task ValidateDistrict(ValidateDistrictRequest)
    }

    class SpatialController {
        +Task GetRoadSide(RoadSideRequest)
        +Task RefreshScatteredAreas()
    }

    class LocationsController {
        +Task GetWilayas()
        +Task GetDairas()
        +Task GetCommunes()
        +Task GetCommuneBoundary(int id)
    }

    class UsersController {
        +Task UpdateProfile(UpdateUserRequest)
    }

    class FieldController {
        +Task GetFeatures()
        +Task Inspect()
        +Task GetInspections(Guid featureId)
        +Task CreateEntrance()
    }

    class DraftFeaturesController {
        +Task Segment(SegmentationRequest)
        +Task List()
        +Task Accept(Guid id)
        +Task Reject(Guid id)
    }

    class LogsController {
        +Task SubmitLog(ClientLogEntry)
    }

    class PagesController {
        +Task Index()
        +Task Login()
        +Task Map()
    }

    ControllerBase <|-- NarsControllerBase

    NarsControllerBase <|-- AuthController
    NarsControllerBase <|-- AdminSignupController
    NarsControllerBase <|-- AdminController
    NarsControllerBase <|-- AdminUserController
    NarsControllerBase <|-- FeaturesController
    NarsControllerBase <|-- FeatureCatalogController
    NarsControllerBase <|-- ValidationController
    NarsControllerBase <|-- SpatialController
    NarsControllerBase <|-- LocationsController
    NarsControllerBase <|-- UsersController
    NarsControllerBase <|-- FieldController
    NarsControllerBase <|-- DraftFeaturesController
    NarsControllerBase <|-- LogsController
    NarsControllerBase <|-- PagesController

    AuthController --> JwtService
    AuthController --> AppDbContext
    AdminController --> AppDbContext
    FeaturesController --> AppDbContext
    FeaturesController --> IScatteredAreaService
    FeaturesController --> IBackgroundTaskQueue
    FeaturesController --> FeatureTypeRegistry
    ValidationController --> FeatureQueryHelper
    SpatialController --> IScatteredAreaService
    SpatialController --> FeatureQueryHelper
    DraftFeaturesController --> FeatureTypeRegistry
```
