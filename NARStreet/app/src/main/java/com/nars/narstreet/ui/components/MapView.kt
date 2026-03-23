package com.nars.narstreet.ui.components

import android.graphics.PointF
import android.graphics.RectF
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.viewinterop.AndroidView
import com.nars.narstreet.data.model.EntranceEntity
import com.squareup.moshi.JsonReader
import okio.Buffer
import org.maplibre.android.MapLibre
import org.maplibre.android.camera.CameraPosition
import org.maplibre.android.geometry.LatLng
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.maps.MapView
import org.maplibre.android.maps.Style
import org.maplibre.android.style.expressions.Expression
import org.maplibre.android.style.layers.CircleLayer
import org.maplibre.android.style.layers.LineLayer
import org.maplibre.android.style.layers.PropertyFactory.*
import org.maplibre.android.style.layers.RasterLayer
import org.maplibre.android.style.layers.SymbolLayer
import org.maplibre.android.style.sources.GeoJsonSource
import org.maplibre.android.style.sources.RasterSource
import org.maplibre.android.style.sources.TileSet
import org.maplibre.geojson.Feature
import org.maplibre.geojson.FeatureCollection
import org.maplibre.geojson.Point
import org.maplibre.geojson.Polygon

// ─────────────────────────────────────────────────────────────────────────────
// Colors
// ─────────────────────────────────────────────────────────────────────────────

private object C {
    const val ROAD           = "#3498DB"
    const val ROAD_HIGHLIGHT = "#5DADE2"
    const val ENTRANCE       = "#27AE60"
    const val GPS            = "#E74C3C"
    const val BUILDING       = "#E67E22"
    const val SPACE          = "#2ECC71"
    const val PANEL          = "#9B59B6"
    const val AREA_MAIN      = "#C0392B"
    const val AREA_SEC       = "#8E44AD"
    const val DISTRICT       = "#F39C12"
    const val BOUNDARY       = "#E74C3C"
    const val CITY_CENTER    = "#E74C3C"
}

private const val TAP_PX = 40f   // generous tap radius for thin line layers

// ─────────────────────────────────────────────────────────────────────────────
// Data classes
// ─────────────────────────────────────────────────────────────────────────────

data class CommuneMapContext(
    val boundaryGeoJson: String?,
    val mainAreaGeoJson: String?,
    val secondaryAreasGeoJson: List<String> = emptyList(),
)

data class MapLayersState(
    val communeContext: CommuneMapContext?    = null,
    val communeCenter: LatLng                = LatLng(36.7, 3.05),
    val areaPolygons: List<List<LatLng>>     = emptyList(),
    val areaLabels: List<String>             = emptyList(),
    val roadPolylines: List<List<LatLng>>    = emptyList(),
    val roadLabels: List<String>             = emptyList(),
    val roadDbIds: List<Long>                = emptyList(),
    val buildingPoints: List<LatLng>         = emptyList(),
    val buildingLabels: List<String>          = emptyList(),
    val buildingDbIds: List<Long>             = emptyList(),
    val spacePolygons: List<List<LatLng>>    = emptyList(),
    val spaceLabels: List<String>            = emptyList(),
    val districtPolygons: List<List<LatLng>> = emptyList(),
    val districtLabels: List<String>         = emptyList(),
    val panelPoints: List<LatLng>            = emptyList(),
    val panelLabels: List<String>            = emptyList(),
    val cityCenterPoint: LatLng?             = null,
    val highlightPolyline: List<LatLng>?     = null,
)

// ─────────────────────────────────────────────────────────────────────────────
// ESRI tiles — no setMaxZoom on TileSet to avoid compile issues; use camera max
// ─────────────────────────────────────────────────────────────────────────────

private const val ESRI =
    "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}"

private fun Style.Builder.withEsri(): Style.Builder =
    withSource(RasterSource("esri-src", TileSet("2.2.0", ESRI), 256))
        .withLayer(RasterLayer("esri-lyr", "esri-src"))

private fun MapLibreMap.applyDefaults(center: LatLng, zoom: Double) {
    setMaxZoomPreference(19.0)   // stop before "data not available" blank tiles
    cameraPosition = CameraPosition.Builder().target(center).zoom(zoom).build()
}

// ─────────────────────────────────────────────────────────────────────────────
// Coordinate parser — handles JSON {"lat":x} AND Kotlin Map.toString() {lat=x}
// ─────────────────────────────────────────────────────────────────────────────

