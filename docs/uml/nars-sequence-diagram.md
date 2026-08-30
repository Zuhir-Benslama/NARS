# NARS Backend - Sequence Diagrams

## 1. Sign-In Flow

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant AuthController
    participant UserAuthorizationService
    participant RefreshTokenService
    participant JwtService
    participant SecurityStampCache
    participant PostgreSQL

    User->>AuthController: POST /api/signin {username, password}
    Note over AuthController: Rate limit: Auth policy (5 req / 30 s)

    AuthController->>AuthController: Normalize username to lowercase
    AuthController->>UserAuthorizationService: VerifyCredentialsAsync(username, password, maxAttempts, lockoutMinutes)

    UserAuthorizationService->>PostgreSQL: SELECT * FROM users WHERE username = ?
    PostgreSQL-->>UserAuthorizationService: User or null

    alt User not found
        UserAuthorizationService->>UserAuthorizationService: BCrypt.Verify against DummyHash (constant-time)
        UserAuthorizationService-->>AuthController: Invalid
    else Wrong password
        UserAuthorizationService->>UserAuthorizationService: BCrypt.Verify (constant-time mismatch)
        UserAuthorizationService->>RefreshTokenService: RecordFailedLoginAsync(userId)
        RefreshTokenService->>PostgreSQL: UPDATE users SET failed_attempts++, locked_until = ?
        alt Max attempts reached
            RefreshTokenService->>SecurityStampCache: EvictStamp(userId)
            RefreshTokenService->>PostgreSQL: UPDATE users SET security_stamp = new_guid
            Note over PostgreSQL: All existing JWTs invalidated
        end
        UserAuthorizationService-->>AuthController: Invalid
    else Account locked
        UserAuthorizationService-->>AuthController: Locked
    else Valid credentials
        UserAuthorizationService-->>AuthController: Success(user)
    end

    alt !success
        AuthController-->>User: 401 "Invalid username or password"
        Note over AuthController: Identical message for all failure modes
    else success
        AuthController->>RefreshTokenService: ResetFailedAttemptsIfNeededAsync(user)
        RefreshTokenService->>PostgreSQL: UPDATE users SET failed_attempts = 0, locked_until = NULL

        AuthController->>JwtService: CreateToken(userId, username, name, email, communeId, securityStamp, role, ...)
        JwtService->>JwtService: Build claims + sign with HS256
        JwtService-->>AuthController: JWT access token (60 min expiry)

        AuthController->>RefreshTokenService: IssueRefreshTokenAsync(userId)
        RefreshTokenService->>JwtService: CreateRefreshToken()
        JwtService->>JwtService: 64 random bytes (CSPRNG) + SHA-256 hash
        JwtService-->>RefreshTokenService: (rawToken, hash)
        RefreshTokenService->>PostgreSQL: INSERT INTO refresh_tokens (user_id, token_hash, expires_at)
        RefreshTokenService-->>AuthController: rawToken

        AuthController->>AuthController: Set-Cookie: access_token (HttpOnly, Secure, SameSite=Lax)
        AuthController->>AuthController: Set-Cookie: refresh_token (HttpOnly, Secure, SameSite=Lax)

        opt User has CommuneId
            AuthController->>PostgreSQL: SELECT commune/daira/wilaya names
        end

        AuthController-->>User: 200 OK {success, user: {id, username, name, email, role, commune}}
    end
```

## 2. Refresh Token Rotation Flow

```mermaid
sequenceDiagram
    autonumber
    actor Client
    participant AuthController
    participant RefreshTokenService
    participant JwtService
    participant PostgreSQL

    Client->>AuthController: POST /api/refresh (Cookie: refresh_token=...)
    Note over AuthController: Rate limit: Auth policy

    AuthController->>AuthController: Read raw token from cookie
    AuthController->>RefreshTokenService: RotateRefreshTokenAsync(rawToken)

    RefreshTokenService->>RefreshTokenService: SHA-256 hash the raw token

    RefreshTokenService->>PostgreSQL: BEGIN READ COMMITTED
    RefreshTokenService->>PostgreSQL: SELECT * FROM refresh_tokens WHERE token_hash = ? AND expires_at > now() FOR UPDATE SKIP LOCKED

    alt Token not found
        RefreshTokenService-->>AuthController: Invalid or expired
        AuthController-->>Client: 401 Unauthorized
    else Token found but already revoked (REPLAY ATTACK)
        RefreshTokenService->>PostgreSQL: Revoke ALL user tokens
        RefreshTokenService-->>AuthController: Replay detected
        AuthController-->>Client: 401 Unauthorized
    else Token found, not revoked
        RefreshTokenService->>PostgreSQL: Load user, check lockout
        alt User is locked
            RefreshTokenService->>PostgreSQL: Revoke this token
            RefreshTokenService-->>AuthController: Account locked
            AuthController-->>Client: 401 Unauthorized
        else User OK
            RefreshTokenService->>PostgreSQL: UPDATE refresh_tokens SET revoked = true WHERE token_hash = ?
            RefreshTokenService->>JwtService: CreateRefreshToken()
            JwtService-->>RefreshTokenService: (newRaw, newHash)
            RefreshTokenService->>PostgreSQL: INSERT INTO refresh_tokens (newHash, expires_at)
            RefreshTokenService->>PostgreSQL: COMMIT
            RefreshTokenService->>JwtService: CreateToken(userId, username, ...)
            JwtService-->>RefreshTokenService: newAccessToken
            RefreshTokenService-->>AuthController: (newRaw, newAccessToken, expiry)
        end
    end

    AuthController->>AuthController: Set-Cookie: access_token (new JWT)
    AuthController->>AuthController: Set-Cookie: refresh_token (new raw token)
    AuthController-->>Client: 200 OK {success: true, token_type: "bearer"}
