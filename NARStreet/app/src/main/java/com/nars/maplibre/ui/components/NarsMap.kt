package com.nars.maplibre.ui.components

import android.content.Context
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.viewinterop.AndroidView
import com.nars.maplibre.NarsViewModel
import com.nars.maplibre.data.model.BaseLayerType
import com.nars.maplibre.data.model.NarsFeature
import org.maplibre.android.camera.CameraPosition
import org.maplibre.android.geometry.LatLng
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.maps.MapView
import org.maplibre.android.maps.Style
import org.maplibre.android.style.layers.CircleLayer
import org.maplibre.android.style.layers.FillLayer
import org.maplibre.android.style.layers.LineLayer
import org.maplibre.android.style.layers.SymbolLayer
import org.maplibre.android.style.sources.GeoJsonSource

/**
 * MapLibre Map Composable for NARS
 * Integrates MapLibre Android SDK with Jetpack Compose
 */
@Composable
fun NarsMap(
    viewModel: NarsViewModel,
    onMapReady: (MapView, MapLibreMap) -> Unit,
    onMapClick: ((LatLng) -> Unit)? = null,
    onMapLongClick: ((LatLng) -> Unit)? = null,
    shouldHandleClick: (() -> Boolean)? = null,
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    val baseLayer = viewModel.baseLayer.collectAsState()

    // MapView state
    val mapView = remember {
        MapView(context)
    }

    // Handle lifecycle
    DisposableEffect(Unit) {
        mapView.onCreate(null)
        mapView.onStart()
        mapView.onResume()

        onDispose {
            mapView.onStop()
            mapView.onPause()
            mapView.onDestroy()
        }
    }

    // Base layer change
    LaunchedEffect(baseLayer.value) {
        updateBaseLayer(mapView, baseLayer.value)
    }

    AndroidView(
        factory = { ctx ->
            mapView.apply {
                getMapAsync { mapLibreMap ->
                    // Configure map
                    configureMap(mapLibreMap, ctx, onMapClick, onMapLongClick, shouldHandleClick)

                    // Set initial camera position (Algeria)
                    val cameraPosition = CameraPosition.Builder()
                        .target(LatLng(28.0, 2.5))
                        .zoom(5.0)
                        .bearing(0.0)
                        .tilt(0.0)
                        .build()
                    mapLibreMap.setCameraPosition(cameraPosition)

                    // Initialize base layer
                    initializeBaseLayer(mapLibreMap, baseLayer.value)

                    // Notify map ready
                    onMapReady(this, mapLibreMap)
                }
            }
        },
        modifier = modifier.fillMaxSize()
    )
}

/**
 * Configure MapLibre map
 */
private fun configureMap(
    map: MapLibreMap,
    @Suppress("UNUSED_PARAMETER") context: Context,
    onMapClick: ((LatLng) -> Unit)?,
    onMapLongClick: ((LatLng) -> Unit)?,
    shouldHandleClick: (() -> Boolean)? = null
) {
    // Set compass position
    map.uiSettings.compassGravity = android.view.Gravity.TOP or android.view.Gravity.END

    // Set attribution position
    map.uiSettings.attributionGravity = android.view.Gravity.BOTTOM or android.view.Gravity.START

    // Set logo position
    map.uiSettings.logoGravity = android.view.Gravity.BOTTOM or android.view.Gravity.START

    // Add map click listener
    onMapClick?.let { clickHandler ->
        map.addOnMapClickListener { latLng ->
            // Check if we should handle this click (for drawing/editing mode detection)
            if (shouldHandleClick?.invoke() == false) {
                // Skip click handling when in drawing/editing mode
                android.util.Log.d("NarsMap", "Skipping feature selection click - in drawing/editing mode")
                return@addOnMapClickListener false // Let Geoman handle it
            }
            android.util.Log.d("NarsMap", "Processing feature selection click")
            clickHandler(latLng)
            false // Return false to allow Geoman's listener to also process
        }
    }

    // Add map long click listener — always forward long clicks (needed for
    // finishing shapes in drawing mode). Feature-selection long clicks are
    // handled upstream in MapScreen.
    onMapLongClick?.let { longClickHandler ->
        map.addOnMapLongClickListener { latLng ->
            longClickHandler(latLng)
            false // Return false to allow Geoman's listener to also process
        }
    }
}

