package com.nars.maplibre.utils

import android.util.Log
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.NarsFeatureType
import com.nars.maplibre.data.model.PointGeometry
import com.nars.maplibre.data.model.PolygonGeometry
import com.nars.maplibre.data.model.LineStringGeometry
import java.util.UUID
import kotlin.math.cos
import kotlin.math.sin
import kotlin.math.sqrt

/**
 * Naming panel generation matching web version (naming-panels.ts)
 *
 * Generates labels for:
 * - Districts: every vertex
 * - Roads: start, end, every 100m along polyline
 * - Public buildings: first vertex
 * - Public spaces: first vertex
 */
class NamingPanelGenerator {

    companion object {
        private const val TAG = "NamingPanelGenerator"
        private const val ROAD_STEP_METERS = 100.0
        private const val DEDUPE_METERS = 3.0

        // Colors matching web version
        private const val COLOR_DISTRICTS = "#f39c12"
        private const val COLOR_ROADS = "#3498db"
        private const val COLOR_PUBLIC_BUILDINGS = "#e67e22"
        private const val COLOR_PUBLIC_SPACES = "#2ecc71"
    }

    /**
     * Generate all naming panels for the current dataset
     * @return List of new NarsFeature (naming panels)
     */
    fun generatePanels(
        districts: List<NarsFeature>,
        roads: List<NarsFeature>,
        publicBuildings: List<NarsFeature>,
        publicSpaces: List<NarsFeature>
    ): List<NarsFeature> {
        Log.d(TAG, "Generating panels for ${districts.size} districts, ${roads.size} roads, ${publicBuildings.size} buildings, ${publicSpaces.size} spaces")

        val panels = mutableListOf<NarsFeature>()
        val seenPositions = mutableListOf<Pair<Double, Double>>() // For deduplication

        // Districts: every vertex
        for ((idx, district) in districts.withIndex()) {
            val geom = district.geometry as? PolygonGeometry ?: continue
            val coords = geom.coordinates.chunked(2)

            for ((vIdx, coord) in coords.withIndex()) {
                val lng = coord[0]
                val lat = coord[1]

                if (isDuplicate(lat, lng, seenPositions)) continue

                val label = district.properties.name ?: "District ${idx + 1}"
                panels.add(createPanel(label, lat, lng, COLOR_DISTRICTS))
                seenPositions.add(lat to lng)
            }
        }

        // Roads: start, end, every 100m
        for ((idx, road) in roads.withIndex()) {
            val geom = road.geometry as? LineStringGeometry ?: continue
            val stations = getRoadStations(geom.coordinates)

            for ((sIdx, station) in stations.withIndex()) {
                val (lng, lat) = station

                if (isDuplicate(lat, lng, seenPositions)) continue

                val label = road.properties.name ?: "Road ${idx + 1}"
                // Add suffix for intermediate stations
                val fullLabel = if (sIdx > 0 && sIdx < stations.size - 1) {
                    "${label} (${sIdx * 100}m)"
                } else {
                    label
                }
                panels.add(createPanel(fullLabel, lat, lng, COLOR_ROADS))
                seenPositions.add(lat to lng)
            }
        }

        // Public buildings: first vertex
        for (building in publicBuildings) {
            val geom = building.geometry as? PolygonGeometry ?: continue
            val coords = geom.coordinates.chunked(2)
            if (coords.isEmpty()) continue

            val first = coords.first()
            val lng = first[0]
            val lat = first[1]

            if (isDuplicate(lat, lng, seenPositions)) continue

            val label = building.properties.name ?: "Building"
            panels.add(createPanel(label, lat, lng, COLOR_PUBLIC_BUILDINGS))
            seenPositions.add(lat to lng)
        }

        // Public spaces: first vertex
        for (space in publicSpaces) {
            val geom = space.geometry as? PolygonGeometry ?: continue
            val coords = geom.coordinates.chunked(2)
            if (coords.isEmpty()) continue

            val first = coords.first()
            val lng = first[0]
            val lat = first[1]

            if (isDuplicate(lat, lng, seenPositions)) continue

            val label = space.properties.name ?: "Space"
            panels.add(createPanel(label, lat, lng, COLOR_PUBLIC_SPACES))
            seenPositions.add(lat to lng)
        }

        Log.d(TAG, "Generated ${panels.size} naming panels")
        return panels
    }

    /**
     * Get road stations (points along road at regular intervals)
     */
    private fun getRoadStations(coordinates: List<Double>): List<Pair<Double, Double>> {
        if (coordinates.size < 4) return emptyList()

        val result = mutableListOf<Pair<Double, Double>>()

        // Add start point
        result.add(coordinates[0] to coordinates[1])

        // Calculate total distance and add stations
        var accumulatedDistance = 0.0
        var nextStationDistance = ROAD_STEP_METERS

        for (i in 0 until coordinates.size - 2 step 2) {
            val lng1 = coordinates[i]
            val lat1 = coordinates[i + 1]
            val lng2 = coordinates[i + 2]
            val lat2 = coordinates[i + 3]

            val segmentDist = distance(lat1, lng1, lat2, lng2)

            // Check if we need to add stations on this segment
            while (accumulatedDistance + segmentDist >= nextStationDistance) {
                // Interpolate point at nextStationDistance
                val remaining = nextStationDistance - accumulatedDistance
                val t = if (segmentDist > 0) remaining / segmentDist else 0.0

                val lng = lng1 + (lng2 - lng1) * t
                val lat = lat1 + (lat2 - lat1) * t

                result.add(lng to lat)
                nextStationDistance += ROAD_STEP_METERS
            }

            accumulatedDistance += segmentDist
        }

        // Add end point if not already added
        val endLng = coordinates[coordinates.size - 2]
        val endLat = coordinates[coordinates.size - 1]
        if (result.isEmpty() || result.last() != (endLng to endLat)) {
            result.add(endLng to endLat)
        }

        return result
    }

    /**
     * Check if position is a duplicate of an existing position
     */
    private fun isDuplicate(lat: Double, lng: Double, positions: List<Pair<Double, Double>>): Boolean {
        for ((existingLat, existingLng) in positions) {
            if (distance(lat, lng, existingLat, existingLng) < DEDUPE_METERS) {
                return true
            }
        }
        return false
    }

    /**
     * Create a naming panel feature
     */
    private fun createPanel(label: String, lat: Double, lng: Double, color: String): NarsFeature {
        return NarsFeature(
            id = "panel_${UUID.randomUUID()}",
            type = NarsFeatureType.NAMING_PANEL,
            geometry = PointGeometry(
                type = "Point",
                coordinates = listOf(lng, lat)
            ),
            properties = com.nars.maplibre.data.model.FeatureProperties(
                phase = "namingPanels",
                name = label,
                color = color,
                decisionNumber = "",
                decisionDate = ""
            )
        )
    }

    /**
     * Calculate distance between two points in meters (Haversine formula)
     */
    private fun distance(lat1: Double, lng1: Double, lat2: Double, lng2: Double): Double {
        val r = 6371000.0 // Earth radius in meters
        val dLat = Math.toRadians(lat2 - lat1)
        val dLng = Math.toRadians(lng2 - lng1)
        val a = sin(dLat / 2) * sin(dLat / 2) +
                cos(Math.toRadians(lat1)) * cos(Math.toRadians(lat2)) *
                sin(dLng / 2) * sin(dLng / 2)
        val c = 2 * kotlin.math.atan2(sqrt(a), sqrt(1 - a))
        return r * c
    }
}