internal fun parseCoordinatesJson(json: String): List<LatLng> {
    if (json.isBlank() || json == "[]") return emptyList()
    return try {
        val reader = JsonReader.of(Buffer().writeUtf8(json))
        reader.isLenient = true
        val result = mutableListOf<LatLng>()
        reader.beginArray()
        while (reader.hasNext()) {
            var lat = 0.0
            var lng = 0.0
            reader.beginObject()
            while (reader.hasNext()) {
                when (reader.nextName()) {
                    "lat" -> lat = reader.nextDouble()
                    "lng" -> lng = reader.nextDouble()
                    else  -> reader.skipValue()
                }
            }
            reader.endObject()
            if (lat != 0.0 || lng != 0.0) result.add(LatLng(lat, lng))
        }
        reader.endArray()
        result
    } catch (_: Exception) { emptyList() }
}

private fun List<LatLng>.toPoints() = map { Point.fromLngLat(it.longitude, it.latitude) }

// ─────────────────────────────────────────────────────────────────────────────
// Gesture lock + tap rect
// ─────────────────────────────────────────────────────────────────────────────

private fun MapLibreMap.setLocked(locked: Boolean) {
    uiSettings.isScrollGesturesEnabled = !locked
    uiSettings.isZoomGesturesEnabled   = !locked
    uiSettings.isRotateGesturesEnabled = !locked
    uiSettings.isTiltGesturesEnabled   = !locked
}

private fun PointF.toTapRect() = RectF(x - TAP_PX, y - TAP_PX, x + TAP_PX, y + TAP_PX)

// ─────────────────────────────────────────────────────────────────────────────
// Feature collection builders
// ─────────────────────────────────────────────────────────────────────────────

private fun lineFC(
    lines: List<List<LatLng>>,
    ids: List<Long> = emptyList(),
    labels: List<String> = emptyList(),
): FeatureCollection = FeatureCollection.fromFeatures(
    lines.filter { it.size >= 2 }.mapIndexed { i, pts ->
        Feature.fromGeometry(org.maplibre.geojson.LineString.fromLngLats(pts.toPoints())).also { f ->
            if (i < ids.size)    f.addNumberProperty("dbId",  ids[i])
            if (i < labels.size && labels[i].isNotBlank()) f.addStringProperty("label", labels[i])
        }
    }
)

private fun polygonFC(
    polys: List<List<LatLng>>,
    ids: List<Long> = emptyList(),
    labels: List<String> = emptyList(),
): FeatureCollection = FeatureCollection.fromFeatures(
    polys.filter { it.size >= 3 }.mapIndexed { i, pts ->
        val ring = pts.toPoints() + Point.fromLngLat(pts.first().longitude, pts.first().latitude)
        Feature.fromGeometry(Polygon.fromLngLats(listOf(ring))).also { f ->
            if (i < ids.size)    f.addNumberProperty("dbId",  ids[i])
            if (i < labels.size && labels[i].isNotBlank()) f.addStringProperty("label", labels[i])
        }
    }
)

private fun pointFC(
    pts: List<LatLng>,
    ids: List<Long> = emptyList(),
    labels: List<String> = emptyList(),
): FeatureCollection = FeatureCollection.fromFeatures(
    pts.mapIndexed { i, pt ->
        Feature.fromGeometry(Point.fromLngLat(pt.longitude, pt.latitude)).also { f ->
            if (i < ids.size)    f.addNumberProperty("dbId",  ids[i])
            if (i < labels.size && labels[i].isNotBlank()) f.addStringProperty("label", labels[i])
        }
    }
)

private fun buildPolygonFC(vertices: List<LatLng>, label: String): FeatureCollection {
    if (vertices.size < 3) return FeatureCollection.fromFeatures(emptyList())
    val ring = vertices.toPoints() + Point.fromLngLat(vertices.first().longitude, vertices.first().latitude)
    val f = Feature.fromGeometry(Polygon.fromLngLats(listOf(ring)))
    if (label.isNotBlank()) f.addStringProperty("label", label)
    return FeatureCollection.fromFeature(f)
}

// ─────────────────────────────────────────────────────────────────────────────
// Label layer (no explicit font to avoid sprite crash)
// ─────────────────────────────────────────────────────────────────────────────

private fun Style.addLabelLayer(id: String, src: String, placement: String = "point", color: String = "#FFFFFF") {
    addLayer(SymbolLayer(id, src).apply {
        setProperties(
            textField(Expression.get("label")),
            textSize(11f),
            textColor(color),
            textHaloColor("#000000"),
            textHaloWidth(1.5f),
            symbolPlacement(placement),
            textMaxAngle(30f),
            textPadding(4f),
            textAllowOverlap(false),
        )
    })
}

