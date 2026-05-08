package com.nars.maplibre.modes

import com.nars.maplibre.data.model.CircleGeometry
import com.nars.maplibre.data.model.LineStringGeometry
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.PointGeometry
import com.nars.maplibre.data.model.PolygonGeometry
import com.nars.maplibre.utils.NarsLogger
import org.maplibre.android.geometry.LatLng

class SnappingEngine {
    companion object {
        private const val TAG = "SnappingEngine"
        private const val DEFAULT_SNAP_THRESHOLD_METERS = 20.0
    }

    fun snapPoint(
        point: LatLng,
        features: List<NarsFeature>,
        snapThresholdMeters: Double = DEFAULT_SNAP_THRESHOLD_METERS
    ): LatLng {
        var closestPoint = point
        var minDistance = snapThresholdMeters

        for (feature in features) {
            when (val geom = feature.geometry) {
                is PointGeometry -> {
                    val fp = LatLng(geom.coordinates[1], geom.coordinates[0])
                    val d = point.distanceTo(fp)
                    if (d < minDistance) { minDistance = d; closestPoint = fp }
                }
                is LineStringGeometry -> {
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
                is PolygonGeometry -> {
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
                is CircleGeometry -> {
                    val cp = LatLng(geom.coordinates[1], geom.coordinates[0])
                    val d = point.distanceTo(cp)
                    if (d < minDistance) { minDistance = d; closestPoint = cp }
                }
            }
        }

        if (minDistance < snapThresholdMeters) {
            NarsLogger.d(TAG, "Snapped point: ${point.latitude},${point.longitude} -> ${closestPoint.latitude},${closestPoint.longitude} (${minDistance.toInt()}m)")
        }
        return closestPoint
    }

    fun nearestPointOnSegment(point: LatLng, p1: LatLng, p2: LatLng): LatLng {
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
}
