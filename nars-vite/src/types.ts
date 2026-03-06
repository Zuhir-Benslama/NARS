// ─── SHARED TYPES ─────────────────────────────────────────────────────────────
import type * as L from "leaflet"

// ── Phases ────────────────────────────────────────────────────────────────────

export type DrawType = 'polygon' | 'polyline' | 'marker'

export interface Phase {
    index:    number
    key:      string
    label:    string
    drawType: DrawType
    color:    string
    hint:     string
}

// ── Feature sub-types ─────────────────────────────────────────────────────────

export interface AreaType {
    key:   string
    label: string
    color: string
}

export interface DistrictType {
    key:   string
    label: string
}

export interface RoadType {
    key:      string
    label:    string
    category: 'primary' | 'secondary' | 'tertiary'
}

export interface PublicSpaceType {
    key:   string
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
    type:           string
    label:          string
    decisionNumber: string
    decisionDate:   string
    // polygon / polyline
    coordinates?:   LatLng[]
    // marker
    lat?:           number
    lng?:           number
    // areas
    areaTypeKey?:   string
    // districts
    districtTypeKey?: string
    // roads
    roadTypeKey?:   string
    // mainEntrances
    roadDbId?:      number
    roadLabel?:     string
    side?:          'left' | 'right'
    entranceNumber?: number
    // secondaryEntrances
    mainEntranceDbId?:  number
    mainEntranceLabel?: string
    bisNumber?:         number
    // publicSpaces
    spaceTypeKey?:  string
    // scattered (auto-computed)
    geometry?:      string
}

// ── Layer entry stored in featureLayers ───────────────────────────────────────

export interface LayerEntry {
    layer: L.Layer
    data:  FeatureData
}

// ── Road / entrance options for the modal ────────────────────────────────────

export interface RoadOption {
    idx:   number
    label: string
    dbId:  number
}

export interface EntranceOption {
    idx:   number
    label: string
    dbId:  number
}

// ── Modal form state ──────────────────────────────────────────────────────────

export interface ModalState {
    visible:             boolean
    phaseIndex:          number | null
    isEdit:              boolean
    editDbId:            number | null
    label:               string
    decisionNumber:      string
    decisionDate:        string
    errors:              Record<string, string>
    // areas
    areaTypeKey:         string
    mainUrbanExists:     boolean
    // districts
    districtTypeKey:     string
    // roads
    roadTypeKey:         string
    // mainEntrances
    roadOptions:         RoadOption[]
    selectedRoadIdx:     number | ''
    entranceSide:        'left' | 'right' | null
    entranceNumber:      number | null
    entranceSideLoading: boolean
    // secondaryEntrances
    mainEntranceOptions: EntranceOption[]
    selectedMainIdx:     number | ''
    bisNumber:           number | null
    // publicSpaces
    spaceTypeKey:        string
}

// ── Commune / user ────────────────────────────────────────────────────────────

export interface CommuneInfo {
    id:        number | null
    name_fr:   string | null
    name_ar:   string | null
    latitude:  number | null
    longitude: number | null
}

export interface UserInfo {
    id:       number
    username: string
    name:     string
    email:    string
    commune:  CommuneInfo
}

// ── Store ─────────────────────────────────────────────────────────────────────

export interface FeatureCounts {
    areas:               number
    cityCenter:          number
    districts:           number
    roads:               number
    mainEntrances:       number
    secondaryEntrances:  number
    publicBuildings:     number
    publicSpaces:        number
}

export interface AppStore {
    currentPhase:            number
    counts:                  FeatureCounts
    cityCenterMode:          'city_center' | 'auto' | null
    cityCenterLatLng:        LatLng | null
    user:                    UserInfo | null
    municipalityName:        string
    cityCenterDialogVisible: boolean
    modal:                   ModalState
}

// ── API responses ─────────────────────────────────────────────────────────────

export interface SaveResult {
    ok:    boolean
    error?: string
    data?: { id: number }
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
    side:            'left' | 'right'
    suggestedNumber: number
}

export interface ScatteredRefreshResponse {
    success: boolean
    geojson: string | null
    message: string
}

export interface DbFeature {
    id:         number
    type:       string
    layer:      string
    label:      string
    data:       FeatureData | string
    created_at: string
    updated_at: string | null
}

// ── Modal result returned by openModal() ──────────────────────────────────────

export type ModalResult = Omit<FeatureData, 'type' | 'coordinates' | 'lat' | 'lng'>