// ─────────────────────────────────────────────────────────────────────────────
// Commune context (boundary outlines — no fills)
// ─────────────────────────────────────────────────────────────────────────────

private fun Style.addCommuneContext(ctx: CommuneMapContext?) {
    if (ctx == null) return
    val secFeats = ctx.secondaryAreasGeoJson.mapNotNull { gj ->
        runCatching { Feature.fromJson("""{"type":"Feature","geometry":$gj,"properties":{}}""") }.getOrNull()
    }
    addSource(GeoJsonSource("ctx-sec-src", FeatureCollection.fromFeatures(secFeats)))
    addLayer(LineLayer("ctx-sec-line", "ctx-sec-src").apply { setProperties(lineColor(C.AREA_SEC), lineWidth(2f)) })

    ctx.mainAreaGeoJson?.let { gj ->
        runCatching { Feature.fromJson("""{"type":"Feature","geometry":$gj,"properties":{}}""") }.getOrNull()
    }?.let { feat ->
        addSource(GeoJsonSource("ctx-main-src", feat))
        addLayer(LineLayer("ctx-main-line", "ctx-main-src").apply { setProperties(lineColor(C.AREA_MAIN), lineWidth(2f)) })
    }

    ctx.boundaryGeoJson?.let { gj ->
        runCatching { Feature.fromJson("""{"type":"Feature","geometry":$gj,"properties":{}}""") }.getOrNull()
    }?.let { feat ->
        addSource(GeoJsonSource("ctx-boundary-src", feat))
        addLayer(LineLayer("ctx-boundary-line", "ctx-boundary-src").apply { setProperties(lineColor(C.BOUNDARY), lineWidth(2.5f)) })
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Shared layer adders
// ─────────────────────────────────────────────────────────────────────────────

private fun Style.addAreasLayer(fc: FeatureCollection) {
    addSource(GeoJsonSource("area-src", fc))
    addLayer(LineLayer("area-line", "area-src").apply { setProperties(lineColor(C.AREA_SEC), lineWidth(1.5f)) })
    addLabelLayer("area-label", "area-src", color = C.AREA_SEC)
}

private fun Style.addRoadsLayer(fc: FeatureCollection, labeled: Boolean = true) {
    addSource(GeoJsonSource("roads-src", fc))
    addLayer(LineLayer("roads-line", "roads-src").apply { setProperties(lineColor(C.ROAD), lineWidth(2.5f)) })
    if (labeled) addLabelLayer("roads-label", "roads-src", placement = "line", color = C.ROAD)
}

private fun Style.addBuildingsLayer(pts: List<LatLng>, ids: List<Long> = emptyList(), labels: List<String> = emptyList()) {
    addSource(GeoJsonSource("bld-src", pointFC(pts, ids, labels)))
    addLayer(CircleLayer("bld-circle", "bld-src").apply {
        setProperties(circleRadius(8f), circleColor(C.BUILDING), circleStrokeWidth(1.5f), circleStrokeColor("#FFFFFF"))
    })
    addLabelLayer("bld-label", "bld-src", color = C.BUILDING)
}

private fun Style.addSpacesLayer(fc: FeatureCollection) {
    addSource(GeoJsonSource("spc-src", fc))
    addLayer(LineLayer("spc-line", "spc-src").apply { setProperties(lineColor(C.SPACE), lineWidth(1.5f)) })
    addLabelLayer("spc-label", "spc-src", color = C.SPACE)
}

private fun Style.addDistrictsLayer(fc: FeatureCollection) {
    addSource(GeoJsonSource("dst-src", fc))
    addLayer(LineLayer("dst-line", "dst-src").apply { setProperties(lineColor(C.DISTRICT), lineWidth(1.5f)) })
    addLabelLayer("dst-label", "dst-src", color = C.DISTRICT)
}

private fun Style.addCityCenterLayer(point: LatLng) {
    val feat = Feature.fromGeometry(Point.fromLngLat(point.longitude, point.latitude))
    feat.addStringProperty("label", "City Center")
    addSource(GeoJsonSource("cc-src", feat))
    addLayer(CircleLayer("cc-circle", "cc-src").apply {
        setProperties(circleRadius(10f), circleColor(C.CITY_CENTER), circleStrokeWidth(2f), circleStrokeColor("#FFFFFF"))
    })
    addLabelLayer("cc-label", "cc-src", color = C.CITY_CENTER)
}

// ─────────────────────────────────────────────────────────────────────────────
// Hit-test helper — queries multiple layer IDs, returns first match's dbId
// ─────────────────────────────────────────────────────────────────────────────

private fun MapLibreMap.hitTest(sc: PointF, vararg layerIds: String): Long? {
    val rect = sc.toTapRect()
    for (layerId in layerIds) {
        val feats = queryRenderedFeatures(rect, layerId)
        val id = feats.firstOrNull()?.getNumberProperty("dbId")?.toLong()
        if (id != null) return id
    }
    return null
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 04 — Road Map
// KEY FIX: latestLayers ref read inside setStyle ensures no race condition
// ─────────────────────────────────────────────────────────────────────────────

@Composable
fun RoadMapView(
    layers: MapLayersState,
    isLocked: Boolean                  = false,
    onFeatureClick: (Long) -> Unit     = {},
    onFeatureLongClick: (Long) -> Unit = {},
    modifier: Modifier                 = Modifier,
) {
    val latestLayers     = rememberUpdatedState(layers)
    val latestClick      = rememberUpdatedState(onFeatureClick)
    val latestLongClick  = rememberUpdatedState(onFeatureLongClick)
    var mapRef           by remember { mutableStateOf<MapLibreMap?>(null) }

    val center = remember(layers.communeCenter) { layers.communeCenter }

    AndroidView(modifier = modifier,
        factory = { ctx ->
            MapLibre.getInstance(ctx)
            MapView(ctx).apply {
                onCreate(null)
                getMapAsync { m ->
                    mapRef = m
                    m.setStyle(Style.Builder().withEsri()) { style ->
                        // Use latestLayers.value — guaranteed to be the most recent state
                        val L = latestLayers.value
                        val roadCenter = L.roadPolylines.flatten().takeIf { it.isNotEmpty() }
                            ?.let { LatLng(it.map { p -> p.latitude }.average(), it.map { p -> p.longitude }.average()) }
                            ?: L.communeCenter
                        m.applyDefaults(roadCenter, 14.0)

                        style.addCommuneContext(L.communeContext)
                        // Areas and city center shown as context (per spreadsheet: Phase04)
                        if (L.areaPolygons.isNotEmpty())
                            style.addAreasLayer(polygonFC(L.areaPolygons, labels = L.areaLabels))
                        L.cityCenterPoint?.let { style.addCityCenterLayer(it) }
                        style.addRoadsLayer(lineFC(L.roadPolylines, L.roadDbIds, L.roadLabels))
                        style.addSource(GeoJsonSource("hl-src", lineFC(listOfNotNull(L.highlightPolyline))))
                        style.addLayer(LineLayer("hl-line", "hl-src").apply {
                            setProperties(lineColor(C.ROAD_HIGHLIGHT), lineWidth(5f))
                        })

                        m.addOnMapClickListener { ll ->
                            val id = m.hitTest(m.projection.toScreenLocation(ll), "roads-line")
                            if (id != null) latestClick.value(id)
                            id != null
                        }
                        m.addOnMapLongClickListener { ll ->
                            val id = m.hitTest(m.projection.toScreenLocation(ll), "roads-line")
                            if (id != null) latestLongClick.value(id)
                            true
                        }
                    }
                }
            }
        },
        update = { _ ->
            val L = latestLayers.value
            mapRef?.setLocked(isLocked)
            mapRef?.getStyle { style ->
                (style.getSource("roads-src") as? GeoJsonSource)
                    ?.setGeoJson(lineFC(L.roadPolylines, L.roadDbIds, L.roadLabels))
                (style.getSource("hl-src") as? GeoJsonSource)
                    ?.setGeoJson(lineFC(listOfNotNull(L.highlightPolyline)))
                // Refresh context layers added in factory
                (style.getSource("area-src") as? GeoJsonSource)
                    ?.setGeoJson(polygonFC(L.areaPolygons, labels = L.areaLabels))
                (style.getSource("cc-src") as? GeoJsonSource)?.let { src ->
                    L.cityCenterPoint?.let { pt ->
                        val f = Feature.fromGeometry(Point.fromLngLat(pt.longitude, pt.latitude))
                        f.addStringProperty("label", "City Center")
                        src.setGeoJson(f)
                    }
                }

                // Move camera: priority = highlight > road centre > commune centre
                val target = L.highlightPolyline?.takeIf { it.isNotEmpty() }
                    ?.let { pts -> LatLng(pts.map { it.latitude }.average(), pts.map { it.longitude }.average()) }
                    ?: L.roadPolylines.flatten().takeIf { it.isNotEmpty() }
                        ?.let { pts -> LatLng(pts.map { it.latitude }.average(), pts.map { it.longitude }.average()) }
                if (target != null) {
                    val zoom = if (L.highlightPolyline != null) 16.0 else 14.0
                    mapRef?.cameraPosition = CameraPosition.Builder().target(target).zoom(zoom).build()
                }
            }
        })
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 05 — Entrance Map
// ─────────────────────────────────────────────────────────────────────────────

@Composable
fun EntranceMapView(
    currentLat: Double,
    currentLng: Double,
    entrances: List<EntranceEntity>,
    modifier: Modifier                 = Modifier,
    layers: MapLayersState             = MapLayersState(),
    isLocked: Boolean                  = false,
    onFeatureClick: (Long) -> Unit     = {},
    onFeatureLongClick: (Long) -> Unit = {},
) {
    val latestLayers    = rememberUpdatedState(layers)
    val latestEntrances = rememberUpdatedState(entrances)
    val latestClick     = rememberUpdatedState(onFeatureClick)
    val latestLongClick = rememberUpdatedState(onFeatureLongClick)
    var mapRef          by remember { mutableStateOf<MapLibreMap?>(null) }

    AndroidView(modifier = modifier,
        factory = { ctx ->
            MapLibre.getInstance(ctx)
            MapView(ctx).apply {
                onCreate(null)
                getMapAsync { m ->
                    mapRef = m
                    m.setStyle(Style.Builder().withEsri()) { style ->
                        val L = latestLayers.value
                        val E = latestEntrances.value
                        m.applyDefaults(L.communeCenter, 15.0)

                        style.addCommuneContext(L.communeContext)
                        L.cityCenterPoint?.let { style.addCityCenterLayer(it) }
                        style.addRoadsLayer(lineFC(L.roadPolylines, L.roadDbIds), labeled = false)

                        style.addSource(GeoJsonSource("entr-src",
                            pointFC(E.map { LatLng(it.lat, it.lng) }, E.map { it.id }, E.map { it.label })))
                        style.addLayer(CircleLayer("entr-circle", "entr-src").apply {
                            setProperties(circleRadius(8f), circleColor(C.ENTRANCE),
                                circleStrokeWidth(1.5f), circleStrokeColor("#FFFFFF"))
                        })
                        style.addLabelLayer("entr-label", "entr-src", color = C.ENTRANCE)

                        style.addSource(GeoJsonSource("gps-src",
                            Feature.fromGeometry(Point.fromLngLat(currentLng, currentLat))))
                        style.addLayer(CircleLayer("gps-circle", "gps-src").apply {
                            setProperties(circleRadius(12f), circleColor(C.GPS),
                                circleStrokeWidth(2f), circleStrokeColor("#FFFFFF"))
                        })

                        m.addOnMapClickListener { ll ->
                            val id = m.hitTest(m.projection.toScreenLocation(ll), "entr-circle")
                            if (id != null) latestClick.value(id)
                            id != null
                        }
                        m.addOnMapLongClickListener { ll ->
                            val id = m.hitTest(m.projection.toScreenLocation(ll), "entr-circle")
                            if (id != null) latestLongClick.value(id)
                            true
                        }
                    }
                }
            }
        },
        update = { _ ->
            val E = latestEntrances.value
            val L = latestLayers.value
            mapRef?.setLocked(isLocked)
            mapRef?.getStyle { style ->
                (style.getSource("gps-src") as? GeoJsonSource)
                    ?.setGeoJson(Feature.fromGeometry(Point.fromLngLat(currentLng, currentLat)))
                (style.getSource("entr-src") as? GeoJsonSource)
                    ?.setGeoJson(pointFC(E.map { LatLng(it.lat, it.lng) }, E.map { it.id }, E.map { it.label }))
                // Refresh context layers (may arrive after style loaded)
                (style.getSource("roads-src") as? GeoJsonSource)
                    ?.setGeoJson(lineFC(L.roadPolylines, L.roadDbIds))
                (style.getSource("cc-src") as? GeoJsonSource)?.let { src ->
                    L.cityCenterPoint?.let { pt ->
                        val f = Feature.fromGeometry(Point.fromLngLat(pt.longitude, pt.latitude))
                        f.addStringProperty("label", "City Center")
                        src.setGeoJson(f)
                    }
                }
                (style.getSource("area-src") as? GeoJsonSource)
                    ?.setGeoJson(polygonFC(L.areaPolygons, labels = L.areaLabels))
            }
        })
}

// ─────────────────────────────────────────────────────────────────────────────
// Polygon Editor
// ─────────────────────────────────────────────────────────────────────────────

@Composable
fun PolygonEditorMapView(
    vertices: List<LatLng>,
    onVerticesMoved: (List<LatLng>) -> Unit,
    modifier: Modifier                 = Modifier,
    communeContext: CommuneMapContext?  = null,
    communeCenter: LatLng              = LatLng(36.7, 3.05),
    featureColor: String               = C.BUILDING,
    featureLabel: String               = "",
    isLocked: Boolean                  = false,
) {
    val latestVerts  = rememberUpdatedState(vertices)
    val latestMoved  = rememberUpdatedState(onVerticesMoved)
    var mapRef       by remember { mutableStateOf<MapLibreMap?>(null) }

    AndroidView(modifier = modifier,
        factory = { ctx ->
            MapLibre.getInstance(ctx)
            MapView(ctx).apply {
                onCreate(null)
                getMapAsync { m ->
                    mapRef = m
                    m.setStyle(Style.Builder().withEsri()) { style ->
                        val V = latestVerts.value
                        val ctr = V.takeIf { it.isNotEmpty() }
                            ?.let { LatLng(it.map { v -> v.latitude }.average(), it.map { v -> v.longitude }.average()) }
                            ?: communeCenter
                        m.applyDefaults(ctr, 16.0)
                        style.addCommuneContext(communeContext)

                        style.addSource(GeoJsonSource("poly-src", buildPolygonFC(V, featureLabel)))
                        style.addSource(GeoJsonSource("vert-src", pointFC(V)))
                        style.addLayer(LineLayer("poly-line", "poly-src").apply {
                            setProperties(lineColor(featureColor), lineWidth(2.5f))
                        })
                        style.addLayer(CircleLayer("vert-circle", "vert-src").apply {
                            setProperties(circleRadius(8f), circleColor(featureColor),
                                circleStrokeWidth(2f), circleStrokeColor("#FFFFFF"))
                        })
                        if (featureLabel.isNotBlank()) style.addLabelLayer("poly-label", "poly-src", color = featureColor)

                        m.addOnMapLongClickListener { tapped ->
                            val cur = latestVerts.value
                            val nearest = cur.minByOrNull { v ->
                                Math.hypot(v.latitude - tapped.latitude, v.longitude - tapped.longitude)
                            }
                            if (nearest != null) latestMoved.value(cur.map { if (it == nearest) tapped else it })
                            true
                        }
                    }
                }
            }
        },
        update = { _ ->
            mapRef?.setLocked(isLocked)
            mapRef?.getStyle { style ->
                (style.getSource("poly-src") as? GeoJsonSource)?.setGeoJson(buildPolygonFC(vertices, featureLabel))
                (style.getSource("vert-src") as? GeoJsonSource)?.setGeoJson(pointFC(vertices))
            }
        })
}

// ─────────────────────────────────────────────────────────────────────────────
// Multi-polygon read-only map
// ─────────────────────────────────────────────────────────────────────────────

@Composable
fun MultiPolygonMapView(
    polygons: List<List<LatLng>>,
    labels: List<String>               = emptyList(),
    dbIds: List<Long>                  = emptyList(),
    featureColor: String               = C.AREA_SEC,
    communeContext: CommuneMapContext?  = null,
    communeCenter: LatLng              = LatLng(36.7, 3.05),
    // Context layers
    roadPolylines: List<List<LatLng>>  = emptyList(),
    cityCenterPoint: LatLng?           = null,
    areaPolygons: List<List<LatLng>>   = emptyList(),
    areaLabels: List<String>           = emptyList(),
    // Optional full layers state — used when phase needs to render building/space points
    layers: MapLayersState?            = null,
    isLocked: Boolean                  = false,
    onFeatureClick: (Long) -> Unit     = {},
    onFeatureLongClick: (Long) -> Unit = {},
    modifier: Modifier                 = Modifier,
) {
    val latestPolygons  = rememberUpdatedState(polygons)
    val latestLabels    = rememberUpdatedState(labels)
    val latestIds       = rememberUpdatedState(dbIds)
    val latestClick     = rememberUpdatedState(onFeatureClick)
    val latestLongClick = rememberUpdatedState(onFeatureLongClick)
    var mapRef          by remember { mutableStateOf<MapLibreMap?>(null) }

    AndroidView(modifier = modifier,
        factory = { ctx ->
            MapLibre.getInstance(ctx)
            MapView(ctx).apply {
                onCreate(null)
                getMapAsync { m ->
                    mapRef = m
                    m.setStyle(Style.Builder().withEsri()) { style ->
                        val P = latestPolygons.value
                        val L = latestLabels.value
                        val I = latestIds.value
                        val ctr = P.flatten().takeIf { it.isNotEmpty() }
                            ?.let { pts -> LatLng(pts.map { it.latitude }.average(), pts.map { it.longitude }.average()) }
                            ?: communeCenter
                        m.applyDefaults(ctr, 13.0)
                        style.addCommuneContext(communeContext)
                        // Context layers injected via extra params (roads, city center)
                        if (roadPolylines.isNotEmpty())
                            style.addRoadsLayer(lineFC(roadPolylines), labeled = false)
                        cityCenterPoint?.let { style.addCityCenterLayer(it) }
                        if (areaPolygons.isNotEmpty())
                            style.addAreasLayer(polygonFC(areaPolygons, labels = areaLabels))

                        style.addSource(GeoJsonSource("mp-src", polygonFC(P, I, L)))
                        style.addLayer(LineLayer("mp-line", "mp-src").apply {
                            setProperties(lineColor(featureColor), lineWidth(2f))
                        })
                        style.addLabelLayer("mp-label", "mp-src", color = featureColor)
                        // Building/space points from layers (phases 06/07)
                        val bldPts = layers?.buildingPoints ?: emptyList()
                        if (bldPts.isNotEmpty()) style.addBuildingsLayer(
                            bldPts,
                            layers?.buildingDbIds ?: emptyList(),
                            layers?.buildingLabels ?: emptyList(),
                        )

                        m.addOnMapClickListener { ll ->
                            val id = m.hitTest(m.projection.toScreenLocation(ll), "mp-line")
                            if (id != null) latestClick.value(id)
                            id != null
                        }
                        m.addOnMapLongClickListener { ll ->
                            val id = m.hitTest(m.projection.toScreenLocation(ll), "mp-line")
                            if (id != null) latestLongClick.value(id)
                            true
                        }
                    }
                }
            }
        },
        update = { _ ->
            mapRef?.setLocked(isLocked)
            mapRef?.getStyle { style ->
                (style.getSource("mp-src") as? GeoJsonSource)
                    ?.setGeoJson(polygonFC(polygons, dbIds, labels))
                (style.getSource("roads-src") as? GeoJsonSource)
                    ?.setGeoJson(lineFC(roadPolylines))
                (style.getSource("area-src") as? GeoJsonSource)
                    ?.setGeoJson(polygonFC(areaPolygons, labels = areaLabels))
                // Building points (Phase06)
                val bldPts = layers?.buildingPoints ?: emptyList()
                (style.getSource("bld-src") as? GeoJsonSource)
                    ?.setGeoJson(pointFC(bldPts,
                        layers?.buildingDbIds ?: emptyList(),
                        layers?.buildingLabels ?: emptyList()))
            }
        })
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 08 — Panel Map
//
// Two modes:
//   Detail view: panelLat/Lng != 0.0  → renders single focused panel
//   List view:   panelLat/Lng == 0.0  → centres on layers.panelPoints centroid,
//                                        renders all panels as a point cluster.
// onFeatureClick fires with the panel's dbId when a circle is tapped.
// ─────────────────────────────────────────────────────────────────────────────

@Composable
fun PanelMapView(
    panelLat: Double,
    panelLng: Double,
    panelLabel: String             = "",
    layers: MapLayersState         = MapLayersState(),
    isLocked: Boolean              = false,
    onFeatureClick: (Long) -> Unit = {},
    modifier: Modifier             = Modifier,
) {
    val latestLayers = rememberUpdatedState(layers)
    val latestClick  = rememberUpdatedState(onFeatureClick)
    var mapRef       by remember { mutableStateOf<MapLibreMap?>(null) }

    // True when this is a focused detail view, false for the list overview
    val isFocused = panelLat != 0.0 || panelLng != 0.0

    AndroidView(modifier = modifier,
        factory = { ctx ->
            MapLibre.getInstance(ctx)
            MapView(ctx).apply {
                onCreate(null)
                getMapAsync { m ->
                    mapRef = m
                    m.setStyle(Style.Builder().withEsri()) { style ->
                        val L = latestLayers.value

                        // Camera: focused panel OR centroid of all panel points
                        val cameraTarget = when {
                            isFocused            -> LatLng(panelLat, panelLng)
                            L.panelPoints.isNotEmpty() -> LatLng(
                                L.panelPoints.map { it.latitude  }.average(),
                                L.panelPoints.map { it.longitude }.average(),
                            )
                            L.communeCenter.latitude != 0.0 || L.communeCenter.longitude != 0.0
                                                 -> L.communeCenter
                            else                 -> LatLng(36.7, 3.05)  // Algeria fallback
                        }
                        val zoom = if (isFocused) 16.0 else 13.0
                        m.applyDefaults(cameraTarget, zoom)

                        style.addCommuneContext(L.communeContext)
                        // Per spreadsheet Phase08: Areas ✓ Districts ✓ Roads ✓ Buildings ✓ Spaces ✓
                        if (L.areaPolygons.isNotEmpty())
                            style.addAreasLayer(polygonFC(L.areaPolygons, labels = L.areaLabels))
                        style.addDistrictsLayer(polygonFC(L.districtPolygons, labels = L.districtLabels))
                        style.addRoadsLayer(lineFC(L.roadPolylines), labeled = false)
                        style.addBuildingsLayer(L.buildingPoints, L.buildingDbIds, L.buildingLabels)
                        style.addSpacesLayer(polygonFC(L.spacePolygons, labels = L.spaceLabels))

                        // Panel points: use all from layers (list view) or single focused point
                        val panelFC = if (isFocused) {
                            val f = Feature.fromGeometry(Point.fromLngLat(panelLng, panelLat))
                            if (panelLabel.isNotBlank()) f.addStringProperty("label", panelLabel)
                            FeatureCollection.fromFeature(f)
                        } else {
                            pointFC(L.panelPoints, L.panelPoints.indices.map { it.toLong() }, L.panelLabels)
                        }
                        style.addSource(GeoJsonSource("panel-src", panelFC))
                        style.addLayer(CircleLayer("panel-circle", "panel-src").apply {
                            setProperties(circleRadius(if (isFocused) 14f else 10f),
                                circleColor(C.PANEL),
                                circleStrokeWidth(if (isFocused) 3f else 2f),
                                circleStrokeColor("#FFFFFF"))
                        })
                        if (isFocused && panelLabel.isNotBlank())
                            style.addLabelLayer("panel-label", "panel-src", color = C.PANEL)
                        else if (!isFocused && L.panelLabels.isNotEmpty())
                            style.addLabelLayer("panel-label", "panel-src", color = C.PANEL)

                        m.addOnMapClickListener { ll ->
                            val id = m.hitTest(m.projection.toScreenLocation(ll), "panel-circle")
                            if (id != null) latestClick.value(id)
                            id != null
                        }
                    }
                }
            }
        },
        update = { _ ->
            val L = latestLayers.value
            mapRef?.setLocked(isLocked)
            mapRef?.getStyle { style ->
                // Refresh panel source when layers update (e.g. after DB load)
                val panelFC = if (isFocused) {
                    val f = Feature.fromGeometry(Point.fromLngLat(panelLng, panelLat))
                    if (panelLabel.isNotBlank()) f.addStringProperty("label", panelLabel)
                    FeatureCollection.fromFeature(f)
                } else {
                    pointFC(L.panelPoints, L.panelPoints.indices.map { it.toLong() }, L.panelLabels)
                }
                (style.getSource("panel-src") as? GeoJsonSource)?.setGeoJson(panelFC)
                // Refresh all context layers (may arrive after style loaded)
                (style.getSource("area-src") as? GeoJsonSource)
                    ?.setGeoJson(polygonFC(L.areaPolygons, labels = L.areaLabels))
                (style.getSource("dst-src") as? GeoJsonSource)
                    ?.setGeoJson(polygonFC(L.districtPolygons, labels = L.districtLabels))
                (style.getSource("roads-src") as? GeoJsonSource)
                    ?.setGeoJson(lineFC(L.roadPolylines))
                (style.getSource("bld-src") as? GeoJsonSource)
                    ?.setGeoJson(pointFC(L.buildingPoints, L.buildingDbIds, L.buildingLabels))
                (style.getSource("spc-src") as? GeoJsonSource)
                    ?.setGeoJson(polygonFC(L.spacePolygons, labels = L.spaceLabels))
            }
        })
}
