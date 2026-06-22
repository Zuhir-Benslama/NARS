// ─── SHARED TYPES ─────────────────────────────────────────────────────────────
// Re-exports from domain-specific type modules for backward compatibility.
// New code may import directly from the domain modules (e.g. "./types/features").

export type { LatLng } from "./types/coordinates"

export type { DrawType, Phase } from "./types/phases"

export type { AreaType, DistrictType, RoadType, PublicSpaceType } from "./types/feature-types"

export type { FeatureTypeKey, FeatureData, LayerEntry } from "./types/features"

export type { RoadOption, EntranceOption, ModalState, ModalResult } from "./types/modal"

export type { UserRole, CommuneInfo, DairaInfo, WilayaInfo, UserInfo } from "./types/user"

export type {
  UserFeatureStats,
  AdminInfo,
  CommuneReport,
  DairaReport,
  WilayaReport,
  WilayaSummary,
  NationalOverview,
} from "./types/admin"

export type { FeatureCounts, AppStoreState, AppStore } from "./types/store"

export type {
  SaveResult,
  ValidateRoadResponse,
  ValidateDistrictResponse,
  DistrictCoverageResponse,
  RoadSideResponse,
  ScatteredRefreshResponse,
  DbFeature,
} from "./types/api"
