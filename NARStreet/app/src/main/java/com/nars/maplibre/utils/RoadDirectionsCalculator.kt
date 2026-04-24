package com.nars.maplibre.utils

import android.util.Log
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.LineStringGeometry
import com.nars.maplibre.data.model.PointGeometry
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.sin
import kotlin.math.sqrt

/**
 * Road directions computation matching web version (road-directions.ts)
 *
 * Algorithm:
 * 1. Build connection graph from road segments at intersections
 * 2. Orient roads using BFS from city center outward
 * 3. Geographic fallback when no city center exists
 * 4. Dead-end correction for roads with degree=1 nodes
 * 5. Vote per road (majority of segments)
 *
 * Reference: nars-vite-maplibre/src/map/road-directions.ts
 */
class RoadDirectionsCalculator {

    companion object {
        private const val TAG = "RoadDirections"
        private const val CONNECT_METERS = 50.0 // Connection threshold
    }

    /**
     * Road segment data structure
     */
    data class RoadSegment(
        val id: String,
        val coordinates: List<Double>, // Flat [lng,lat,lng,lat,...]
        val dbId: String,
        val entry: NarsFeature,
        var reversed: Boolean = false
    )

    /**
     * Graph node representing an intersection
     */
    data class Node(
        val key: String, // "lat,lng"
        val lat: Double,
        val lng: Double,
        val connectedSegmentIds: MutableSet<String> = mutableSetOf()
    )

    /**
     * Result of road direction computation
     */
    data class RoadDirectionsResult(
        val reversedRoadIds: List<String>,
        val message: String
    )

    /**
     * Compute and apply road directions (field mode - no cityCenter)
     * @param roads List of road features
     * @return Result with list of reversed road IDs
     */
    fun computeDirectionsFromRoads(
        roads: List<NarsFeature>
    ): RoadDirectionsResult {
        return computeDirections(roads, null)
    }

    /**
     * Compute and apply road directions
     * @param roads List of road features
     * @param cityCenter City center feature (optional)
     * @return Result with list of reversed road IDs
     */
    fun computeDirections(
        roads: List<NarsFeature>,
        cityCenter: NarsFeature?
    ): RoadDirectionsResult {
        if (roads.isEmpty()) {
            return RoadDirectionsResult(emptyList(), "No roads to process")
        }

        Log.d(TAG, "Computing directions for ${roads.size} roads")

        // Phase 1: Build connection graph
        val (graph, segments) = buildConnectionGraph(roads)
        Log.d(TAG, "Built graph with ${graph.size} nodes and ${segments.size} segments")

        // Phase 2: Orient roads from city center or geographically
        if (cityCenter != null) {
            orientFromCityCenter(cityCenter, graph, segments)
            Log.d(TAG, "Oriented from city center")
        } else {
            orientGeographic(segments)
            Log.d(TAG, "Oriented geographically (west to east)")
        }

        // Phase 3: Vote per road (majority of segments)
        val votes = computeVotes(segments)
        Log.d(TAG, "Votes computed: ${votes.size} roads")

        // Phase 4: Dead-end correction
        val corrected = deadEndCorrection(votes, graph, roads)
        Log.d(TAG, "Dead-end correction: ${corrected.size} roads to reverse")

        // Phase 5: Generate result (reversed road IDs)
        return RoadDirectionsResult(corrected, "Directions applied to ${corrected.size} roads")
    }

