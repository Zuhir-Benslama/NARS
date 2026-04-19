// ─── SHARED TYPES ─────────────────────────────────────────────────────────────

// ── Phases ────────────────────────────────────────────────────────────────────

export type DrawType = 'polygon' | 'polyline' | 'marker' | 'circle'

export interface Phase {
    index: number
    key: string
    label: string
    drawType: DrawType
    color: string
    hint: string
    geometryType: 'Polygon' | 'LineString' | 'Point'
}

// ── Feature sub-types ─────────────────────────────────────────────────────────

export interface AreaType {
    key: string
    label: string
    color: string
}

export interface DistrictType {
    key: string
    label: string
    allowInScattered?: boolean
}

export interface RoadType {
    key: string
    label: string
    category: 'primary' | 'secondary' | 'tertiary'
}

export interface PublicSpaceType {
    key: string
    label: string
    color: string
}

// ── Coordinates ───────────────────────────────────────────────────────────────

export interface LatLng {
    lat: number
    lng: number
}

// ── Feature data stored in DB ─────────────────────────────────────────────────

export interface FeatureData {
    type: string
    label: string
    decisionNumber: string
    decisionDate: string
    coordinates?: LatLng[]
    lat?: number
    lng?: number
    radius?: number
    areaTypeKey?: string
    districtTypeKey?: string
    roadTypeKey?: string
    roadDbId?: string
    roadLabel?: string
    side?: 'left' | 'right'
    entranceNumber?: number
    mainEntranceDbId?: string
    mainEntranceLabel?: string
    bisNumber?: number
    entranceTypeKey?: 'main_entrance' | 'secondary_entrance'
    spaceTypeKey?: string
    sectorKey?: string
    buildingTypeKey?: string
    geometry?: string
}

// ── Layer entry stored in featureLayers ───────────────────────────────────────

export interface LayerEntry {
    id: string
    dbId: string // database primary key (UUID v7) — use this for API calls
    data: FeatureData
    type: 'polygon' | 'line' | 'circle' | 'marker'
}

// ── Road / entrance options for the modal ────────────────────────────────────

export interface RoadOption {
    idx: number
    label: string
    dbId: string
}

export interface EntranceOption {
    idx: number
    label: string
    dbId: string
}

// ── Modal form state ──────────────────────────────────────────────────────────

export interface ModalState {
    visible: boolean
    phaseIndex: number | null
    isEdit: boolean
    editDbId: string | null
    label: string
    decisionNumber: string
    decisionDate: string
    errors: Record<string, string>
    areaTypeKey: string
    mainUrbanExists: boolean
    districtTypeKey: string
    roadTypeKey: string
    entranceTypeKey: 'main_entrance' | 'secondary_entrance'
    roadOptions: RoadOption[]
    selectedRoadIdx: number | ''
    entranceSide: 'left' | 'right' | null
    entranceNumber: number | null
    entranceSideLoading: boolean
    mainEntranceOptions: EntranceOption[]
    selectedMainIdx: number | ''
    bisNumber: number | null
    spaceTypeKey: string
    sectorKey: string
    buildingTypeKey: string
    radius: number | null
}

// ── Commune / user ────────────────────────────────────────────────────────────

export interface CommuneInfo {
    id: number | null
    name_fr: string | null
    name_ar: string | null
    latitude: number | null
    longitude: number | null
}

export interface UserInfo {
    id: number
    username: string
    name: string
    email: string
    commune: CommuneInfo
}

// ── App Store State ──────────────────────────────────────────────────────────

export interface AppStoreState {
    currentPhase: number
    counts: FeatureCounts
    cityCenterMode: 'city_center' | 'auto' | null
    cityCenterLatLng: LatLng | null
    user: UserInfo | null
    municipalityName: string
    loadError: boolean
    isLoading: boolean
    referenceRoadDbId: string | null
    referenceEntranceDbId: string | null
}

// ── Store ─────────────────────────────────────────────────────────────────────

export interface FeatureCounts {
    areas: number
    cityCenter: number
    districts: number
    roads: number
    mainEntrances: number
    secondaryEntrances: number
    publicBuildings: number
    publicSpaces: number
    namingPanels: number
}

export interface AppStore {
    currentPhase: number
    counts: FeatureCounts
    cityCenterMode: 'city_center' | 'auto' | null
    cityCenterLatLng: LatLng | null
    user: UserInfo | null
    municipalityName: string
    loadError: boolean
    isLoading: boolean
    modal: ModalState
    referenceRoadDbId: string | null
    referenceEntranceDbId: string | null
}

// ── API responses ─────────────────────────────────────────────────────────────

export interface SaveResult {
    ok: boolean
    error?: string
    data?: { id: string }
}

export interface ValidateRoadResponse {
    valid: boolean
    error: string | null
}

export interface ValidateDistrictResponse {
    valid: boolean
    error: string | null
}

export interface DistrictCoverageResponse {
    covered: boolean
    message: string
}

export interface RoadSideResponse {
    side: 'left' | 'right'
    suggestedNumber: number
}

export interface ScatteredRefreshResponse {
    success: boolean
    geojson: string | null
    message: string
}

export interface DbFeature {
    id: string
    layer: string
    feature_type: string
    label: string
    data: FeatureData | string
    created_at: string
}

export type ModalResult = Omit<FeatureData, 'type' | 'coordinates' | 'lat' | 'lng'>
