// ─── SHARED TYPES ─────────────────────────────────────────────────────────────
// Re-exports from domain-specific type modules for backward compatibility.
// New code may import directly from the domain modules (e.g. "./types/features").

export type { LatLng } from "./types/coordinates"

export type { DrawType, Phase } from "./types/phases"

export type { AreaType, DistrictType, RoadType, PublicSpaceType } from "./types/feature-types"

export type {
  FeatureTypeKey,
  FeatureData,
  FeatureDataByType,
  AreaFeatureData,
  DistrictFeatureData,
  CityCenterFeatureData,
  RoadFeatureData,
  HouseEntranceFeatureData,
  PublicBuildingFeatureData,
  PublicSpaceFeatureData,
  NamingPanelFeatureData,
  LayerEntry,
} from "./types/features"

export type {
  ModalState,
  ModalResult,
  AreaModalResult,
  DistrictModalResult,
  CityCenterModalResult,
  RoadModalResult,
  HouseEntranceModalResult,
  PublicBuildingModalResult,
  PublicSpaceModalResult,
  NamingPanelModalResult,
} from "./types/modal"

export type { UserRole, CommuneInfo, DairaInfo, WilayaInfo, UserInfo } from "./types/user"

export type {
  UserFeatureStats,
  AdminInfo,
  ManageableUser,
  CommuneReport,
  DairaReport,
  WilayaReport,
  WilayaSummary,
  NationalOverview,
} from "./types/admin"

export type { FeatureCounts, AppStoreState } from "./types/store"

export type {
  SaveResult,
  ValidateRoadResponse,
  ValidateDistrictResponse,
  DistrictCoverageResponse,
  RoadSideResponse,
  DbFeature,
} from "./types/api"