    /**
     * Build connection graph from roads
     * Nodes are road endpoints that are within CONNECT_METERS of each other
     */
    private fun buildConnectionGraph(roads: List<NarsFeature>): Pair<MutableMap<String, Node>, Map<String, RoadSegment>> {
        val graph = mutableMapOf<String, Node>()
        val segments = mutableMapOf<String, RoadSegment>()

        for (road in roads) {
            val geom = road.geometry as? LineStringGeometry ?: continue
            val coords = geom.coordinates
            if (coords.size < 4) continue // Need at least 2 points

            // Create segment for this road
            val segmentId = "seg_${road.id}"
            segments[segmentId] = RoadSegment(
                id = segmentId,
                coordinates = coords,
                dbId = road.id,
                entry = road
            )

            // Add endpoints as nodes
            val startLng = coords[0]
            val startLat = coords[1]
            val endLng = coords[coords.size - 2]
            val endLat = coords[coords.size - 1]

            val startKey = "${startLat},${startLng}"
            val endKey = "${endLat},${endLng}"

            // Get or create start node
            val startNode = graph.getOrPut(startKey) {
                Node(startKey, startLat, startLng)
            }
            startNode.connectedSegmentIds.add(segmentId)

            // Get or create end node
            val endNode = graph.getOrPut(endKey) {
                Node(endKey, endLat, endLng)
            }
            endNode.connectedSegmentIds.add(segmentId)

            // Check for intersections - connect nearby nodes
            // This is a simplified version - full version would check all segment endpoints
            for ((otherKey, otherNode) in graph) {
                if (otherKey == startKey || otherKey == endKey) continue

                val dist = distance(startLat, startLng, otherNode.lat, otherNode.lng)
                if (dist < CONNECT_METERS) {
                    // Connect the nodes in graph
                    startNode.connectedSegmentIds.addAll(otherNode.connectedSegmentIds)
                }

                val dist2 = distance(endLat, endLng, otherNode.lat, otherNode.lng)
                if (dist2 < CONNECT_METERS) {
                    endNode.connectedSegmentIds.addAll(otherNode.connectedSegmentIds)
                }
            }
        }

        return graph to segments
    }

    /**
     * Orient roads using BFS from city center
     * Roads closer to city center flow OUTWARD (toward edges)
     */
    private fun orientFromCityCenter(
        cityCenter: NarsFeature,
        graph: MutableMap<String, Node>,
        segments: Map<String, RoadSegment>
    ) {
        val centerGeom = cityCenter.geometry as? PointGeometry ?: return
        val centerLng = centerGeom.coordinates.getOrNull(0) ?: return
        val centerLat = centerGeom.coordinates.getOrNull(1) ?: return

        // Find nodes within city center radius (or nearby)
        val centerKey = "${centerLat},${centerLng}"

        // BFS from city center
        val visited = mutableSetOf<String>()
        val queue = ArrayDeque<String>()

        // Get city center radius from geometry (coordinates[2] = radius for CircleGeometry)
        val cityCenterRadius = when (val geom = cityCenter.geometry) {
            is com.nars.maplibre.data.model.CircleGeometry -> geom.coordinates.getOrNull(2) ?: 100.0
            else -> 100.0
        }

        // Start with city center node or nearby nodes
        val startNodes = graph.filter { (_, node) ->
            distance(centerLat, centerLng, node.lat, node.lng) < cityCenterRadius * 2
        }.keys.toList()

        if (startNodes.isEmpty()) {
            // Fallback: use all nodes sorted by distance
            val sorted = graph.keys.sortedBy { key ->
                val parts = key.split(",")
                val lat = parts[0].toDoubleOrNull() ?: 0.0
                val lng = parts[1].toDoubleOrNull() ?: 0.0
                distance(centerLat, centerLng, lat, lng)
            }.take(1)
            queue.addAll(sorted)
        } else {
            queue.addAll(startNodes)
        }

        while (queue.isNotEmpty()) {
            val nodeKey = queue.removeFirst()
            if (nodeKey in visited) continue
            visited.add(nodeKey)

            val node = graph[nodeKey] ?: continue

            // Mark all connected segments as oriented
            for (segmentId in node.connectedSegmentIds) {
                val segment = segments[segmentId] ?: continue

                // Calculate direction: from this node toward others
                // This is simplified - full version would calculate arc distance from center
                val segmentDir = calculateDirectionFromCenter(
                    segment.coordinates,
                    centerLat,
                    centerLng
                )
                segment.reversed = !segmentDir
            }

            // Add connected nodes to queue
            for (otherKey in graph.keys) {
                if (otherKey in visited) continue

                val otherNode = graph[otherKey] ?: continue
                if (distance(node.lat, node.lng, otherNode.lat, otherNode.lng) < CONNECT_METERS) {
                    queue.add(otherKey)
                }
            }
        }
    }

