package com.nars.maplibre.utils

import com.nars.maplibre.BuildConfig

/**
 * Centralized application configuration matching the web version (config.ts)
 * 
 * All magic numbers and configuration constants should be defined here.
 */
object Config {

    // ─── API CONFIGURATION ────────────────────────────────────────────────────────

    /** Base URL for API requests (from BuildConfig) */
    val API_BASE_URL: String = BuildConfig.API_BASE_URL

    /** Default timeout for API requests in milliseconds */
    const val API_DEFAULT_TIMEOUT_MS = 10000

    /** Maximum number of retries for failed API requests */
    const val API_MAX_RETRIES = 3

    /** Base delay between retries in milliseconds */
    const val API_RETRY_BASE_DELAY_MS = 1000

    /** Maximum delay between retries in milliseconds */
    const val API_RETRY_MAX_DELAY_MS = 10000

    // ─── MAP CONFIGURATION ────────────────────────────────────────────────────────

    /** Default map center longitude (Algeria) */
    const val MAP_DEFAULT_LNG = 2.5

    /** Default map center latitude (Algeria) */
    const val MAP_DEFAULT_LAT = 28.0

    /** Default map zoom level */
    const val MAP_DEFAULT_ZOOM = 5.0

    /** Default map bearing (rotation) in degrees */
    const val MAP_DEFAULT_BEARING = 0.0

    /** Default map pitch (tilt) in degrees */
    const val MAP_DEFAULT_PITCH = 0.0

    // ─── SNAPPING CONFIGURATION ───────────────────────────────────────────────────

    /** Snap threshold in pixels */
    const val SNAP_THRESHOLD_PX = 20

    /** Phases that support snapping (polygon/polyline geometry only) */
    val SNAP_PHASES = listOf("areas", "districts", "roads", "publicBuildings", "publicSpaces")

    // ─── VALIDATION CONFIGURATION ─────────────────────────────────────────────────

    /** Minimum road length in meters */
    const val MIN_ROAD_LENGTH_METERS = 10

    // ─── TILE SERVERS ─────────────────────────────────────────────────────────────

    const val TILE_SATELLITE = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}"
    const val TILE_STREET = "https://tile.openstreetmap.org/{z}/{x}/{y}.png"
    const val TILE_LIGHT = "https://basemaps.cartocdn.com/light_all/{z}/{x}/{y}{ratio}.png"
    const val TILE_DARK = "https://basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{ratio}.png"

    const val GLYPHS = "https://fonts.openmaptiles.org/{fontstack}/{range}.pbf"

    const val ATTR_ESRI = "Source: Esri, Maxar, Earthstar Geographics, and the GIS User Community"
    const val ATTR_OSM = "© OpenStreetMap contributors"
    const val ATTR_CARTO = "© OpenStreetMap contributors, © CARTO"

    // ─── UI CONFIGURATION ─────────────────────────────────────────────────────────

    /** Toast/notification duration in milliseconds */
    const val TOAST_DURATION_MS = 3500

    // ─── FEATURE COUNTS ───────────────────────────────────────────────────────────

    /** Number of phases in the mapping pipeline */
    const val PHASE_COUNT = 8

    // ─── EXPORT HELPERS ───────────────────────────────────────────────────────────

    /** Check if running in debug mode */
    val isDebug: Boolean = BuildConfig.DEBUG

    /** Check if running in release mode */
    val isRelease: Boolean = !isDebug

    /** Check if analytics is enabled */
    val isAnalyticsEnabled: Boolean = BuildConfig.ENABLE_ANALYTICS

    /** Check if Crashlytics is enabled */
    val isCrashlyticsEnabled: Boolean = BuildConfig.ENABLE_CRASHLYTICS
}
