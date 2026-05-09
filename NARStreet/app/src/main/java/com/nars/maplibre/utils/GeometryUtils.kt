package com.nars.maplibre.utils

import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.sin
import kotlin.math.sqrt
import org.maplibre.android.geometry.LatLng

/**
 * Geometry utility functions
 */
object GeometryUtils {

    private const val EARTH_RADIUS_METERS = 6371000.0

    /**
     * Calculate Haversine distance between two points in meters using raw coordinates
     */
    fun haversineDistance(lat1: Double, lng1: Double, lat2: Double, lng2: Double): Double {
        val dLat = Math.toRadians(lat2 - lat1)
        val dLng = Math.toRadians(lng2 - lng1)
        val a = sin(dLat / 2) * sin(dLat / 2) +
                cos(Math.toRadians(lat1)) * cos(Math.toRadians(lat2)) *
                sin(dLng / 2) * sin(dLng / 2)
        val c = 2 * atan2(sqrt(a), sqrt(1 - a))
        return EARTH_RADIUS_METERS * c
    }

    /**
     * Calculate distance between two LatLng points in meters using Haversine formula
     */
    fun calculateDistance(from: LatLng, to: LatLng): Double {
        return haversineDistance(from.latitude, from.longitude, to.latitude, to.longitude)
    }
    
    /**
     * Calculate the centroid of a list of points
     */
    fun calculateCentroid(points: List<LatLng>): LatLng {
        if (points.isEmpty()) return LatLng(0.0, 0.0)
        
        var sumLat = 0.0
        var sumLon = 0.0
        
        points.forEach { point ->
            sumLat += point.latitude
            sumLon += point.longitude
        }
        
        return LatLng(sumLat / points.size, sumLon / points.size)
    }
    
    /**
     * Check if a point is within a polygon (Ray casting algorithm)
     */
    fun isPointInPolygon(point: LatLng, polygon: List<LatLng>): Boolean {
        var inside = false
        val x = point.longitude
        val y = point.latitude
        
        var j = polygon.size - 1
        for (i in polygon.indices) {
            val xi = polygon[i].longitude
            val yi = polygon[i].latitude
            val xj = polygon[j].longitude
            val yj = polygon[j].latitude
            
            val intersect = ((yi > y) != (yj > y)) &&
                    (x < (xj - xi) * (y - yi) / (yj - yi) + xi)
            
            if (intersect) inside = !inside
            j = i
        }
        
        return inside
    }
    
    /**
     * Simplify a line using Douglas-Peucker algorithm
     */
    fun simplifyLine(points: List<LatLng>, tolerance: Double): List<LatLng> {
        if (points.size <= 2) return points
        
        // Find the point with the maximum distance
        var dMax = 0.0
        var index = 0
        val end = points.size - 1
        
        for (i in 1 until end) {
            val d = perpendicularDistance(points[i], points[0], points[end])
            if (d > dMax) {
                index = i
                dMax = d
            }
        }
        
        // If max distance is greater than tolerance, recursively simplify
        return if (dMax > tolerance) {
            val recResults1 = simplifyLine(points.subList(0, index + 1), tolerance)
            val recResults2 = simplifyLine(points.subList(index, end + 1), tolerance)
            
            recResults1.dropLast(1) + recResults2
        } else {
            listOf(points[0], points[end])
        }
    }
    
    /**
     * Calculate perpendicular distance from point to line
     */
    private fun perpendicularDistance(point: LatLng, lineStart: LatLng, lineEnd: LatLng): Double {
        val dx = lineEnd.longitude - lineStart.longitude
        val dy = lineEnd.latitude - lineStart.latitude
        
        if (dx == 0.0 && dy == 0.0) {
            return calculateDistance(point, lineStart)
        }
        
        val u = ((point.longitude - lineStart.longitude) * dx +
                (point.latitude - lineStart.latitude) * dy) / (dx * dx + dy * dy)
        
        val closestX = when {
            u < 0 -> lineStart.longitude
            u > 1 -> lineEnd.longitude
            else -> lineStart.longitude + u * dx
        }
        val closestY = when {
            u < 0 -> lineStart.latitude
            u > 1 -> lineEnd.latitude
            else -> lineStart.latitude + u * dy
        }
        
        return calculateDistance(point, LatLng(closestY, closestX))
    }
    
    /**
     * Calculate the bounding box of a list of points
     */
    fun calculateBoundingBox(points: List<LatLng>): BoundingBox {
        if (points.isEmpty()) {
            return BoundingBox(0.0, 0.0, 0.0, 0.0)
        }
        
        var minLat = points[0].latitude
        var maxLat = points[0].latitude
        var minLon = points[0].longitude
        var maxLon = points[0].longitude
        
        points.forEach { point ->
            minLat = minOf(minLat, point.latitude)
            maxLat = maxOf(maxLat, point.latitude)
            minLon = minOf(minLon, point.longitude)
            maxLon = maxOf(maxLon, point.longitude)
        }
        
        return BoundingBox(minLat, maxLat, minLon, maxLon)
    }
    
    /**
     * Check if two points are equal within a tolerance
     */
    fun pointsEqual(a: LatLng, b: LatLng, tolerance: Double = 0.000001): Boolean {
        return Math.abs(a.latitude - b.latitude) < tolerance &&
                Math.abs(a.longitude - b.longitude) < tolerance
    }
}

/**
 * Bounding box data class
 */
data class BoundingBox(
    val minLat: Double,
    val maxLat: Double,
    val minLon: Double,
    val maxLon: Double
) {
    fun contains(point: LatLng): Boolean {
        return point.latitude in minLat..maxLat &&
                point.longitude in minLon..maxLon
    }
    
    fun center(): LatLng {
        return LatLng((minLat + maxLat) / 2, (minLon + maxLon) / 2)
    }
}
