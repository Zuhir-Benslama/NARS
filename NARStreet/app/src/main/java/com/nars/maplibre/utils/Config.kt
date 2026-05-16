package com.nars.maplibre.utils

import com.nars.maplibre.BuildConfig

object Config {

    val API_BASE_URL: String = BuildConfig.API_BASE_URL

    const val API_DEFAULT_TIMEOUT_MS = 10000

    const val API_MAX_RETRIES = 3

    const val API_RETRY_BASE_DELAY_MS = 1000

    const val API_RETRY_MAX_DELAY_MS = 10000

    const val MAP_DEFAULT_LNG = 2.5

    const val MAP_DEFAULT_LAT = 28.0

    const val MAP_DEFAULT_ZOOM = 5.0

    const val MAP_DEFAULT_BEARING = 0.0

    const val MAP_DEFAULT_PITCH = 0.0

    const val SNAP_THRESHOLD_PX = 20

    const val MIN_ROAD_LENGTH_METERS = 10

    const val TILE_SATELLITE = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}"
    const val TILE_STREET = "https://tile.openstreetmap.org/{z}/{x}/{y}.png"
    const val TILE_LIGHT = "https://basemaps.cartocdn.com/light_all/{z}/{x}/{y}{ratio}.png"
    const val TILE_DARK = "https://basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{ratio}.png"

    const val GLYPHS = "https://fonts.openmaptiles.org/{fontstack}/{range}.pbf"

    const val ATTR_ESRI = "Source: Esri, Maxar, Earthstar Geographics, and the GIS User Community"
    const val ATTR_OSM = "© OpenStreetMap contributors"
    const val ATTR_CARTO = "© OpenStreetMap contributors, © CARTO"

    const val TOAST_DURATION_MS = 3500

    val isDebug: Boolean = BuildConfig.DEBUG

    val isRelease: Boolean = !isDebug
}
