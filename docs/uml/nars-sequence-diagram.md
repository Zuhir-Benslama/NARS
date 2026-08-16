# NARS Backend - Sequence Diagrams

## 1. Authentication Flow

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant AuthController
    participant AppDbContext
    participant JwtService
    participant PasswordValidator

    User->>AuthController: POST /api/signin {username, password}
    AuthController->>AppDbContext: FindUserByUsername(username)
    AppDbContext-->>AuthController: User entity

    alt User not found
        AuthController-->>User: 401 Unauthorized
    else Invalid credentials
        AuthController->>AppDbContext: IncrementFailedLoginAttempts()
        alt Too many attempts
            AppDbContext->>AppDbContext: LockAccount(30 min)
        end
        AuthController-->>User: 401 Unauthorized
    else Account locked
        AuthController-->>User: 423 Locked
    else Valid credentials
        AuthController->>AppDbContext: ResetFailedLoginAttempts()
        AuthController->>JwtService: GenerateToken(user)
        JwtService-->>AuthController: JWT token
        AuthController->>AuthController: Generate refresh token (SHA-256)
        AuthController->>AppDbContext: StoreRefreshToken(userId, hash)

        Note over AuthController,User: Set HttpOnly, SameSite=Lax cookies
        AuthController-->>User: 200 OK (JWT + refresh token cookies)
    end
```

## 2. Feature Creation Flow

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant FeaturesController
    participant FeatureTypeRegistry
    participant AppDbContext
    participant IBackgroundTaskQueue
    participant BackgroundQueueProcessor
    participant ScatteredAreaService

    User->>FeaturesController: POST /api/features FeatureSaveRequest
    FeaturesController->>FeaturesController: Validate ownership/permissions

    FeaturesController->>FeatureTypeRegistry: GetDescriptor(featureType)
    FeatureTypeRegistry-->>FeaturesController: FeatureTypeDescriptor

    FeaturesController->>FeaturesController: Create entity via factory
    FeaturesController->>AppDbContext: Add(entity)
    AppDbContext->>AppDbContext: SaveChangesAsync()
    AppDbContext-->>FeaturesController: Entity with generated Id

    alt Area created/updated
        FeaturesController->>IBackgroundTaskQueue: QueueBackgroundWorkItemAsync(RefreshScatteredAreas)
        IBackgroundTaskQueue-->>FeaturesController: Queued
        FeaturesController-->>User: 200 OK {id}

        BackgroundQueueProcessor->>IBackgroundTaskQueue: DequeueAsync()
        IBackgroundTaskQueue-->>BackgroundQueueProcessor: workItem
        BackgroundQueueProcessor->>ScatteredAreaService: RefreshAsync(userId, communeId)
        ScatteredAreaService->>AppDbContext: Get commune boundary
        ScatteredAreaService->>AppDbContext: Get union of urban areas
        ScatteredAreaService->>ScatteredAreaService: Compute scattered = boundary - areas
        ScatteredAreaService->>AppDbContext: Update scattered geometry
    else Non-area feature
        FeaturesController-->>User: 200 OK {id}
    end
```

## 3. Feature Load Flow (Cross-Table Query)

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant FeaturesController
    participant FeatureQueryHelper
    participant FeatureTypeRegistry
    participant PostgreSQL
    participant SqlFragments

    User->>FeaturesController: GET /api/features
    FeaturesController->>FeaturesController: Get CurrentUserId

    FeaturesController->>FeatureQueryHelper: LoadFeaturesAsync(userId)

    FeatureQueryHelper->>FeatureTypeRegistry: GetAllDescriptors()
    FeatureTypeRegistry-->>FeatureQueryHelper: List~FeatureTypeDescriptor~

    FeatureQueryHelper->>FeatureQueryHelper: Build UNION ALL query
    Note over FeatureQueryHelper: SELECT id, layer, label, data, ST_AsText(geometry)<br/>FROM areas WHERE user_id = ?<br/>UNION ALL<br/>FROM roads WHERE user_id = ?<br/>UNION ALL ...

    FeatureQueryHelper->>SqlFragments: ST_AsText(geometry)
    FeatureQueryHelper->>PostgreSQL: Execute UNION ALL query
    PostgreSQL-->>FeatureQueryHelper: Raw feature rows

    FeatureQueryHelper->>FeatureQueryHelper: Map rows to DTOs
    FeatureQueryHelper-->>FeaturesController: List~FeatureDto~

    FeaturesController-->>User: 200 OK {features: [...]}
```

## 4. Admin User Creation Flow

```mermaid
sequenceDiagram
    autonumber
    actor Admin
    participant AuthController
    participant AppDbContext
    participant PasswordValidator
    participant UserRoles

    Admin->>AuthController: POST /api/admin/authorized-signup
    Note over AuthController: Requires JWT authentication

    AuthController->>AuthController: Get current user role/scope
    Admin->>AuthController: CreateAdminRequest {role, name, email, ...}

    AuthController->>AuthController: Validate hierarchy
    Note over AuthController: national_admin -> wilaya_admin<br/>wilaya_admin -> daira_admin<br/>daira_admin -> commune_user

    alt Invalid hierarchy
        AuthController-->>Admin: 403 Forbidden
    else Email already exists
        AuthController-->>Admin: 409 Conflict
    else Scope mismatch
        AuthController-->>Admin: 403 Forbidden
    else Valid
        AuthController->>PasswordValidator: Validate(password)
        alt Weak password
            AuthController-->>Admin: 400 Bad Request
        else Strong password
            AuthController->>AppDbContext: Create user with BCrypt hash
            AuthController->>AppDbContext: SaveChangesAsync()
            AuthController-->>Admin: 200 OK {user}
        end
    end
```

## 5. Road-Side Determination Flow

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant SpatialController
    participant FeatureQueryHelper
    participant PostgreSQL
    participant Turf.js equivalent

    User->>SpatialController: POST /api/road-side RoadSideRequest
    Note over SpatialController: {roadId, lat, lng}

    SpatialController->>FeatureQueryHelper: Get road geometry by ID
    FeatureQueryHelper->>PostgreSQL: SELECT geometry FROM roads WHERE id = ?
    PostgreSQL-->>FeatureQueryHelper: LineString geometry

    SpatialController->>SpatialController: Project point onto line
    SpatialController->>SpatialController: Compute cross product
    Note over SpatialController: Cross product determines<br/>left or right side of road

    SpatialController->>FeatureQueryHelper: Get existing entrances on road side
    FeatureQueryHelper->>PostgreSQL: SELECT COUNT FROM house_entrances<br/>WHERE road_id = ? AND side = ?
    PostgreSQL-->>FeatureQueryHelper: Count

    SpatialController->>SpatialController: Compute next entrance number
    SpatialController-->>User: 200 OK {side, entranceNumber}
```
