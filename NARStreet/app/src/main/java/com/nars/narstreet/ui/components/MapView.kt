package com.nars.narstreet.ui.components

import android.graphics.PointF
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.viewinterop.AndroidView
import com.nars.narstreet.data.model.EntranceEntity
import org.maplibre.android.MapLibre
import org.maplibre.android.camera.CameraPosition
import org.maplibre.android.geometry.LatLng
import org.maplibre.android.maps.MapView
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.maps.Style
import org.maplibre.android.annotations.MarkerOptions
import org.maplibre.android.annotations.PolygonOptions

private const val OSM_STYLE = "https://demotiles.maplibre.org/style.json"

/** Simple OSM map for Phase 05 — shows GPS position marker. */
@Composable
fun EntranceMapView(
    currentLat: Double,
    currentLng: Double,
    entrances: List<EntranceEntity>,
    modifier: Modifier = Modifier,
) {
    var mapLibreMap by remember { mutableStateOf<MapLibreMap?>(null) }

    AndroidView(
        modifier = modifier,
        factory  = { context ->
            MapLibre.getInstance(context)
            MapView(context).apply {
                onCreate(null)
                getMapAsync { map ->
                    mapLibreMap = map
                    map.setStyle(OSM_STYLE)
                    map.cameraPosition = CameraPosition.Builder()
                        .target(LatLng(currentLat, currentLng))
                        .zoom(17.0)
                        .build()
                }
            }
        },
        update = { mapView ->
            mapLibreMap?.let { map ->
                map.clear()
                // Current GPS position
                map.addMarker(
                    MarkerOptions()
                        .position(LatLng(currentLat, currentLng))
                        .title("You are here"),
                )
                // Existing entrances
                entrances.forEach { e ->
                    map.addMarker(
                        MarkerOptions()
                            .position(LatLng(e.lat, e.lng))
                            .title(e.label),
                    )
                }
            }
        },
    )
}

/** Polygon vertex-drag editor for Phases 06 & 07. */
@Composable
fun PolygonEditorMapView(
    vertices: List<LatLng>,
    onVerticesMoved: (List<LatLng>) -> Unit,
    modifier: Modifier = Modifier,
) {
    var mapLibreMap by remember { mutableStateOf<MapLibreMap?>(null) }

    AndroidView(
        modifier = modifier,
        factory  = { context ->
            MapLibre.getInstance(context)
            MapView(context).apply {
                onCreate(null)
                getMapAsync { map ->
                    mapLibreMap = map
                    map.setStyle(OSM_STYLE) {
                        if (vertices.isNotEmpty()) {
                            val center = LatLng(
                                vertices.map { it.latitude }.average(),
                                vertices.map { it.longitude }.average(),
                            )
                            map.cameraPosition = CameraPosition.Builder()
                                .target(center)
                                .zoom(18.0)
                                .build()
                        }
                    }
                }
            }
        },
        update = { _ ->
            mapLibreMap?.let { map ->
                map.clear()
                if (vertices.size >= 3) {
                    map.addPolygon(
                        PolygonOptions()
                            .addAll(vertices)
                            .fillColor(0x331D9E75)
                            .strokeColor(0xFF1D9E75.toInt()),
                    )
                }
                vertices.forEach { v ->
                    map.addMarker(MarkerOptions().position(v))
                }
                // Vertex drag: map long-press moves nearest vertex
                map.addOnMapLongClickListener { tapped ->
                    val nearest = vertices.minByOrNull { v ->
                        Math.hypot(v.latitude - tapped.latitude, v.longitude - tapped.longitude)
                    }
                    if (nearest != null) {
                        val updated = vertices.map { if (it == nearest) tapped else it }
                        onVerticesMoved(updated)
                    }
                    true
                }
            }
        },
    )
}
