@file:Suppress("DEPRECATION")

package com.nars.maplibre.modes

import android.content.Context
import android.util.Log
import com.geoman.maplibre.geoman.Geoman
import com.geoman.maplibre.geoman.GeomanConstants
import com.geoman.maplibre.geoman.types.events.GmDrawEvent
import com.geoman.maplibre.geoman.types.events.GmEditEvent
import com.geoman.maplibre.geoman.core.events.GmEventBus
import com.geoman.maplibre.geoman.types.events.GmFeatureEvent
import com.geoman.maplibre.geoman.types.events.GmMapEvent
import com.geoman.maplibre.geoman.core.options.GmOptionsData
import com.geoman.maplibre.geoman.core.options.SettingsOptions
import com.geoman.maplibre.geoman.types.DrawModeName
import com.geoman.maplibre.geoman.types.EditModeName
import com.geoman.maplibre.geoman.types.geojson.Feature
import com.geoman.maplibre.geoman.types.geojson.FeatureCollection
import com.geoman.maplibre.geoman.types.geojson.Geometry
import com.geoman.maplibre.geoman.types.geojson.LineString
import com.geoman.maplibre.geoman.types.geojson.LngLat
import com.geoman.maplibre.geoman.types.geojson.Point
import com.geoman.maplibre.geoman.types.geojson.Polygon
import com.nars.maplibre.data.model.CircleGeometry
import com.nars.maplibre.data.model.DrawType
import com.nars.maplibre.data.model.LineStringGeometry
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.PhaseDefinition
import com.nars.maplibre.data.model.PointGeometry
import com.nars.maplibre.data.model.PolygonGeometry
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import org.maplibre.android.geometry.LatLng
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.maps.MapView
import org.maplibre.android.style.expressions.Expression
import org.maplibre.android.style.layers.CircleLayer
import org.maplibre.android.style.layers.FillLayer
import org.maplibre.android.style.layers.LineLayer
import org.maplibre.android.style.layers.SymbolLayer
import org.maplibre.android.style.sources.GeoJsonSource
import java.util.UUID

/**
 * NARS wrapper for Geoman
 * Provides NARS-specific configuration and integration
 */
