package com.nars.maplibre.utils

import com.nars.maplibre.utils.NarsLogger
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.LineStringGeometry
import com.nars.maplibre.data.model.PointGeometry
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.sin
import kotlin.math.sqrt

/**
 * House numbering matching web version (house-numbering.ts)
 *
 * Algorithm:
 * 1. Filter main entrances assigned to a reference road
 * 2. Project each entrance onto the road polyline
 * 3. Sort by arc-distance along the road
 * 4. Assign odd numbers to one side, even to the other
 * 5. Continue from highest existing number on the road
 */
class HouseNumberingManager {

    companion object {
        private const val TAG = "HouseNumbering"
    }

    /**
     * Set house numbers for entrances along a reference road
     * @param entrances All house entrance features
     * @param road The reference road feature
     * @return Updated list of features with numbers assigned
     */
    fun setHouseNumbers(
        entrances: List<NarsFeature>,
        road: NarsFeature
    ): List<NarsFeature> {
        val roadGeom = road.geometry as? LineStringGeometry
        if (roadGeom == null) {
            NarsLogger.w(TAG, "Road has no geometry")
            return entrances
        }

        val roadCoords = roadGeom.coordinates.chunked(2)
        if (roadCoords.size < 2) {
            NarsLogger.w(TAG, "Road has insufficient coordinates")
            return entrances
        }

        // Filter main entrances on this road (compare dbId to road.dbId)
        val mainEntrances = entrances.filter { entrance ->
            entrance.properties.entranceTypeKey == "main_entrance" &&
            entrance.properties.roadDbId == road.dbId
        }

        if (mainEntrances.isEmpty()) {
            NarsLogger.d(TAG, "No main entrances found for road ${road.id}")
            return entrances
        }

        NarsLogger.d(TAG, "Numbering ${mainEntrances.size} entrances for road ${road.id}")

        // Project each entrance onto road and calculate arc distance
        val projectedEntrances = mainEntrances.mapNotNull { entrance ->
            val entranceGeom = entrance.geometry as? PointGeometry ?: return@mapNotNull null
            val coords = entranceGeom.coordinates
            if (coords.size < 2) return@mapNotNull null

            val entranceLng = coords[0]
            val entranceLat = coords[1]

            val arcDistance = projectOntoPolyline(entranceLat, entranceLng, roadCoords)

            // Determine which side of the road (left/right)
            val side = getSideOfRoad(entranceLat, entranceLng, roadCoords)

            Triple(entrance, arcDistance, side)
        }.sortedBy { it.second }

        // Find existing numbers on this road to continue from
        val existingNumbers = entrances
            .filter { it.properties.entranceTypeKey == "main_entrance" && it.properties.roadDbId == road.dbId }
            .mapNotNull { it.properties.entranceNumber }
            .filter { it > 0 }

        var oddNext = if (existingNumbers.isEmpty()) {
            1
        } else {
            existingNumbers.filter { it % 2 == 1 }.maxOrNull()?.plus(2) ?: 1
        }
        var evenNext = if (existingNumbers.isEmpty()) {
            2
        } else {
            existingNumbers.filter { it % 2 == 0 }.maxOrNull()?.plus(2) ?: 2
        }

        // Assign numbers alternating between sides
        val updatedEntrances = projectedEntrances.mapIndexed { index, (entrance, _, side) ->
            val number = if (side == "left") {
                // Alternate between odd/even based on position in list
                if (index % 2 == 0) {
                    val n = oddNext
                    oddNext += 2
                    n
                } else {
                    val n = evenNext
                    evenNext += 2
                    n
                }
            } else {
                // Right side gets opposite numbers
                if (index % 2 == 0) {
                    val n = evenNext
                    evenNext += 2
                    n
                } else {
                    val n = oddNext
                    oddNext += 2
                    n
                }
            }

            entrance.copy(
                properties = entrance.properties.copy(
                    entranceNumber = number,
                    side = side
                )
            )
        }

        // Return updated list (replacing numbered entrances)
        val result = entrances.map { entrance ->
            val updated = updatedEntrances.find { it.id == entrance.id }
            updated ?: entrance
        }

        NarsLogger.d(TAG, "Assigned numbers: oddNext=$oddNext, evenNext=$evenNext")
        return result
    }

    /**
     * Project point onto polyline and return arc distance from start
     */
    private fun projectOntoPolyline(
        lat: Double,
        lng: Double,
        polyline: List<List<Double>>
    ): Double {
        var minDistance = Double.MAX_VALUE
        var arcDistance = 0.0
        var cumulativeDistance = 0.0

        for (i in 0 until polyline.size - 1) {
            val p1 = polyline[i]
            val p2 = polyline[i + 1]

            val projected = nearestPointOnSegment(lat, lng, p1[1], p1[0], p2[1], p2[0])
            val dist = distance(lat, lng, projected.first, projected.second)

            if (dist < minDistance) {
                minDistance = dist
                arcDistance = cumulativeDistance + distance(p1[1], p1[0], projected.first, projected.second)
            }

            cumulativeDistance += distance(p1[1], p1[0], p2[1], p2[0])
        }

        return arcDistance
    }

    /**
     * Determine which side of the road a point is on
     * Uses cross product of road direction and point direction
     */
    private fun getSideOfRoad(
        pointLat: Double,
        pointLng: Double,
        roadCoords: List<List<Double>>
    ): String {
        if (roadCoords.size < 2) return "right"

        // Use middle of road for side calculation
        val midIndex = roadCoords.size / 2
        val p1 = roadCoords[midIndex - 1]
        val p2 = roadCoords.getOrNull(midIndex) ?: roadCoords.last()

        // Road direction vector
        val roadDx = p2[0] - p1[0]
        val roadDy = p2[1] - p1[1]

        // Point direction from road midpoint
        val pointDx = pointLng - ((p1[0] + p2[0]) / 2)
        val pointDy = pointLat - ((p1[1] + p2[1]) / 2)

        // Cross product (2D)
        val cross = roadDx * pointDy - roadDy * pointDx

        // Positive = right, negative = left (arbitrary convention)
        return if (cross > 0) "right" else "left"
    }

    /**
     * Find nearest point on a line segment
     */
    private fun nearestPointOnSegment(
        px: Double,
        py: Double,
        x1: Double,
        y1: Double,
        x2: Double,
        y2: Double
    ): Pair<Double, Double> {
        val dx = x2 - x1
        val dy = y2 - y1

        if (dx == 0.0 && dy == 0.0) {
            return x1 to y1
        }

        val t = ((px - x1) * dx + (py - y1) * dy) / (dx * dx + dy * dy)
        val clampedT = t.coerceIn(0.0, 1.0)

        return (x1 + clampedT * dx) to (y1 + clampedT * dy)
    }

    /**
     * Calculate distance between two points in meters
     */
    private fun distance(lat1: Double, lng1: Double, lat2: Double, lng2: Double): Double {
        val r = 6371000.0
        val dLat = Math.toRadians(lat2 - lat1)
        val dLng = Math.toRadians(lng2 - lng1)
        val a = sin(dLat / 2) * sin(dLat / 2) +
                cos(Math.toRadians(lat1)) * cos(Math.toRadians(lat2)) *
                sin(dLng / 2) * sin(dLng / 2)
        val c = 2 * atan2(sqrt(a), sqrt(1 - a))
        return r * c
    }
}