    /**
     * Orient roads geographically (west to east)
     */
    private fun orientGeographic(segments: Map<String, RoadSegment>) {
        // Calculate average longitude of each road
        // If start is west of end, keep direction; otherwise reverse
        for (segment in segments.values) {
            val coords = segment.coordinates
            if (coords.size < 4) continue

            val startLng = coords[0]
            val endLng = coords[coords.size - 2]

            // Reverse if start is east of end (flow west to east)
            segment.reversed = startLng > endLng
        }
    }

    /**
     * Calculate direction from center (true = flow away from center)
     */
    private fun calculateDirectionFromCenter(coords: List<Double>, centerLat: Double, centerLng: Double): Boolean {
        if (coords.size < 4) return true

        val startLng = coords[0]
        val startLat = coords[1]
        val endLng = coords[coords.size - 2]
        val endLat = coords[coords.size - 1]

        val startDist = distance(centerLat, centerLng, startLat, startLng)
        val endDist = distance(centerLat, centerLng, endLat, endLng)

        // Flow from closer to farther (away from center)
        return startDist < endDist
    }

    /**
     * Compute votes per road (majority of segments)
     */
    private fun computeVotes(segments: Map<String, RoadSegment>): Map<String, Boolean> {
        val roadVotes = mutableMapOf<String, MutableList<Boolean>>()

        for (segment in segments.values) {
            val votes = roadVotes.getOrPut(segment.dbId) { mutableListOf() }
            votes.add(!segment.reversed) // true = forward, false = reversed
        }

        // Majority vote
        return roadVotes.mapValues { (_, votes) ->
            val forward = votes.count { it }
            val reverse = votes.size - forward
            forward > reverse // Return true if forward should win
        }
    }

    /**
     * Dead-end correction for roads with degree=1 nodes
     */
    private fun deadEndCorrection(
        votes: Map<String, Boolean>,
        graph: Map<String, Node>,
        roads: List<NarsFeature>
    ): List<String> {
        val reversed = mutableListOf<String>()

        for (road in roads) {
            val geom = road.geometry as? LineStringGeometry ?: continue
            val coords = geom.coordinates
            if (coords.size < 4) continue

            val startKey = "${coords[1]},${coords[0]}"
            val endKey = "${coords[coords.size - 1]},${coords[coords.size - 2]}"

            val startDegree = graph[startKey]?.connectedSegmentIds?.size ?: 0
            val endDegree = graph[endKey]?.connectedSegmentIds?.size ?: 0

            if (startDegree == 1 && endDegree > 1) {
                // Dead end on start - should flow from end (connected side)
                // Current vote says: vote = true (forward), so reverse
                if (votes[road.id] == true) {
                    reversed.add(road.id)
                }
            } else if (endDegree == 1 && startDegree > 1) {
                // Dead end on end - should flow from start (connected side)
                if (votes[road.id] == true) {
                    reversed.add(road.id)
                }
            } else if (startDegree == 1 && endDegree == 1) {
                // Both ends are dead ends - use distance to center
                // (simplified - just use vote)
            }
        }

        return reversed
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
        val c = 2 * atan2(sqrt(a), sqrt(1 - a))
        return r * c
    }

    /**
     * Reverse a road's coordinates
     */
    fun reverseRoadCoordinates(coordinates: List<Double>): List<Double> {
        return coordinates.reversed()
    }

    /**
     * Get bearing from start to end point
     */
    fun getBearing(startLng: Double, startLat: Double, endLng: Double, endLat: Double): Double {
        val dLng = Math.toRadians(endLng - startLng)
        val y = sin(dLng)
        val x = cos(Math.toRadians(startLat)) * tan(Math.toRadians(endLat)) -
                sin(Math.toRadians(startLat)) * cos(dLng)
        val bearing = Math.toDegrees(atan2(y, x))
        return (bearing + 360) % 360
    }

    private fun tan(angle: Double): Double = sin(angle) / cos(angle)
}