/**
 * Initialize base layer on map ready
 */
private fun initializeBaseLayer(map: MapLibreMap, initialLayer: BaseLayerType) {
    map.setStyle(Style.Builder().fromJson(getStyleJson(initialLayer)))
}

/**
 * Update base layer style
 */
private fun updateBaseLayer(mapView: MapView, layer: BaseLayerType) {
    mapView.getMapAsync { map ->
        map.setStyle(Style.Builder().fromJson(getStyleJson(layer)))
    }
}

/**
 * Get style JSON for layer type
 * Uses inline Mapbox Style Specification for raster tiles
 */
private fun getStyleJson(layer: BaseLayerType): String {
    return when (layer) {
        BaseLayerType.SATELLITE -> """
        {
            "version": 8,
            "sources": {
                "esri-satellite": {
                    "type": "raster",
                    "tiles": ["https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}"],
                    "tileSize": 256,
                    "attribution": "Source: Esri, Maxar, Earthstar Geographics, and the GIS User Community"
                }
            },
            "glyphs": "https://demotiles.maplibre.org/font/{fontstack}/{range}.pbf",
            "layers": [{
                "id": "satellite-layer",
                "type": "raster",
                "source": "esri-satellite",
                "minzoom": 0,
                "maxzoom": 19
            }]
        }
        """.trimIndent()
        
        BaseLayerType.STREET -> """
        {
            "version": 8,
            "sources": {
                "osm-tiles": {
                    "type": "raster",
                    "tiles": ["https://tile.openstreetmap.org/{z}/{x}/{y}.png"],
                    "tileSize": 256,
                    "attribution": "© OpenStreetMap contributors"
                }
            },
            "glyphs": "https://demotiles.maplibre.org/font/{fontstack}/{range}.pbf",
            "layers": [{
                "id": "osm-layer",
                "type": "raster",
                "source": "osm-tiles",
                "minzoom": 0,
                "maxzoom": 19
            }]
        }
        """.trimIndent()
        
        BaseLayerType.LIGHT -> """
        {
            "version": 8,
            "sources": {
                "carto-light": {
                    "type": "raster",
                    "tiles": ["https://basemaps.cartocdn.com/light_all/{z}/{x}/{y}.png"],
                    "tileSize": 256,
                    "attribution": "© OpenStreetMap contributors, © CARTO"
                }
            },
            "glyphs": "https://demotiles.maplibre.org/font/{fontstack}/{range}.pbf",
            "layers": [{
                "id": "carto-light-layer",
                "type": "raster",
                "source": "carto-light",
                "minzoom": 0,
                "maxzoom": 19
            }]
        }
        """.trimIndent()
        
        BaseLayerType.DARK -> """
        {
            "version": 8,
            "sources": {
                "carto-dark": {
                    "type": "raster",
                    "tiles": ["https://basemaps.cartocdn.com/dark_all/{z}/{x}/{y}.png"],
                    "tileSize": 256,
                    "attribution": "© OpenStreetMap contributors, © CARTO"
                }
            },
            "glyphs": "https://demotiles.maplibre.org/font/{fontstack}/{range}.pbf",
            "layers": [{
                "id": "carto-dark-layer",
                "type": "raster",
                "source": "carto-dark",
                "minzoom": 0,
                "maxzoom": 19
            }]
        }
        """.trimIndent()
    }
}

/**
 * Animate camera to location
 */
fun animateCameraTo(mapView: MapView, latLng: LatLng, zoom: Double = 15.0, duration: Int = 1000) {
    mapView.getMapAsync { map ->
        val cameraPosition = CameraPosition.Builder()
            .target(latLng)
            .zoom(zoom)
            .build()
        map.animateCamera(
            org.maplibre.android.camera.CameraUpdateFactory.newCameraPosition(cameraPosition),
            duration
        )
    }
}

/**
 * Fly camera to location
 */
fun flyCameraTo(mapView: MapView, latLng: LatLng, zoom: Double = 15.0, duration: Int = 2000) {
    mapView.getMapAsync { map ->
        val cameraPosition = CameraPosition.Builder()
            .target(latLng)
            .zoom(zoom)
            .build()
        map.easeCamera(
            org.maplibre.android.camera.CameraUpdateFactory.newCameraPosition(cameraPosition),
            duration
        )
    }
}
