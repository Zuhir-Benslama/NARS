// ─── APPLICATION CONFIGURATION ────────────────────────────────────────────────
// Centralized configuration constants for the NARS application.
// Environment variables and magic numbers should be defined here.

// ─── API CONFIGURATION ────────────────────────────────────────────────────────

export const API_CONFIG = {
  /** Base URL for API requests (from environment variable) */
  baseUrl: import.meta.env.VITE_API_BASE ?? "",

  /** Default timeout for API requests in milliseconds */
  defaultTimeout: 10000,

  /** Maximum number of retries for failed API requests */
  maxRetries: 3,

  /** Base delay between retries in milliseconds */
  retryBaseDelay: 1000,

  /** Maximum delay between retries in milliseconds */
  retryMaxDelay: 10000,
} as const

// ─── MAP CONFIGURATION ────────────────────────────────────────────────────────

export const MAP_CONFIG = {
  /** Default map center [longitude, latitude] */
  defaultCenter: [2.5, 28.0] as [number, number],

  /** Default map zoom level */
  defaultZoom: 5,

  /** Minimum allowed zoom level */
  minZoom: 4,

  /** Maximum allowed zoom level */
  maxZoom: 18,

  /** Default map bearing (rotation) in degrees */
  defaultBearing: 0,

  /** Default map pitch (tilt) in degrees */
  defaultPitch: 0,

  /** Timeout for style loading in milliseconds */
  styleLoadTimeout: 10000,

  /** Max zoom for raster tile sources */
  tileMaxZoomSatellite: 17,
  tileMaxZoomStreet: 19,
  tileMaxZoomLight: 19,
  tileMaxZoomDark: 19,

  /** Base tile URLs per style key — configurable via env vars */
  tileUrls: {
    satellite: [
      import.meta.env.VITE_TILE_SATELLITE ??
        "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
    ],
    street: [
      import.meta.env.VITE_TILE_STREET ?? "https://a.tile.openstreetmap.org/{z}/{x}/{y}.png",
    ],
    light: [
      import.meta.env.VITE_TILE_LIGHT ??
        "https://a.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png",
    ],
    dark: [
      import.meta.env.VITE_TILE_DARK ??
        "https://a.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png",
    ],
  },
} as const

// ─── SNAPPING CONFIGURATION ───────────────────────────────────────────────────

export const SNAP_CONFIG = {
  /** Per-type snap thresholds in pixels */
  thresholds: {
    vertex: 40,
    edge: 40,
    circle: 20,
    midpoint: 20,
  } as const,

  /** Phases that support snapping (polygon/polyline geometry only) */
  phases: ["areas", "districts", "roads", "publicBuildings", "publicSpaces"] as const,

  /**
   * Per-phase snap targets — defines what each phase can snap TO.
   * Based on Snapping.xlsx specification.
   * Key = phase being drawn, Value = array of phases it can snap to.
   */
  snapTargets: {
    areas: ["areas"],
    districts: ["areas", "districts"],
    cityCenter: [],
    roads: ["areas", "cityCenter", "roads"],
    houseEntrances: [],
    publicBuildings: ["areas", "publicBuildings", "publicSpaces"],
    publicSpaces: ["areas", "publicBuildings", "publicSpaces"],
    namingPanels: [],
  } as const,
} as const

// ─── DRAW TIMING CONFIGURATION ───────────────────────────────────────────────

export const DRAW_CONFIG = {
  modeSwitchSettleMs: 50,
  edgeRetryMax: 10,
  edgeRetryIntervalMs: 200,
  edgeRetryTimeoutMs: 2500,
  geomanCleanupDelayMs: 100,
  drawModeResetDelayMs: 200,
} as const

// ─── EXPORT CONFIGURATION ────────────────────────────────────────────────────

export const EXPORT_CONFIG = {
  a0Scale: 3,
  a3Scale: 2,
  jpegQuality: 0.92,
  imageTimeoutMs: 15000,
  titleBarOffsetMm: 14,
} as const

// ─── CITY CENTER CONFIGURATION ───────────────────────────────────────────────

export const CITY_CENTER_CONFIG = {
  /** Minimum allowed radius in meters */
  minRadiusM: 5,

  /** Maximum allowed radius in meters */
  maxRadiusM: 50_000,
} as const

// ─── EDIT VISUAL CONFIGURATION ───────────────────────────────────────────────

export const EDIT_CONFIG = {
  edgeLineOpacity: 0.8,
  edgeLineColor: "#3498db",
  edgeLineWidth: 3,
} as const

// ─── UI CONFIGURATION ─────────────────────────────────────────────────────────

export const UI_CONFIG = {
  /** Toast notification duration in milliseconds */
  toastDuration: 3500,

  /** Toast background colors by type */
  toastColors: {
    success: "#22c55e",
    error: "#ef4444",
    info: "#3b82f6",
    warning: "#f59e0b",
  } as const,

  /** Default text color for labels */
  defaultTextColor: "#333333",

  /** Default text color for entrance markers */
  entranceTextColor: "#000000",
} as const

// ─── GEOMETRY CONSTANTS ───────────────────────────────────────────────────────

export const GEOMETRY_CONFIG = {
  /** Earth radius in meters (WGS84 mean radius) */
  earthRadiusMeters: 6371000,

  /** Number of segments for smooth circle rendering */
  circleSegments: 64,

  /** Number of segments for circle rendering in edit mode */
  editCircleSegments: 16,
} as const

// ─── EXPORT HELPERS ───────────────────────────────────────────────────────────

/** Get API base URL */
export function getApiBaseUrl(): string {
  return API_CONFIG.baseUrl
}

/**
 * Login page path.
 * In Vite dev we use the static public login page; in production we keep
 * backend /login to preserve server-side token/nonce injection behavior.
 */
export function getLoginPath(): string {
  return isDev() ? "/login.html" : "/login"
}

/** Check if running in development mode */
export function isDev(): boolean {
  return import.meta.env?.DEV ?? false
}