class NarsGeoman(
    private val mapView: MapView,
    private val map: MapLibreMap,
    private val context: Context,
    private val onFeatureCreated: (NarsFeature) -> Unit,
    private val onFeatureUpdated: (NarsFeature) -> Unit,
    private val onFeatureDeleted: (String) -> Unit
) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)

    // Geoman instance
    private val geoman: Geoman

    // Current drawing phase
    private var currentPhase: PhaseDefinition? = null

    // Edit mode state
    private var editingFeatureId: String? = null
    private var editingFeature: NarsFeature? = null

    // Track added feature IDs for proper cleanup
    private val addedFeatureIds = mutableSetOf<String>()

    // State flows
    private val _isDrawing = MutableStateFlow(false)
    val isDrawing: StateFlow<Boolean> = _isDrawing.asStateFlow()

    private val _isEditing = MutableStateFlow(false)
    val isEditing: StateFlow<Boolean> = _isEditing.asStateFlow()

    init {
        // Configure Geoman for NARS
        // Note: useControlsUi = true to enable click handling via GmControl
        // The control UI is hidden (showControlsOnMap = false), we use custom UI
        val options = GmOptionsData(
            settings = SettingsOptions(
                useControlsUi = true, // Required for click handling
                showControlsOnMap = false, // Hide default UI - we use custom UI
                enableSnap = true,
                snapDistance = 20f
            )
        )

        geoman = Geoman(mapView, map, options)

        // Set up event listeners
        setupEventListeners()
        
        Log.d("NarsGeoman", "NarsGeoman initialized with Geoman instance: $geoman")
        Log.d("NarsGeoman", "Geoman control: ${geoman.control}")
    }
    
    /**
     * Set up Geoman event listeners
     */
    private fun setupEventListeners() {
        scope.launch {
            geoman.events.events.collect { event ->
                when (event) {
                    is GmMapEvent.Loaded -> {
                        Log.d("NarsGeoman", "Geoman loaded")
                    }
                    
                    is GmDrawEvent.Create -> {
                        Log.d("NarsGeoman", "GmDrawEvent.Create received: shape=${event.shape}, feature=${event.feature}, feature javaClass=${event.feature?.javaClass?.name}")
                        handleFeatureCreated(event)
                    }
                    
                    is GmDrawEvent.EditEnd -> {
                        Log.d("NarsGeoman", "Edit ended: ${event.shape}")
                        handleEditEnd(event)
                    }
                    
                    is GmEditEvent.ChangeEnd -> {
                        Log.d("NarsGeoman", "Geometry changed")
                        handleGeometryChanged(event)
                    }
                    
                    is GmEditEvent.Delete -> {
                        Log.d("NarsGeoman", "Feature deleted")
                        handleFeatureDeleted(event)
                    }
                    
                    is GmFeatureEvent.Created -> {
                        Log.d("NarsGeoman", "Feature event created")
                    }
                    
                    is GmFeatureEvent.Updated -> {
                        Log.d("NarsGeoman", "Feature event updated")
                    }
                    
                    is GmFeatureEvent.Removed -> {
                        Log.d("NarsGeoman", "Feature event removed")
                    }
                }
            }
        }
    }
    
    /**
     * Handle feature creation from Geoman
     */
    private fun handleFeatureCreated(event: GmDrawEvent.Create) {
        val phase = currentPhase ?: run {
            Log.e("NarsGeoman", "No current phase set when creating feature!")
            return
        }
        
        Log.d("NarsGeoman", "Creating feature for phase: ${phase.label} (${phase.key})")
        
        // Create the NARS feature with the current phase
        val narsFeature = createNarsFeatureFromEvent(event, phase)
        onFeatureCreated(narsFeature)
        _isDrawing.value = false
    }

    /**
     * Create NarsFeature from Geoman event with proper phase
     */
    private fun createNarsFeatureFromEvent(event: GmDrawEvent.Create, phase: PhaseDefinition): NarsFeature {
        val featureData = event.feature as? com.geoman.maplibre.geoman.core.features.FeatureData
        val geometry = if (featureData != null) {
            // Check if it's a circle (stored as polygon with center/radius properties)
            if (featureData.properties["shapeType"] == "circle" || 
                featureData.properties["radius"] != null) {
                extractCircleGeometry(featureData)
            } else {
                extractGeometryFromEvent(event)
            }
        } else {
            extractGeometryFromEvent(event)
        }

        return NarsFeature(
            id = featureData?.id ?: UUID.randomUUID().toString(),
            type = getFeatureTypeFromPhase(phase),
            geometry = geometry,
            properties = com.nars.maplibre.data.model.FeatureProperties(
                phase = phase.key,
                color = phase.color
            )
        )
    }
    
    /**
     * Handle edit end event
     */
    private fun handleEditEnd(@Suppress("UNUSED_PARAMETER") event: GmDrawEvent.EditEnd) {
        editingFeatureId?.let { _ ->
            // Get updated geometry from Geoman
            // This would need to query Geoman's feature store
        }
        _isEditing.value = false
        editingFeatureId = null
    }

    /**
     * Handle geometry changed during edit - mirror updates to NARS render layer live
     */
    private fun handleGeometryChanged(event: GmEditEvent.ChangeEnd) {
        val featureData = event.feature as? com.geoman.maplibre.geoman.core.features.FeatureData
        if (featureData == null) return

        val geometry = extractGeometryFromFeatureData(featureData)
        editingFeature?.let { original ->
            val updated = original.copy(geometry = geometry)
            // Update the NARS render layer live so user sees changes immediately
            onFeatureUpdated(updated)
        }
    }

    /**
     * Handle feature deletion
     */
    private fun handleFeatureDeleted(@Suppress("UNUSED_PARAMETER") event: GmEditEvent.Delete) {
        editingFeatureId?.let { featureId ->
            onFeatureDeleted(featureId)
        }
        _isEditing.value = false
        editingFeatureId = null
    }
    
    /**
     * Extract geometry from Geoman event
     */
    private fun extractGeometryFromEvent(event: GmDrawEvent.Create): com.nars.maplibre.data.model.Geometry {
        // The feature in the event is the raw FeatureData object from Android Geoman
        val featureObj = event.feature
        Log.d("NarsGeoman", "extractGeometryFromEvent: event.feature javaClass=${featureObj?.javaClass?.name}, toString=${featureObj?.toString()}")
        
        // Try direct cast first
        val featureData = featureObj as? com.geoman.maplibre.geoman.core.features.FeatureData
        Log.d("NarsGeoman", "extractGeometryFromEvent: featureData cast success=${featureData != null}")
        
        val geometry = featureData?.geometry
        Log.d("NarsGeoman", "extractGeometryFromEvent: geometry type=${geometry?.javaClass?.simpleName}")

        if (geometry == null) {
            Log.w("NarsGeoman", "No geometry in event feature")
            return PointGeometry(coordinates = listOf(0.0, 0.0))
        }

        return when (geometry) {
            is com.geoman.maplibre.geoman.types.geojson.Point -> {
                val coords = geometry.coordinates
                PointGeometry(coordinates = listOf(coords[0], coords[1]))
            }
            is com.geoman.maplibre.geoman.types.geojson.LineString -> {
                // Flatten coordinates: [lon1, lat1, lon2, lat2, ...]
                val flattened = geometry.coordinates.flatMap { coord -> listOf(coord[0], coord[1]) }
                LineStringGeometry(coordinates = flattened)
            }
            is com.geoman.maplibre.geoman.types.geojson.Polygon -> {
                // Extract exterior ring only (flatten coordinates)
                if (geometry.coordinates.isNotEmpty()) {
                    val exteriorRing = geometry.coordinates[0]
                    val flattened = exteriorRing.flatMap { coord -> listOf(coord[0], coord[1]) }
                    PolygonGeometry(coordinates = flattened)
                } else {
                    PolygonGeometry(coordinates = emptyList())
                }
            }
            is com.geoman.maplibre.geoman.types.geojson.MultiPolygon -> {
                // For multi-polygon, use the first polygon's exterior ring
                if (geometry.coordinates.isNotEmpty() && geometry.coordinates[0].isNotEmpty()) {
                    val exteriorRing = geometry.coordinates[0][0]
                    val flattened = exteriorRing.flatMap { coord -> listOf(coord[0], coord[1]) }
                    PolygonGeometry(coordinates = flattened)
                } else {
                    PolygonGeometry(coordinates = emptyList())
                }
            }
            else -> {
                Log.w("NarsGeoman", "Unknown geometry type: ${geometry::class.simpleName}")
                PointGeometry(coordinates = listOf(0.0, 0.0))
            }
        }
    }
    
    /**
     * Extract circle geometry from feature properties
     * Circles are stored as polygons with center and radius in properties
     */
    private fun extractCircleGeometry(featureData: com.geoman.maplibre.geoman.core.features.FeatureData): CircleGeometry {
        val center = featureData.properties["center"] as? com.geoman.maplibre.geoman.types.geojson.LngLat
        val radius = featureData.properties["radius"] as? Double

        if (center != null && radius != null) {
            return CircleGeometry(coordinates = listOf(center.longitude, center.latitude, radius))
        }

        // Fallback: calculate from polygon coordinates
        if (featureData.geometry is com.geoman.maplibre.geoman.types.geojson.Polygon) {
            val polygon = featureData.geometry as com.geoman.maplibre.geoman.types.geojson.Polygon
            val ring = polygon.getExteriorRing()
            if (ring.size >= 2) {
                // Calculate center as average of coordinates
                val sumLon = ring.sumOf { coord -> coord.longitude }
                val sumLat = ring.sumOf { coord -> coord.latitude }
                val avgLon = sumLon / ring.size
                val avgLat = sumLat / ring.size
                val centerPoint = com.geoman.maplibre.geoman.types.geojson.LngLat(avgLon, avgLat)
                val centerLngLat = ring.first()
                val calcRadius = com.geoman.maplibre.geoman.utils.GeometryUtils.calculateDistance(
                    centerPoint,
                    centerLngLat
                )
                return CircleGeometry(coordinates = listOf(avgLon, avgLat, calcRadius))
            }
        }

        return CircleGeometry(coordinates = listOf(0.0, 0.0, 0.0))
    }
    
    /**
     * Set current phase for drawing
     */
    fun setCurrentPhase(phase: PhaseDefinition) {
        currentPhase = phase
    }
    
    /**
     * Start drawing based on current phase
     */
    fun startDrawing() {
        val phase = currentPhase ?: return
        Log.d("NarsGeoman", "startDrawing called for phase: ${phase.label}, drawType: ${phase.drawType}")

        // Disable ALL modes first to prevent conflicts
        geoman.disableAllModes()
        _isEditing.value = false
        editingFeatureId = null
        editingFeature = null

        _isDrawing.value = true

        when (phase.drawType) {
            DrawType.POLYGON -> {
                Log.d("NarsGeoman", "Enabling POLYGON draw mode")
                geoman.enableDraw(DrawModeName.POLYGON)
            }
            DrawType.POLYLINE -> {
                Log.d("NarsGeoman", "Enabling LINE draw mode")
                geoman.enableDraw(DrawModeName.LINE)
            }
            DrawType.CIRCLE -> {
                Log.d("NarsGeoman", "Enabling CIRCLE draw mode")
                geoman.enableDraw(DrawModeName.CIRCLE)
            }
            DrawType.MARKER -> {
                Log.d("NarsGeoman", "Enabling MARKER draw mode")
                geoman.enableDraw(DrawModeName.MARKER)
            }
        }
        Log.d("NarsGeoman", "Drawing mode enabled, isDrawing=${_isDrawing.value}")
    }

    /**
     * Stop drawing
     */
    fun stopDrawing() {
        Log.d("NarsGeoman", "stopDrawing called")
        _isDrawing.value = false
        geoman.disableAllModes()
    }

    /**
     * Start editing a feature
     */
    fun startEditing(feature: NarsFeature) {
        Log.d("NarsGeoman", "startEditing called for feature: ${feature.id}, type: ${feature.type}")

        // Disable ALL modes first to prevent conflicts
        geoman.disableAllModes()
        _isDrawing.value = false

        editingFeatureId = feature.id
        editingFeature = feature
        _isEditing.value = true

        // Convert NARS feature to GeoJSON and import to Geoman for rendering
        val geoJsonFeature = convertToGeoJson(feature)
        geoman.addGeoJsonFeature(geoJsonFeature)

        // Enable edit mode
        geoman.enableEdit(EditModeName.CHANGE)

        // Directly start editing with the feature (bypasses click-based selection)
        geoman.startEditingFeature(convertToGeomanFeatureData(feature))

        Log.d("NarsGeoman", "Edit mode enabled, isEditing=${_isEditing.value}")
    }

    /**
     * Convert NarsFeature to Geoman FeatureData for the ChangeEditor
     */
    private fun convertToGeomanFeatureData(narsFeature: NarsFeature): com.geoman.maplibre.geoman.core.features.FeatureData {
        val geoJsonFeature = convertToGeoJson(narsFeature)
        val sourceName = when (narsFeature.geometry) {
            is com.nars.maplibre.data.model.PointGeometry -> com.geoman.maplibre.geoman.GeomanConstants.SOURCE_MARKERS
            is com.nars.maplibre.data.model.LineStringGeometry -> com.geoman.maplibre.geoman.GeomanConstants.SOURCE_LINES
            is com.nars.maplibre.data.model.PolygonGeometry -> com.geoman.maplibre.geoman.GeomanConstants.SOURCE_POLYGONS
            is com.nars.maplibre.data.model.CircleGeometry -> com.geoman.maplibre.geoman.GeomanConstants.SOURCE_CIRCLES
        }
        return com.geoman.maplibre.geoman.core.features.FeatureData(
            id = narsFeature.id,
            sourceName = sourceName,
            feature = geoJsonFeature,
            properties = mutableMapOf()
        )
    }

    /**
     * Stop editing
     */
    fun stopEditing() {
        _isEditing.value = false
        editingFeatureId = null
        editingFeature = null
        geoman.disableAllModes()
    }

    /**
     * Commit edits - get updated geometry from Geoman and call onFeatureUpdated
     */
    fun commitEdits() {
        editingFeature?.let { originalFeature ->
            // Try to get updated feature from Geoman's feature store
            val sourceNames = listOf(
                com.geoman.maplibre.geoman.GeomanConstants.SOURCE_MARKERS,
                com.geoman.maplibre.geoman.GeomanConstants.SOURCE_LINES,
                com.geoman.maplibre.geoman.GeomanConstants.SOURCE_POLYGONS,
                com.geoman.maplibre.geoman.GeomanConstants.SOURCE_CIRCLES
            )

            var updatedGeometry: com.nars.maplibre.data.model.Geometry? = null

            for (sourceName in sourceNames) {
                val featureData = geoman.features.getFeature(sourceName, originalFeature.id)
                if (featureData != null) {
                    Log.d("NarsGeoman", "Found edited feature in $sourceName")
                    updatedGeometry = extractGeometryFromFeatureData(featureData)
                    break
                }
            }

            if (updatedGeometry != null) {
                val updatedFeature = originalFeature.copy(geometry = updatedGeometry)
                Log.d("NarsGeoman", "Committing updated feature geometry")
                onFeatureUpdated(updatedFeature)
            } else {
                Log.w("NarsGeoman", "Could not find updated geometry for feature ${originalFeature.id}")
                // Still call callback with original feature to trigger save
                onFeatureUpdated(originalFeature)
            }
        }
        stopEditing()
    }

    /**
     * Extract geometry from Geoman FeatureData
     */
    private fun extractGeometryFromFeatureData(featureData: com.geoman.maplibre.geoman.core.features.FeatureData): com.nars.maplibre.data.model.Geometry {
        val geometry = featureData.geometry

        return when (geometry) {
            is com.geoman.maplibre.geoman.types.geojson.Point -> {
                val coords = geometry.coordinates
                PointGeometry(coordinates = listOf(coords[0], coords[1]))
            }
            is com.geoman.maplibre.geoman.types.geojson.LineString -> {
                val flattened = geometry.coordinates.flatMap { coord -> listOf(coord[0], coord[1]) }
                LineStringGeometry(coordinates = flattened)
            }
            is com.geoman.maplibre.geoman.types.geojson.Polygon -> {
                if (geometry.coordinates.isNotEmpty()) {
                    val exteriorRing = geometry.coordinates[0]
                    val flattened = exteriorRing.flatMap { coord -> listOf(coord[0], coord[1]) }
                    PolygonGeometry(coordinates = flattened)
                } else {
                    PolygonGeometry(coordinates = emptyList())
                }
            }
            is com.geoman.maplibre.geoman.types.geojson.MultiPolygon -> {
                if (geometry.coordinates.isNotEmpty() && geometry.coordinates[0].isNotEmpty()) {
                    val exteriorRing = geometry.coordinates[0][0]
                    val flattened = exteriorRing.flatMap { coord -> listOf(coord[0], coord[1]) }
                    PolygonGeometry(coordinates = flattened)
                } else {
                    PolygonGeometry(coordinates = emptyList())
                }
            }
            else -> {
                Log.w("NarsGeoman", "Unknown geometry type: ${geometry::class.simpleName}")
                PointGeometry(coordinates = listOf(0.0, 0.0))
            }
        }
    }
    
    /**
     * Cancel edits - remove the Geoman copy and restore original feature
     */
    fun cancelEdits() {
        // Remove the edited feature from Geoman's internal store
        editingFeatureId?.let { featureId ->
            val sourceNames = listOf(
                com.geoman.maplibre.geoman.GeomanConstants.SOURCE_MARKERS,
                com.geoman.maplibre.geoman.GeomanConstants.SOURCE_LINES,
                com.geoman.maplibre.geoman.GeomanConstants.SOURCE_POLYGONS,
                com.geoman.maplibre.geoman.GeomanConstants.SOURCE_CIRCLES
            )
            for (sourceName in sourceNames) {
                val featureData = geoman.features.getFeature(sourceName, featureId)
                if (featureData != null) {
                    geoman.features.removeFeature(sourceName, featureId)
                    Log.d("NarsGeoman", "Removed edited feature from Geoman source $sourceName")
                    break
                }
            }
        }
        stopEditing()
    }
    
    /**
     * Convert NarsFeature to GeoJSON Feature
     */
    private fun convertToGeoJson(narsFeature: NarsFeature): Feature {
        val geometry = convertGeometryToGeoJson(narsFeature.geometry)
        
        return Feature(
            id = narsFeature.id,
            geometry = geometry,
            properties = mapOf(
                "id" to narsFeature.id,
                "type" to narsFeature.type.value,
                "phase" to narsFeature.properties.phase,
                "name" to (narsFeature.properties.name ?: ""),
                "color" to narsFeature.properties.color
            )
        )
    }
    
    /**
     * Convert NARS geometry to GeoJSON geometry
     */
    private fun convertGeometryToGeoJson(geometry: com.nars.maplibre.data.model.Geometry): Geometry {
        return when (geometry) {
            is PointGeometry -> {
                Point.fromLngLat(LngLat(geometry.coordinates[0], geometry.coordinates[1]))
            }
            is LineStringGeometry -> {
                val coords = geometry.coordinates.chunked(2).map { LngLat(it[0], it[1]) }
                LineString.fromLngLats(coords)
            }
            is PolygonGeometry -> {
                val coords = geometry.coordinates.chunked(2).map { LngLat(it[0], it[1]) }
                Polygon.fromLngLats(listOf(coords))
            }
            is CircleGeometry -> {
                // Circles are stored as center + radius
                Point.fromLngLat(LngLat(geometry.coordinates[0], geometry.coordinates[1]))
            }
        }
    }
    
    /**
     * Parse color string to Android Color Int
     * Handles formats: #RRGGBB, #RRGGBBAA, RRGGBB
     */
    private fun parseColor(colorStr: String): Int {
        return try {
            val color = colorStr.trimStart('#')
            when (color.length) {
                6 -> android.graphics.Color.parseColor("#$color")
                8 -> android.graphics.Color.parseColor("#$color")
                else -> android.graphics.Color.parseColor("#$colorStr")
            }
        } catch (e: Exception) {
            android.graphics.Color.GRAY // Fallback color
        }
    }

    /**
     * Convert GeoJSON geometry to JSON string
     */
    private fun geometryToJson(geometry: com.geoman.maplibre.geoman.types.geojson.Geometry): String {
        return when (geometry) {
            is com.geoman.maplibre.geoman.types.geojson.Point -> {
                val coords = geometry.coordinates
                """{"type": "Point", "coordinates": [${coords[0]}, ${coords[1]}]}"""
            }
            is com.geoman.maplibre.geoman.types.geojson.LineString -> {
                val coords = geometry.coordinates.joinToString(",") { coord ->
                    "[${coord.joinToString(", ")}]"
                }
                """{"type": "LineString", "coordinates": [$coords]}"""
            }
            is com.geoman.maplibre.geoman.types.geojson.Polygon -> {
                val rings = geometry.coordinates.joinToString(",") { ring ->
                    ring.joinToString(",", "[", "]") { coord -> "[${coord.joinToString(", ")}]" }
                }
                """{"type": "Polygon", "coordinates": [$rings]}"""
            }
            else -> """{"type": "Point", "coordinates": [0, 0]}"""
        }
    }

    /**
     * Convert properties map to JSON string
     */
    private fun propertiesToJson(properties: Map<String, Any?>): String {
        // Map 'name' -> 'label' for label layer to work
        val props = properties.toMutableMap()
        if (props.containsKey("name") && !props.containsKey("label")) {
            props["label"] = props["name"]
        }
        return props.entries.joinToString(",", "{", "}") { (key, value) ->
            """ "$key": "${value ?: ""}" """.trim()
        }
    }

    /**
     * Add a feature to the map (render directly to MapLibre)
     */
    fun addFeature(feature: NarsFeature) {
        // Check if feature already exists
        if (addedFeatureIds.contains(feature.id)) {
            Log.w("NarsGeoman", "Feature ${feature.id} already exists, skipping")
            return
        }
        
        val geoJsonFeature = convertToGeoJson(feature)
        val sourceName = "nars_${feature.id}"
        val layerName = "nars_layer_${feature.id}"

        // Convert feature to GeoJSON string
        val geoJsonString = """
            {
                "type": "Feature",
                "id": "${geoJsonFeature.id}",
                "geometry": ${geometryToJson(geoJsonFeature.geometry)},
                "properties": ${propertiesToJson(geoJsonFeature.properties)}
            }
        """.trimIndent()

        // Check if source already exists and remove it first
        try {
            map.style?.getSource(sourceName)?.let { _ ->
                map.style?.removeSource(sourceName)
                Log.d("NarsGeoman", "Removed existing source $sourceName before re-adding")
            }
        } catch (e: Exception) {
            Log.w("NarsGeoman", "Error checking/removing existing source: ${e.message}")
        }

        // Add GeoJSON source to map
        val source = GeoJsonSource(sourceName, geoJsonString)
        map.style?.addSource(source)

        // Get style based on phase (matching web app) - use phase color from definition
        val phaseKey = feature.properties.phase
        val style = getFeatureStyle(phaseKey)

        // Add appropriate layer based on geometry type
        when (feature.geometry) {
            is PointGeometry -> {
                // Add marker symbol layer
                val layer = SymbolLayer(layerName, sourceName)
                layer.setProperties(
                    org.maplibre.android.style.layers.PropertyFactory.iconImage("default-marker"),
                    org.maplibre.android.style.layers.PropertyFactory.iconSize(0.5f),
                    org.maplibre.android.style.layers.PropertyFactory.iconAllowOverlap(true)
                )
                map.style?.addLayer(layer)
                
                // Add label for point
                addLabelLayer(layerName, sourceName, feature.properties.name)
            }
            is LineStringGeometry -> {
                val lineColor = parseColor(style.lineColor)
                val layer = LineLayer(layerName, sourceName)
                layer.setProperties(
                    org.maplibre.android.style.layers.PropertyFactory.lineColor(lineColor),
                    org.maplibre.android.style.layers.PropertyFactory.lineWidth(style.lineWidth.toFloat())
                )
                map.style?.addLayer(layer)
                
                // Add label for line
                addLabelLayer(layerName, sourceName, feature.properties.name)
                
                // Disabled: vertex markers cause lag - re-enable if editing is needed
                // addVertexMarkers(feature)
            }
            is PolygonGeometry -> {
                val lineColor = parseColor(style.lineColor)

                // Skip FillLayer entirely - use only edge lines for outline display
                // This ensures no fill color is shown for polygons

                // Create separate source for polygon edges (stroke)
                val edgeSourceName = "${sourceName}_edges"
                val edgeGeoJson = buildPolygonEdgesGeoJson(feature.geometry.coordinates)

                // Check if edge source already exists and remove it first
                try {
                    map.style?.getSource(edgeSourceName)?.let { _ ->
                        map.style?.removeSource(edgeSourceName)
                    }
                } catch (e: Exception) {
                    Log.w("NarsGeoman", "Error checking/removing existing edge source: ${e.message}")
                }

                val edgeSource = GeoJsonSource(edgeSourceName, edgeGeoJson)
                map.style?.addSource(edgeSource)
                
                // Outline layer using the edge source
                val outlineLayer = LineLayer("${layerName}_outline", edgeSourceName)
                outlineLayer.setProperties(
                    org.maplibre.android.style.layers.PropertyFactory.lineColor(lineColor),
                    org.maplibre.android.style.layers.PropertyFactory.lineWidth(style.lineWidth.toFloat())
                )
                map.style?.addLayer(outlineLayer)

                // Add label for polygon
                addLabelLayer(layerName, sourceName, feature.properties.name)

                // Disabled: vertex markers cause lag - re-enable if editing is needed
                // addVertexMarkers(feature)
            }
            is CircleGeometry -> {
                val lineColor = parseColor(style.lineColor)
                val circleGeom = feature.geometry
                val radiusMeters = circleGeom.coordinates[2].takeIf { it > 0 } ?: 50.0
                val centerLng = circleGeom.coordinates[0]
                val centerLat = circleGeom.coordinates[1]

                // Generate a polygon approximation of the circle (32 segments)
                val circlePolygonGeoJson = buildCircleGeoJson(centerLng, centerLat, radiusMeters)

                // Check if source already exists and remove it first
                try {
                    map.style?.getSource(sourceName)?.let { _ ->
                        map.style?.removeSource(sourceName)
                    }
                } catch (e: Exception) {
                    Log.w("NarsGeoman", "Error checking/removing existing circle source: ${e.message}")
                }

                // Create source for the circle polygon
                val circleSource = GeoJsonSource(sourceName, circlePolygonGeoJson)
                map.style?.addSource(circleSource)

                // Fill layer for circle - no fill (transparent)
                val fillLayer = FillLayer(layerName, sourceName)
                fillLayer.setProperties(
                    org.maplibre.android.style.layers.PropertyFactory.fillOpacity(0f)
                )
                map.style?.addLayer(fillLayer)

                // Stroke layer for circle edge
                val strokeLayer = LineLayer("${layerName}_stroke", sourceName)
                strokeLayer.setProperties(
                    org.maplibre.android.style.layers.PropertyFactory.lineColor(lineColor),
                    org.maplibre.android.style.layers.PropertyFactory.lineWidth(style.lineWidth.toFloat())
                )
                map.style?.addLayer(strokeLayer)

                // Add label for circle
                addLabelLayer(layerName, sourceName, feature.properties.name)
            }
        }

        // Also store in Geoman for event handling
        geoman.addGeoJsonFeature(geoJsonFeature, getSourceNameForGeometry(feature.geometry))
        
        // Track added feature
        addedFeatureIds.add(feature.id)
        Log.d("NarsGeoman", "Added feature ${feature.id} to tracking set (total: ${addedFeatureIds.size})")
    }

    /**
     * Get feature style matching web app
     */
    private fun getFeatureStyle(phaseKey: String): FeatureStyle {
        return when (phaseKey) {
            "areas" -> FeatureStyle("#8e44ad", "#8e44ad", 0.0, 2)
            "districts" -> FeatureStyle("#f39c12", "#f39c12", 0.0, 3)
            "cityCenter" -> FeatureStyle("#e74c3c", "#e74c3c", 0.5, 2)
            "roads" -> FeatureStyle("#3498db", "#3498db", 0.0, 8)
            "houseEntrances" -> FeatureStyle("#27ae60", "#27ae60", 0.0, 2)
            "publicBuildings" -> FeatureStyle("#e67e22", "#e67e22", 0.25, 3)
            "publicSpaces" -> FeatureStyle("#2ecc71", "#2ecc71", 0.20, 3)
            "namingPanels" -> FeatureStyle("#9b59b6", "#9b59b6", 0.0, 2)
            else -> FeatureStyle("#8e44ad", "#8e44ad", 0.3, 2)
        }
    }

    /**
     * Feature style data class
     */
    private data class FeatureStyle(
        val fillColor: String,
        val lineColor: String,
        val fillOpacity: Double,
        val lineWidth: Int
    )

    /**
     * Add multiple features with phase filtering
     */
    fun addFeatures(features: List<NarsFeature>) {
        Log.d("NarsGeoman", "Adding ${features.size} features to map")
        
        // Filter features by current phase
        val currentPhaseKey = currentPhase?.key
        val featuresToAdd = if (currentPhaseKey != null) {
            features.filter { feature -> 
                feature.properties.phase == currentPhaseKey 
            }
        } else {
            features
        }
        
        Log.d("NarsGeoman", "Filtered to ${featuresToAdd.size} features for phase: $currentPhaseKey")
        featuresToAdd.forEach { feature ->
            Log.d("NarsGeoman", "Adding feature: id=${feature.id}, type=${feature.type}, phase=${feature.properties.phase}, geometry=${feature.geometry::class.simpleName}")
            addFeature(feature)
        }
    }
    
    /**
     * Update displayed features based on current phase
     */
    fun updateDisplayedFeatures(allFeatures: List<NarsFeature>) {
        // Clear all existing features first
        clearAllFeatures()
        
        // Re-add only features for current phase
        addFeatures(allFeatures)
        
        // Add road endpoint markers for roads phase
        if (currentPhase?.key == "roads") {
            addRoadEndpointMarkers(allFeatures)
        }
    }
    
    /**
     * Add road endpoint markers (green/red circles)
     */
    private fun addRoadEndpointMarkers(allFeatures: List<NarsFeature>) {
        val roads = allFeatures.filter { it.properties.phase == "roads" }
        if (roads.isEmpty()) return
        
        for (road in roads) {
            val geometry = road.geometry as? LineStringGeometry ?: continue
            val coords = geometry.coordinates.chunked(2)
            if (coords.size < 2) continue
            
            // Add start endpoint (green circle)
            addEndpointMarker(
                id = "${road.id}_start",
                lon = coords[0][0],
                lat = coords[0][1],
                isStart = true
            )
            
            // Add end endpoint (red circle)
            addEndpointMarker(
                id = "${road.id}_end",
                lon = coords.last()[0],
                lat = coords.last()[1],
                isStart = false
            )
            
            // Add label marker (text label at midpoint with road name)
            val midIdx = coords.size / 2
            addLabelLayer(
                layerName = "${road.id}_label",
                labelText = road.properties.name,
                lon = coords[midIdx][0],
                lat = coords[midIdx][1]
            )
        }
        
        Log.d("NarsGeoman", "Added road endpoint markers")
    }
    
    /**
     * Add text label layer (uses SymbolLayer with glyphs)
     */
    private fun addLabelLayer(layerName: String, labelText: String?, lon: Double, lat: Double) {
        if (labelText.isNullOrBlank()) return
        
        val sourceName = "${layerName}_src"
        val labelLayerName = layerName
        
        val geoJson = """
            {"type": "FeatureCollection", "features": [{"type": "Feature", "geometry": {"type": "Point", "coordinates": [$lon, $lat]}, "properties": {"text": "$labelText"}}]}
        """.trimIndent()
        
        try {
            map.style?.addSource(GeoJsonSource(sourceName, geoJson))
        } catch (e: Exception) {
            // Source may already exist
        }
        
        val symbolLayer = SymbolLayer(labelLayerName, sourceName)
        symbolLayer.setProperties(
            org.maplibre.android.style.layers.PropertyFactory.textField(
                org.maplibre.android.style.expressions.Expression.get("text")
            ),
            org.maplibre.android.style.layers.PropertyFactory.textColor(android.graphics.Color.BLACK),
            org.maplibre.android.style.layers.PropertyFactory.textSize(14f),
            org.maplibre.android.style.layers.PropertyFactory.textFont(
                arrayOf("Roboto Regular", "Arial Unicode MS Regular")
            ),
            org.maplibre.android.style.layers.PropertyFactory.textAllowOverlap(true),
            org.maplibre.android.style.layers.PropertyFactory.textHaloColor(android.graphics.Color.WHITE),
            org.maplibre.android.style.layers.PropertyFactory.textHaloWidth(3f)
        )
        
        try {
            map.style?.addLayer(symbolLayer)
            Log.d("NarsGeoman", "Added text label $labelText at $lon,$lat")
        } catch (e: Exception) {}
    }
    
    /**
     * Add endpoint marker
     */
    private fun addEndpointMarker(id: String, lon: Double, lat: Double, isStart: Boolean) {
        val layerName = "nars_$id"
        val sourceName = "${layerName}_src"
        
        val geoJson = """
            {"type": "FeatureCollection", "features": [{"type": "Feature", "geometry": {"type": "Point", "coordinates": [$lon, $lat]}, "properties": {}}]}
        """.trimIndent()
        
        try {
            val source = GeoJsonSource(sourceName, geoJson)
            map.style?.addSource(source)
        } catch (e: Exception) {
            return
        }
        
        val color = if (isStart) android.graphics.Color.parseColor("#2ecc71") else android.graphics.Color.parseColor("#e74c3c")
        
        val circleLayer = CircleLayer("${layerName}_circle", sourceName)
        circleLayer.setProperties(
            org.maplibre.android.style.layers.PropertyFactory.circleColor(color),
            org.maplibre.android.style.layers.PropertyFactory.circleRadius(14f),
            org.maplibre.android.style.layers.PropertyFactory.circleStrokeColor(android.graphics.Color.WHITE),
            org.maplibre.android.style.layers.PropertyFactory.circleStrokeWidth(3f)
        )
        
        try {
            map.style?.addLayer(circleLayer)
        } catch (e: Exception) {}
    }

    /**
     * Get the correct Geoman source name for a geometry type
     */
    private fun getSourceNameForGeometry(geometry: com.nars.maplibre.data.model.Geometry): String {
        return when (geometry) {
            is PointGeometry -> com.geoman.maplibre.geoman.GeomanConstants.SOURCE_MARKERS
            is LineStringGeometry -> com.geoman.maplibre.geoman.GeomanConstants.SOURCE_LINES
            is PolygonGeometry -> com.geoman.maplibre.geoman.GeomanConstants.SOURCE_POLYGONS
            is CircleGeometry -> com.geoman.maplibre.geoman.GeomanConstants.SOURCE_CIRCLES
        }
    }

    /**
     * Add label layer for a feature — each NARS feature has its own source,
     * so we can use the static text directly (no need for expression lookup).
     */
    private fun addLabelLayer(layerName: String, sourceName: String, labelText: String?) {
        if (labelText.isNullOrBlank()) return
        
        val labelLayerName = "${layerName}_label"
        
        // Check if label layer already exists and remove
        try {
            map.style?.getLayer(labelLayerName)?.let {
                map.style?.removeLayer(labelLayerName)
            }
        } catch (e: Exception) {}
        
        val labelLayer = SymbolLayer(labelLayerName, sourceName)
        
        // Use literal expression with the actual text value
        val textExpr = org.maplibre.android.style.expressions.Expression.literal(labelText)
        
        labelLayer.setProperties(
            org.maplibre.android.style.layers.PropertyFactory.textField(textExpr),
            org.maplibre.android.style.layers.PropertyFactory.textColor(android.graphics.Color.BLACK),
            org.maplibre.android.style.layers.PropertyFactory.textSize(14f),
            org.maplibre.android.style.layers.PropertyFactory.textAllowOverlap(true),
            org.maplibre.android.style.layers.PropertyFactory.textIgnorePlacement(true),
            org.maplibre.android.style.layers.PropertyFactory.textHaloColor(android.graphics.Color.WHITE),
            org.maplibre.android.style.layers.PropertyFactory.textHaloWidth(2f)
        )
        
        try {
            map.style?.addLayer(labelLayer)
            Log.d("NarsGeoman", "Added label layer $labelLayerName: $labelText")
        } catch (e: Exception) {
            Log.w("NarsGeoman", "Error adding label: ${e.message}")
        }
    }

    /**
     * Build GeoJSON LineString for polygon edges
     */
    private fun buildPolygonEdgesGeoJson(coordinates: List<Double>): String {
        // Convert flat coordinates to pairs
        val points = coordinates.chunked(2)

        // Ensure ring is closed (first == last)
        val ring = if (points.firstOrNull() == points.lastOrNull()) {
            points
        } else {
            points + points.firstOrNull()
        }

        // Build LineString coordinates string
        val coordsString = ring.filterNotNull().joinToString(",") { coord ->
            "[${coord[0]}, ${coord[1]}]"
        }

        return """{"type": "Feature", "geometry": {"type": "LineString", "coordinates": [$coordsString]}, "properties": {}}"""
    }

    /**
     * Build GeoJSON Polygon approximating a circle
     * Uses 32 segments for smooth appearance
     */
    private fun buildCircleGeoJson(centerLng: Double, centerLat: Double, radiusMeters: Double): String {
        val segments = 32
        val earthRadius = 6378137.0 // WGS84 equatorial radius in meters
        val radiusDegrees = Math.toDegrees(radiusMeters / earthRadius)

        val ring = (0..segments).map { i ->
            val angle = Math.toRadians(i * 360.0 / segments)
            val lng = centerLng + radiusDegrees * Math.cos(angle) / Math.cos(Math.toRadians(centerLat))
            val lat = centerLat + radiusDegrees * Math.sin(angle)
            "[${lng}, ${lat}]"
        }.joinToString(",")

        return """{"type": "Feature", "geometry": {"type": "Polygon", "coordinates": [[$ring]]}, "properties": {}}"""
    }

    /**
     * Add vertex markers for polygon and line features
     */
    private fun addVertexMarkers(feature: NarsFeature) {
        val coordinates = when (feature.geometry) {
            is LineStringGeometry -> feature.geometry.coordinates.chunked(2).map { coord ->
                doubleArrayOf(coord[0], coord[1])
            }
            is PolygonGeometry -> feature.geometry.coordinates.chunked(2).map { coord ->
                doubleArrayOf(coord[0], coord[1])
            }
            else -> return // Only add vertices for lines and polygons
        }

        // Add a small circle marker at each vertex
        coordinates.forEachIndexed { index, coord ->
            val vertexSourceName = "nars_vertex_${feature.id}_$index"
            val vertexLayerName = "nars_vertex_layer_${feature.id}_$index"
            
            try {
                // Check if vertex source already exists and remove it first
                map.style?.getSource(vertexSourceName)?.let { _ ->
                    map.style?.removeSource(vertexSourceName)
                }
                
                val vertexGeoJson = """
                    {
                        "type": "Feature",
                        "geometry": {"type": "Point", "coordinates": [${coord[0]}, ${coord[1]}]},
                        "properties": {"isVertex": true}
                    }
                """.trimIndent()
                
                val source = GeoJsonSource(vertexSourceName, vertexGeoJson)
                map.style?.addSource(source)
                
                val circleLayer = CircleLayer(vertexLayerName, vertexSourceName)
                circleLayer.setProperties(
                    org.maplibre.android.style.layers.PropertyFactory.circleColor(android.graphics.Color.RED),
                    org.maplibre.android.style.layers.PropertyFactory.circleRadius(6f),
                    org.maplibre.android.style.layers.PropertyFactory.circleStrokeColor(android.graphics.Color.WHITE),
                    org.maplibre.android.style.layers.PropertyFactory.circleStrokeWidth(2f)
                )
                map.style?.addLayer(circleLayer)
                
                Log.d("NarsGeoman", "Added vertex marker at index $index: [${coord[0]}, ${coord[1]}]")
            } catch (e: Exception) {
                Log.w("NarsGeoman", "Error adding vertex marker: ${e.message}")
            }
        }
    }

    /**
     * Update a feature's properties on the map (remove old, re-add with new properties)
     */
    fun updateFeatureOnMap(feature: NarsFeature) {
        // Remove the old feature from the map
        removeFeature(feature.id)
        // Re-add with updated properties
        addFeature(feature)
    }

    /**
     * Update feature ID after save to backend
     */
    fun updateFeatureId(oldId: String, newId: String) {
        Log.d("NarsGeoman", "Updating feature ID from $oldId to $newId")
        
        // Update tracking set
        if (addedFeatureIds.contains(oldId)) {
            addedFeatureIds.remove(oldId)
            addedFeatureIds.add(newId)
            Log.d("NarsGeoman", "Updated tracking set: $oldId -> $newId")
        }
    }

    /**
     * Remove a feature from the map
     */
    fun removeFeature(featureId: String) {
        Log.d("NarsGeoman", "Removing feature: $featureId")
        
        // First try to remove from Geoman's feature store
        val sourceNames = listOf(
            com.geoman.maplibre.geoman.GeomanConstants.SOURCE_MARKERS,
            com.geoman.maplibre.geoman.GeomanConstants.SOURCE_LINES,
            com.geoman.maplibre.geoman.GeomanConstants.SOURCE_POLYGONS,
            com.geoman.maplibre.geoman.GeomanConstants.SOURCE_CIRCLES
        )
        
        var removed = false
        for (sourceName in sourceNames) {
            val featureData = geoman.features.getFeature(sourceName, featureId)
            if (featureData != null) {
                // Remove from Geoman's internal feature store
                geoman.features.removeFeature(sourceName, featureId)
                removed = true
                Log.d("NarsGeoman", "Removed feature $featureId from Geoman source $sourceName")
                break
            }
        }
        
        // Also remove the MapLibre style layer and source if they exist
        val layerName = "nars_layer_$featureId"
        try {
            map.style?.getLayer(layerName)?.let { layer ->
                map.style?.removeLayer(layer)
                Log.d("NarsGeoman", "Removed layer $layerName")
            }
        } catch (e: Exception) {
            Log.w("NarsGeoman", "Error removing layer $layerName: ${e.message}")
        }
        
        // Remove outline layer if exists (polygons)
        try {
            map.style?.getLayer("${layerName}_outline")?.let { layer ->
                map.style?.removeLayer(layer)
                Log.d("NarsGeoman", "Removed outline layer ${layerName}_outline")
            }
        } catch (e: Exception) {
            Log.w("NarsGeoman", "Error removing outline layer: ${e.message}")
        }

        // Remove circle stroke layer if exists (circles use _stroke suffix)
        try {
            map.style?.getLayer("${layerName}_stroke")?.let { layer ->
                map.style?.removeLayer(layer)
                Log.d("NarsGeoman", "Removed circle stroke layer ${layerName}_stroke")
            }
        } catch (e: Exception) {
            Log.w("NarsGeoman", "Error removing circle stroke layer: ${e.message}")
        }

        // Remove edge source for polygons
        try {
            val edgeSourceName = "nars_${featureId}_edges"
            map.style?.removeSource(edgeSourceName)
            Log.d("NarsGeoman", "Removed edge source $edgeSourceName")
        } catch (e: Exception) {
            Log.w("NarsGeoman", "Error removing edge source: ${e.message}")
        }

        // Remove label layer if exists
        try {
            map.style?.getLayer("${layerName}_label")?.let { layer ->
                map.style?.removeLayer(layer)
                Log.d("NarsGeoman", "Removed label layer ${layerName}_label")
            }
        } catch (e: Exception) {
            Log.w("NarsGeoman", "Error removing label layer: ${e.message}")
        }

        // Remove vertex markers (up to 100 vertices max)
        for (i in 0 until 100) {
            val vertexLayerName = "nars_vertex_layer_${featureId}_$i"
            val vertexSourceName = "nars_vertex_${featureId}_$i"
            try {
                map.style?.getLayer(vertexLayerName)?.let { layer ->
                    map.style?.removeLayer(layer)
                    map.style?.removeSource(vertexSourceName)
                }
            } catch (e: Exception) {
                // Vertex doesn't exist, continue
                break
            }
        }
        
        // Remove the custom source
        val sourceName = "nars_$featureId"
        try {
            map.style?.removeSource(sourceName)
            Log.d("NarsGeoman", "Removed source $sourceName")
        } catch (e: Exception) {
            Log.w("NarsGeoman", "Error removing source $sourceName: ${e.message}")
        }
        
        if (!removed) {
            Log.w("NarsGeoman", "Feature $featureId not found in any Geoman source")
        }
        
        // Remove from tracking set
        addedFeatureIds.remove(featureId)
        Log.d("NarsGeoman", "Removed feature $featureId from tracking set (remaining: ${addedFeatureIds.size})")
    }
    
    /**
     * Clear all features from map and Geoman
     */
    fun clearAllFeatures() {
        Log.d("NarsGeoman", "Clearing all ${addedFeatureIds.size} tracked features")
        
        // Remove all tracked features from MapLibre
        val featuresToRemove = addedFeatureIds.toList()
        for (featureId in featuresToRemove) {
            removeFeature(featureId)
        }
        
        // Clear Geoman's internal feature store
        geoman.clearAllFeatures()
        
        // Clear tracking set
        addedFeatureIds.clear()
        Log.d("NarsGeoman", "All features cleared")
    }

    /**
     * Snap a point to nearby feature vertices/segments.
     * Searches all provided features within snapThreshold meters.
     */
    fun snapPoint(
        point: LatLng,
        features: List<NarsFeature>,
        snapThresholdMeters: Double = 20.0
    ): LatLng {
        var closestPoint = point
        var minDistance = snapThresholdMeters

        for (feature in features) {
            when (val geom = feature.geometry) {
                is com.nars.maplibre.data.model.PointGeometry -> {
                    val fp = LatLng(geom.coordinates[1], geom.coordinates[0])
                    val d = point.distanceTo(fp)
                    if (d < minDistance) { minDistance = d; closestPoint = fp }
                }
                is com.nars.maplibre.data.model.LineStringGeometry -> {
                    val coords = geom.coordinates.chunked(2)
                    for (i in 0 until coords.size - 1) {
                        val p1 = LatLng(coords[i][1], coords[i][0])
                        val p2 = LatLng(coords[i + 1][1], coords[i + 1][0])
                        val snapped = nearestPointOnSegment(point, p1, p2)
                        val d = point.distanceTo(snapped)
                        if (d < minDistance) { minDistance = d; closestPoint = snapped }
                    }
                    // Also check vertices directly
                    for (coord in coords) {
                        val vp = LatLng(coord[1], coord[0])
                        val d = point.distanceTo(vp)
                        if (d < minDistance) { minDistance = d; closestPoint = vp }
                    }
                }
                is com.nars.maplibre.data.model.PolygonGeometry -> {
                    val coords = geom.coordinates.chunked(2)
                    for (i in 0 until coords.size - 1) {
                        val p1 = LatLng(coords[i][1], coords[i][0])
                        val p2 = LatLng(coords[i + 1][1], coords[i + 1][0])
                        val snapped = nearestPointOnSegment(point, p1, p2)
                        val d = point.distanceTo(snapped)
                        if (d < minDistance) { minDistance = d; closestPoint = snapped }
                    }
                    for (coord in coords) {
                        val vp = LatLng(coord[1], coord[0])
                        val d = point.distanceTo(vp)
                        if (d < minDistance) { minDistance = d; closestPoint = vp }
                    }
                }
                is com.nars.maplibre.data.model.CircleGeometry -> {
                    val cp = LatLng(geom.coordinates[1], geom.coordinates[0])
                    val d = point.distanceTo(cp)
                    if (d < minDistance) { minDistance = d; closestPoint = cp }
                }
            }
        }

        if (minDistance < snapThresholdMeters) {
            Log.d("NarsGeoman", "Snapped point: ${point.latitude},${point.longitude} -> ${closestPoint.latitude},${closestPoint.longitude} (${minDistance.toInt()}m)")
        }
        return closestPoint
    }

    private fun nearestPointOnSegment(point: LatLng, p1: LatLng, p2: LatLng): LatLng {
        val dx = p2.longitude - p1.longitude
        val dy = p2.latitude - p1.latitude
        if (dx == 0.0 && dy == 0.0) return p1
        val t = ((point.longitude - p1.longitude) * dx + (point.latitude - p1.latitude) * dy) /
                (dx * dx + dy * dy)
        val clampedT = t.coerceIn(0.0, 1.0)
        return LatLng(
            p1.latitude + clampedT * dy,
            p1.longitude + clampedT * dx
        )
    }

    /**
     * Forward map click to Geoman for drawing/editing
     */
    fun onMapClick(latLng: LatLng) {
        Log.d("NarsGeoman", "onMapClick called, isDrawing=$_isDrawing, isEditing=$_isEditing")
        
        if (_isDrawing.value) {
            // Forward to current draw mode - Geoman handles the drawing
            val enabledModes = geoman.getEnabledModes()
            Log.d("NarsGeoman", "Enabled modes: $enabledModes")
            val drawMode = enabledModes.find { it.first == com.geoman.maplibre.geoman.types.ModeType.DRAW }
            drawMode?.let { (_, modeName) ->
                Log.d("NarsGeoman", "Forwarding click to draw mode: $modeName")
                geoman.handleDrawClick(modeName, latLng)
            } ?: run {
                Log.w("NarsGeoman", "No draw mode enabled!")
            }
        } else if (_isEditing.value) {
            // Forward to current edit mode
            val enabledModes = geoman.getEnabledModes()
            Log.d("NarsGeoman", "Enabled modes for edit: $enabledModes")
            val editMode = enabledModes.find { it.first == com.geoman.maplibre.geoman.types.ModeType.EDIT }
            editMode?.let { (_, modeName) ->
                Log.d("NarsGeoman", "Forwarding click to edit mode: $modeName")
                geoman.handleEditClick(modeName, latLng)
            } ?: run {
                Log.w("NarsGeoman", "No edit mode enabled!")
            }
        }
    }

    /**
     * Forward map long click to Geoman for finishing drawing
     */
    fun onMapLongClick(latLng: LatLng) {
        Log.d("NarsGeoman", "onMapLongClick called, isDrawing=$_isDrawing")
        
        if (_isDrawing.value) {
            // Forward to current draw mode to finish
            val enabledModes = geoman.getEnabledModes()
            val drawMode = enabledModes.find { it.first == com.geoman.maplibre.geoman.types.ModeType.DRAW }
            drawMode?.let { (_, modeName) ->
                Log.d("NarsGeoman", "Forwarding long click to draw mode: $modeName")
                geoman.handleDrawLongPress(modeName, latLng)
            }
        }
    }

    /**
     * Calculate distance between two points in meters
     */
    private fun calculateDistance(from: LngLat, to: LngLat): Double {
        // Simplified Haversine formula
        val earthRadius = 6371000.0 // meters
        val dLat = Math.toRadians(to.latitude - from.latitude)
        val dLon = Math.toRadians(to.longitude - from.longitude)
        val a = Math.sin(dLat / 2) * Math.sin(dLat / 2) +
                Math.cos(Math.toRadians(from.latitude)) * Math.cos(Math.toRadians(to.latitude)) *
                Math.sin(dLon / 2) * Math.sin(dLon / 2)
        val c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a))
        return earthRadius * c
    }
    
    /**
     * Get feature type from phase
     */
    private fun getFeatureTypeFromPhase(phase: PhaseDefinition): com.nars.maplibre.data.model.NarsFeatureType {
        return when (phase.key) {
            "areas" -> com.nars.maplibre.data.model.NarsFeatureType.URBAN_AREA
            "districts" -> com.nars.maplibre.data.model.NarsFeatureType.DISTRICT
            "cityCenter" -> com.nars.maplibre.data.model.NarsFeatureType.CITY_CENTER
            "roads" -> com.nars.maplibre.data.model.NarsFeatureType.ROAD
            "houseEntrances" -> com.nars.maplibre.data.model.NarsFeatureType.HOUSE_ENTRANCE
            "publicBuildings" -> com.nars.maplibre.data.model.NarsFeatureType.PUBLIC_BUILDING
            "publicSpaces" -> com.nars.maplibre.data.model.NarsFeatureType.PUBLIC_SPACE
            "namingPanels" -> com.nars.maplibre.data.model.NarsFeatureType.NAMING_PANEL
            else -> com.nars.maplibre.data.model.NarsFeatureType.URBAN_AREA
        }
    }
    
    /**
     * Get Geoman instance (for advanced usage)
     */
    fun getGeoman(): Geoman = geoman

    /**
     * Destroy and clean up
     */
    fun destroy() {
        stopDrawing()
        stopEditing()
        geoman.destroy()
    }
}
