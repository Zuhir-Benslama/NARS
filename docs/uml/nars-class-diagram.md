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

    %% ===== NON-FEATURE MODELS =====
    class Inspection {
        +Guid Id
        +Guid FeatureId
        +Guid UserId
        +string Type
        +string Data
        +string Status
        +DateTime CreatedAt
        +DateTime? UpdatedAt
    }

    class ErrorLog {
        +Guid Id
        +Guid? UserId
        +string Level
        +string Code
        +string Message
        +string? Context
        +string? Url
        +string? Method
        +string? IpAddress
        +string? UserAgent
        +DateTime CreatedAt
    }

    class AiDraftFeature {
        +Guid Id
        +string FeatureType
        +string GeometryGeoJson
        +string Source
        +double Confidence
        +string Status
        +int CommuneId
        +Guid? ReviewedBy
        +DateTimeOffset? ReviewedAt
        +DateTimeOffset CreatedAt
        +string? SourceTileRef
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
        +int FailedLoginAttempts
        +DateTime? LockedUntil
        +string SecurityStamp
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

    User "1" --> "0..*" RefreshToken : has
    User "1" --> "0..*" Inspection : creates
    User "1" --> "0..*" ErrorLog : creates
    User --> Commune : CommuneId?
    User --> Daira : DairaId?
    User --> Wilaya : WilayaId?

    Wilaya "1" --> "0..*" Daira
    Daira "1" --> "0..*" Commune
    Commune "1" --> "0..1" CommuneBoundary

    %% ===== DATABASE CONTEXT =====
    class AppDbContext {
        +DbSet~FeatureRegistry~ FeatureRegistry
        +DbSet~Area~ Areas
        +DbSet~District~ Districts
        +DbSet~CityCenter~ CityCenters
        +DbSet~Road~ Roads
        +DbSet~HouseEntrance~ HouseEntrances
        +DbSet~PublicBuilding~ PublicBuildings
        +DbSet~PublicSpace~ PublicSpaces
        +DbSet~NamingPanel~ NamingPanels
        +DbSet~Inspection~ Inspections
        +DbSet~ErrorLog~ ErrorLogs
        +DbSet~User~ Users
        +DbSet~RefreshToken~ RefreshTokens
        +DbSet~Wilaya~ Wilayas
        +DbSet~Daira~ Dairas
        +DbSet~Commune~ Communes
        +DbSet~CommuneBoundary~ CommuneBoundaries
        +DbSet~AiDraftFeature~ AiDraftFeatures
    }

    AppDbContext o-- FeatureBase
    AppDbContext o-- User
    AppDbContext o-- Wilaya
    AppDbContext o-- Daira
    AppDbContext o-- Commune
    AppDbContext o-- RefreshToken
    AppDbContext o-- Inspection
    AppDbContext o-- ErrorLog
    AppDbContext o-- AiDraftFeature

    %% ===== SERVICES =====
    class IJwtService {
        <<interface>>
        +CreateToken(userId, username, name, email, communeId, securityStamp, role, dairaId, wilayaId) string
        +ValidateToken(string) ClaimsPrincipal?
        +CreateRefreshToken() (string raw, string hash)
        +AccessTokenExpiresIn TimeSpan
    }

    class JwtService {
        +CreateToken(...) string
        +ValidateToken(string) ClaimsPrincipal?
        +CreateRefreshToken() (string raw, string hash)
    }
    IJwtService <|.. JwtService

    class IRefreshTokenService {
        <<interface>>
        +IssueRefreshTokenAsync(userId) (raw, hash, expiresAt)
        +RotateRefreshTokenAsync(rawToken) RefreshTokenResult
        +MintAccessTokenAsync(rawToken) RefreshTokenResult
        +RevokeAllUserTokensAsync(userId) Task
        +RecordFailedLoginAsync(user, maxFailedAttempts, lockoutMinutes, utcNow) Task
        +ResetFailedAttemptsIfNeededAsync(user) Task
    }

    class RefreshTokenService {
        +IssueRefreshTokenAsync(userId) (raw, hash, expiresAt)
        +RotateRefreshTokenAsync(rawToken) RefreshTokenResult
        +MintAccessTokenAsync(rawToken) RefreshTokenResult
        +RevokeAllUserTokensAsync(userId) Task
        +RecordFailedLoginAsync(user, maxFailedAttempts, lockoutMinutes, utcNow) Task
        +ResetFailedAttemptsIfNeededAsync(user) Task
    }
    IRefreshTokenService <|.. RefreshTokenService

    class IFeatureService {
        <<interface>>
        +RoadExistsAsync(roadId, userId, ct) Task~bool~
        +SaveFeatureAsync(entity, featureType, ct) Task~Guid~
        +GetFeatureTypeAsync(featureId, ct) Task~string?~
        +OwnsFeatureAsync(featureId, featureType, userId, ct) Task~bool~
        +UpdateFeatureAsync(command, ct) Task~bool~
        +DeleteFeatureAsync(featureId, userId, featureType, ct) Task~bool~
        +ClearAllFeaturesAsync(userId, ct) Task~int~
        +QueueScatteredRefreshAsync(userId, communeId) ValueTask
    }

    class IFeatureStatsService {
        <<interface>>
        +GetFeatureCountsAsync(userId, ct) Task~Dictionary~string, long~~
        +GetUserFeatureCountsAsync(userIds, ct) Task~Dictionary~Guid, UserFeatureStats~~
        +LoadAllFeaturesAsync(userId, skip, take, ct) (features, totalCount)
        +LoadByLayerAsync(userId, layer, skip, take, ct) (features, totalCount)
    }

    class IFeatureCleanupService {
        <<interface>>
        +DeleteAllFeaturesForUserAsync(db, userId, ct) Task~int~
    }

    class IUserAuthorizationService {
        <<interface>>
        +CanCreateRole(callerRole, targetRole) bool
        +ValidateCreateUserScopeAsync(callerRole, callerDaira?, callerWilaya?, targetRole, commune?, daira?, wilaya?) ScopeValidationResult
        +ValidateManagedUserScopeAsync(callerRole, callerCommune?, callerDaira?, callerWilaya?, targetRole, commune?, daira?, wilaya?) ScopeValidationResult
        +GetManageableUsersAsync(callerRole, commune?, daira?, wilaya?, skip, take) PagedResponse~AdminUserSummary~
        +FindUserByIdAsync(userId, ct) Task~User?~
        +FindUserByUsernameAsync(username, ct) Task~User?~
        +VerifyCredentialsAsync(username, password, maxAttempts, lockoutMinutes) CredentialCheckResult
        +UpdateManagedUserAsync(callerUserId, callerRole, targetUserId, body) UserUpdateResult
        +DeleteUserAsync(userId, ct) Task~bool~
    }

    class IUserCreationService {
        <<interface>>
        +CreateUserAsync(callerRole, name, email, phone, username, password, targetRole, commune?, daira?, wilaya?) ManagedUserCreationResult
        +ValidateAndCreateUserAsync(name, email, phone, username, password, role, commune?, daira?, wilaya?) UserCreationResult
        +SaveUserAsync(user, ct) Task
    }

    class IUserProfileService {
        <<interface>>
        +GetUserByIdAsync(userId, ct) Task~User?~
        +IsUsernameTakenAsync(username, ct) Task~bool~
        +IsEmailTakenAsync(email, ct) Task~bool~
        +UpdateUserAsync(user, ct) Task
        +UpdateCredentialsAsync(userId, request) UpdateCredentialsResult
    }

    class IValidationService {
        <<interface>>
        +CheckRoadConnectivityAsync(userId, wkt, maxDistanceMeters, ct) Task~bool~
        +CheckDistrictCoverageAsync(userId, toleranceMeters, ct) Task~bool~
        +CheckDistrictOverlapAsync(userId, wkt, ct) Task~bool~
        +CountSiblingsInSameAreaAsync(userId, wkt, ct) Task~long~
        +CheckDistrictAdjacencyAsync(userId, wkt, ct) Task~bool~
        +UserHasCentralUrbanAreaAsync(userId, ct) Task~bool~
        +CountUserRoadsAsync(userId, ct) Task~int~
        +CountUserDistrictsAsync(userId, ct) Task~int~
        +CountUserUrbanAreasAsync(userId, ct) Task~int~
    }

    class IFieldService {
        <<interface>>
        +QueryFeaturesAsync(descriptor, communeId, skip, take, ct) (items, total)
        +GetFeatureOwnerAsync(featureType, featureId, ct) (userId, communeId)?
        +GetInspectionsAsync(featureId, skip, take, ct) Task~List~FieldInspectionResponse~~
        +GetRoadOwnerAsync(roadId, ct) (ownerUserId, communeId)?
        +CreateEntranceAsync(roadId, ownerUserId, creatorUserId, label, data, ct) Task~Guid~
        +GetFeatureRegistryTypeAsync(featureId, ct) Task~string?~
        +SubmitInspectionAsync(featureId, userId, type, status, data, ct) Task~Guid~
    }

    class IScatteredAreaService {
        <<interface>>
        +RefreshAsync(userId, communeId, ct) Task~bool~
        +GetLastError(userId, communeId) (DateTimeOffset, string)?
    }

    class ScatteredAreaService {
        +RefreshAsync(userId, communeId, ct) Task~bool~
        +GetLastError(userId, communeId) (DateTimeOffset, string)?
    }
    IScatteredAreaService <|.. ScatteredAreaService

    class IRoadQueryService {
        <<interface>>
        +GetUserRoadByIdAsync(roadId, userId, ct) Task~Road?~
    }

    class IEntranceQueryService {
        <<interface>>
        +GetUsedEntranceNumbersAsync(userId, roadId, side, ct) Task~HashSet~int~~
    }

    class IAdminOverviewService {
        <<interface>>
        +GetNationalOverviewAsync(skip, take) (items, total)
        +GetWilayaReportAsync(wilayaId) Task~WilayaReport?~
        +GetDairaReportAsync(dairaId, expectedWilayaId?) Task~DairaReport?~
    }

    class IErrorLogService {
        <<interface>>
        +LogBatchAsync(entries, ct) Task
    }

    class ICommuneScopeService {
        <<interface>>
        +CanAccessCommuneAsync(callerRole, callerCommune?, callerDaira?, callerWilaya?, targetCommuneId, ct) Task~bool~
    }

    class IDraftFeaturesService {
        <<interface>>
        +SegmentTileAsync(callerRole, communeId, tileStream, fileName, contentType, bbox, ct) Task~SegmentSummaryResponse~
        +ListDraftsAsync(callerRole, communeId, featureType?, status, skip, take) PagedResponse~AiDraftFeatureDto~
        +AcceptDraftAsync(callerRole, userId, draftId, ct) DraftReviewResult
        +RejectDraftAsync(callerRole, userId, draftId, ct) DraftReviewResult
    }

    class ISegmentationClient {
        <<interface>>
        +SegmentTileAsync(tileStream, fileName, contentType, bbox, ct) Task~SegmentationResult~
    }

    class ISecurityStampCache {
        <<interface>>
        +GetStampAsync(userId, ct) Task~string?~
        +SetStamp(userId, stamp) void
        +EvictStamp(userId) void
    }

    class IPageAuthService {
        <<interface>>
        +TryAuthenticateAsync(ct) Task~bool~
        +TryRefreshSessionAsync(ct) Task~bool~
    }

    class IBackgroundTaskQueue {
        <<interface>>
        +QueueBackgroundWorkItemAsync(workItem) ValueTask~bool~
        +DequeueAsync(ct) ValueTask~Func~
    }

    class BackgroundTaskQueue {
        +QueueBackgroundWorkItemAsync(workItem) ValueTask~bool~
        +DequeueAsync(ct) ValueTask~Func~
    }
    IBackgroundTaskQueue <|.. BackgroundTaskQueue

    class BackgroundQueueProcessor {
        <<IHostedService>>
        +StartAsync(ct) Task
        +StopAsync(ct) Task
        +DisposeAsync() ValueTask
    }
    BackgroundQueueProcessor --> IBackgroundTaskQueue

    class ILogSanitizer {
        <<interface>>
        +Sanitize(value, maxLen) string
    }

    class IDateTimeProvider {
        <<interface>>
        +UtcNow DateTime
    }

    %% ===== CONTROLLERS =====
    class ControllerBase {
        <<abstract>>
    }

    class NarsControllerBase {
        <<abstract>>
        #Guid? CurrentUserId
        #Guid RequiredCurrentUserId
        #string CurrentUserRole
        #int? CurrentCommuneId
        #int? CurrentDairaId
        #int? CurrentWilayaId
    }

    class AuthController {
        +SignIn(SignInRequest) Task
        +Logout() Task
        +Refresh() Task
        +CurrentUser() Task
    }

    class AdminSignupController {
        +AuthorizedAdminSignup(AuthorizedAdminSignupRequest) Task
    }

    class AdminController {
        +Overview(skip, take) Task
        +GetWilaya(wilayaId) Task
        +GetDaira(dairaId) Task
    }

    class AdminUserController {
        +CreateManagedUser(CreateAdminRequest) Task
        +GetManageableUsers() Task
        +UpdateManagedUser(userId, UpdateAdminRequest) Task
        +DeleteManagedUser(userId) Task
    }

    class FeaturesController {
        +SaveFeature(FeatureSaveRequest) Task
        +UpdateFeature(id, FeatureUpdateRequest) Task
        +DeleteFeature(id) Task
        +LoadFeatures(skip, take) Task
        +ClearFeatures(ClearFeaturesRequest) Task
        +GetStats() Task
    }

    class FeatureCatalogController {
        +GetFeatureTypes() Task
        +LoadByLayer(layerType, skip, take) Task
    }

    class ValidationController {
        +MainUrbanExists() Task
        +DistrictsCoverage() Task
        +ValidateRoad(ValidateRoadRequest) Task
        +ValidateDistrict(ValidateDistrictRequest) Task
    }

    class SpatialController {
        +GetRoadSide(RoadSideRequest) Task
        +GetScatteredStatus() Task
        +RefreshScattered() Task
    }

    class LocationsController {
        +GetWilayas(search?) Task
        +GetDairas(wilayaId?, search?) Task
        +GetCommunes(dairaId?, search?) Task
        +GetCommuneBoundary(id) Task
    }

    class UsersController {
        +UpdateCredentials(UpdateUserRequest) Task
    }

    class FieldController {
        +GetFeatures() Task
        +SubmitInspection(FieldInspectRequest) Task
        +GetInspections(featureId) Task
        +CreateEntranceFromInspection(FieldEntranceCreateRequest) Task
    }

    class DraftFeaturesController {
        +SegmentTile(SegmentTileRequest) Task
        +ListDrafts() Task
        +AcceptDraft(id) Task
        +RejectDraft(id) Task
    }

    class LogsController {
        +SubmitLogs(LogBatch) Task
    }

    class PagesController {
        +Root() Task
        +LoginPage() Task
        +MapPage() Task
    }

    ControllerBase <|-- NarsControllerBase
    ControllerBase <|-- LocationsController

    NarsControllerBase <|-- AuthController
    NarsControllerBase <|-- AdminSignupController
    NarsControllerBase <|-- AdminController
    NarsControllerBase <|-- AdminUserController
    NarsControllerBase <|-- FeaturesController
    NarsControllerBase <|-- FeatureCatalogController
    NarsControllerBase <|-- ValidationController
    NarsControllerBase <|-- SpatialController
    NarsControllerBase <|-- UsersController
    NarsControllerBase <|-- FieldController
    NarsControllerBase <|-- DraftFeaturesController
    NarsControllerBase <|-- LogsController
    NarsControllerBase <|-- PagesController

    %% ===== CONTROLLER DEPENDENCIES =====
    AuthController --> IJwtService
    AuthController --> IRefreshTokenService
    AuthController --> IUserAuthorizationService
    AdminSignupController --> IUserCreationService
    AdminSignupController --> IUserAuthorizationService
    AdminController --> IAdminOverviewService
    AdminUserController --> IUserAuthorizationService
    AdminUserController --> IUserCreationService
    FeaturesController --> IFeatureService
    FeaturesController --> IFeatureStatsService
    ValidationController --> IValidationService
    SpatialController --> IRoadQueryService
    SpatialController --> IScatteredAreaService
    SpatialController --> IEntranceQueryService
    FieldController --> IFieldService
    LocationsController --> IBoundaryService
    LocationsController --> ILocationQueryService
    LocationsController --> ILocationSearchService
    UsersController --> IUserProfileService
    UsersController --> IRefreshTokenService
    DraftFeaturesController --> IDraftFeaturesService
    PagesController --> IPageAuthService
    LogsController --> IErrorLogService

    %% ===== INFRASTRUCTURE =====
    class FeatureTypeRegistry {
        <<static>>
        +GetDescriptor(string) FeatureTypeDescriptor
        +GetAllDescriptors() FeatureTypeDescriptor[]
        +IsValidTableName(string) bool
    }

    class FeatureTypeDescriptor {
        +string Type
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
        +UrbanAreaLayersSqlIn
    }

    class UserRoles {
        <<static>>
        +CommuneUser
        +DairaAdmin
        +WilayaAdmin
        +NationalAdmin
        +FieldWorker
        +IsAdmin(string) bool
        +IsDraftReviewer(string) bool
        +IsCommuneScoped(string) bool
    }

    ScatteredAreaService --> FeatureQueryHelper
    ScatteredAreaService --> SqlFragments
    FeatureQueryHelper --> SqlFragments
    FeatureQueryHelper --> FeatureTypeRegistry
```
