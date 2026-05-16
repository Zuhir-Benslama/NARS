package com.nars.maplibre.data.model

enum class BaseLayerType(val value: String, val displayName: String) {
    SATELLITE("satellite", "Satellite"),
    STREET("street", "Street"),
    LIGHT("light", "Light"),
    DARK("dark", "Dark")
}