```

## 3. Security Stamp Validation (Every Authenticated Request)

```mermaid
sequenceDiagram
    autonumber
    participant Middleware
    participant JwtService
    participant SecurityStampCache
    participant PostgreSQL
    participant Controller

    Middleware->>JwtService: ValidateToken(accessToken)
    JwtService-->>Middleware: ClaimsPrincipal or null

    alt Token invalid or expired
        Middleware-->>Controller: 401 Unauthorized
    else Token valid
        Middleware->>Middleware: Extract user_id + security_stamp claims
        alt Claims missing
            Middleware-->>Controller: 401 "Missing identity claims"
        else Claims present
            Middleware->>SecurityStampCache: GetStampAsync(userId)
            alt Cache hit
                SecurityStampCache-->>Middleware: currentStamp
            else Cache miss
                SecurityStampCache->>PostgreSQL: SELECT security_stamp FROM users WHERE id = ?
                PostgreSQL-->>SecurityStampCache: stamp
                SecurityStampCache-->>Middleware: currentStamp (cached 30s)
            end

            alt Stamp mismatch (session invalidated)
                Middleware-->>Controller: 401 "Session invalidated (security stamp rotated)"
            else Stamp matches
                Middleware->>Controller: Continue to action
            end
        end
    end
```

## 4. Feature Creation Flow

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

## 5. Feature Load Flow (Cross-Table Query)

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

## 6. Admin User Creation Flow

```mermaid
sequenceDiagram
    autonumber
    actor Admin
    participant AdminUserController
    participant IUserCreationService
    participant IUserAuthorizationService
    participant PasswordValidator
    participant PostgreSQL

    Admin->>AdminUserController: POST /api/admin/users CreateAdminRequest
    Note over AdminUserController: Requires JWT + UserManagementRoles

    AdminUserController->>IUserAuthorizationService: CreateManagedUserAsync(request, creatorId)
    IUserAuthorizationService->>IUserAuthorizationService: Validate hierarchy
    Note over IUserAuthorizationService: national_admin -> wilaya_admin<br/>wilaya_admin -> daira_admin<br/>daira_admin -> commune_user

    alt Invalid hierarchy
        IUserAuthorizationService-->>AdminUserController: 403 Forbidden
    else Email already exists
        IUserAuthorizationService-->>AdminUserController: 409 Conflict
    else Scope mismatch
        IUserAuthorizationService-->>AdminUserController: 403 Forbidden
    else Valid
        IUserAuthorizationService->>IUserCreationService: CreateUserAsync(request, creatorId)
        IUserCreationService->>PasswordValidator: Validate(password)
        alt Weak password
            IUserCreationService-->>IUserAuthorizationService: 400 Bad Request
        else Strong password
            IUserCreationService->>PostgreSQL: Hash password with BCrypt
            IUserCreationService->>PostgreSQL: INSERT INTO users
            IUserCreationService-->>IUserAuthorizationService: User
        end
        IUserAuthorizationService-->>AdminUserController: UserInfo
        AdminUserController-->>Admin: 200 OK {user}
    end
```

## 7. Road-Side Determination Flow

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant SpatialController
    participant FeatureQueryHelper
    participant PostgreSQL
    participant GeometryHelper

    User->>SpatialController: POST /api/road-side RoadSideRequest
    Note over SpatialController: {roadId, lat, lng}

    SpatialController->>FeatureQueryHelper: Get road geometry by ID
    FeatureQueryHelper->>PostgreSQL: SELECT geometry FROM roads WHERE id = ?
    PostgreSQL-->>FeatureQueryHelper: LineString geometry

    SpatialController->>GeometryHelper: FindNearestSegmentIndex(lat, lng, coords)
    GeometryHelper-->>SpatialController: nearest index

    SpatialController->>GeometryHelper: DetermineSide(lat, lng, segStart, segEnd)
    Note over GeometryHelper: Cross product determines<br/>left or right side of road
    GeometryHelper-->>SpatialController: side (left/right)

    SpatialController->>GeometryHelper: SuggestEntranceNumber(roadId, side)
    GeometryHelper->>PostgreSQL: SELECT COUNT FROM house_entrances WHERE road_id = ? AND side = ?
    PostgreSQL-->>GeometryHelper: existing count
    GeometryHelper-->>SpatialController: next number

    SpatialController-->>User: 200 OK {side, suggestedNumber}
```
