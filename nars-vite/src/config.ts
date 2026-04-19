// ─── APPLICATION CONFIGURATION ────────────────────────────────────────────────
// Centralized configuration constants for the NARS application.
// Environment variables and magic numbers should be defined here.

import { debugWarn } from './utils/debug'

// ─── ENVIRONMENT VARIABLE VALIDATION ─────────────────────────────────────────

/** Validate required environment variables at startup */
function validateEnvVars(): void {
    if (!import.meta.env.VITE_API_BASE && import.meta.env.PROD) {
        debugWarn('VITE_API_BASE is not set — API requests will fail in production.')
    }
}

validateEnvVars()

// ─── API CONFIGURATION ────────────────────────────────────────────────────────

export const API_CONFIG = {
    /** Base URL for API requests (from environment variable) */
    baseUrl: (import.meta.env.VITE_API_BASE as string) ?? '',

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

    /** Default map bearing (rotation) in degrees */
    defaultBearing: 0,

    /** Default map pitch (tilt) in degrees */
    defaultPitch: 0,
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
    phases: ['areas', 'districts', 'roads', 'publicBuildings', 'publicSpaces'] as const,

    /**
     * Per-phase snap targets — defines what each phase can snap TO.
     * Based on Snapping.xlsx specification.
     * Key = phase being drawn, Value = array of phases it can snap to.
     */
    snapTargets: {
        areas: ['areas'],
        districts: ['areas', 'districts'],
        cityCenter: [],
        roads: ['areas', 'cityCenter', 'roads'],
        houseEntrances: [],
        publicBuildings: ['areas', 'publicBuildings', 'publicSpaces'],
        publicSpaces: ['areas', 'publicBuildings', 'publicSpaces'],
        namingPanels: [],
    } as const,
} as const

// ─── VALIDATION CONFIGURATION ─────────────────────────────────────────────────

export const VALIDATION_CONFIG = {
    /** Minimum road length in meters */
    minRoadLengthMeters: 10,
} as const

// ─── UI CONFIGURATION ─────────────────────────────────────────────────────────

export const UI_CONFIG = {
    /** Toast notification duration in milliseconds */
    toastDuration: 3500,

    /** Default text color for labels */
    defaultTextColor: '#333333',

    /** Default text color for entrance markers */
    entranceTextColor: '#000000',
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

/** Check if running in development mode */
export function isDev(): boolean {
    return import.meta.env?.DEV ?? false
}

/** Check if running in production mode */
export function isProd(): boolean {
    return import.meta.env?.PROD ?? false
}
