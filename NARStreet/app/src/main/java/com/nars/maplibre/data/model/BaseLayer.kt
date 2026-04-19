package com.nars.maplibre.data.model

/**
 * Base layer types matching web version
 */
enum class BaseLayerType(val value: String, val displayName: String) {
    SATELLITE("satellite", "Satellite"),
    STREET("street", "Street"),
    LIGHT("light", "Light"),
    DARK("dark", "Dark")
}

/**
 * Base layer style definitions
 */
object BaseLayers {
    
    // ArcGIS World Imagery (Satellite)
    const val SATELLITE_STYLE = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}"
    
    // OpenStreetMap
    const val STREET_STYLE = "https://tile.openstreetmap.org/{z}/{x}/{y}.png"
    
    // CartoDB Light
    const val LIGHT_STYLE_URL = "https://basemaps.cartocdn.com/light_all/{z}/{x}/{y}{ratio}.png"
    
    // CartoDB Dark
    const val DARK_STYLE_URL = "https://basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{ratio}.png"
    
    // Attribution strings
    const val SATELLITE_ATTRIBUTION = "Tiles &copy; Esri &mdash; Source: Esri, i-cubed, USDA, USGS, AEX, GeoEye, Getmapping, Aerogrid, IGN, IGP, UPR-EGP, and the GIS User Community"
    const val OSM_ATTRIBUTION = """ &copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors """
    const val CARTODB_ATTRIBUTION = """ &copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/attributions">CARTO</a> """
    
    fun getAttribution(layer: BaseLayerType): String = when (layer) {
        BaseLayerType.SATELLITE -> SATELLITE_ATTRIBUTION
        BaseLayerType.STREET -> OSM_ATTRIBUTION
        BaseLayerType.LIGHT, BaseLayerType.DARK -> CARTODB_ATTRIBUTION
    }
}
