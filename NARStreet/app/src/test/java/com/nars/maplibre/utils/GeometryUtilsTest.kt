package com.nars.maplibre.utils

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import org.maplibre.android.geometry.LatLng

class GeometryUtilsTest {

    @Test
    fun `calculateDistance returns zero for same point`() {
        val point = LatLng(36.753768, 3.058756)
        val distance = GeometryUtils.calculateDistance(point, point)
        assertEquals(0.0, distance, 0.001)
    }

    @Test
    fun `calculateDistance returns correct distance between two points`() {
        val from = LatLng(36.753768, 3.058756)
        val to = LatLng(36.753817, 3.058792)
        val distance = GeometryUtils.calculateDistance(from, to)
        assertTrue(distance > 0)
    }

    @Test
    fun `calculateCentroid returns origin for empty list`() {
        val centroid = GeometryUtils.calculateCentroid(emptyList())
        assertEquals(0.0, centroid.latitude, 0.001)
        assertEquals(0.0, centroid.longitude, 0.001)
    }

    @Test
    fun `calculateCentroid returns correct centroid for points`() {
        val points = listOf(
            LatLng(36.0, 3.0),
            LatLng(38.0, 5.0)
        )
        val centroid = GeometryUtils.calculateCentroid(points)
        assertEquals(37.0, centroid.latitude, 0.001)
        assertEquals(4.0, centroid.longitude, 0.001)
    }

    @Test
    fun `isPointInPolygon returns true for point inside triangle`() {
        val point = LatLng(36.0, 3.0)
        val polygon = listOf(
            LatLng(35.0, 2.0),
            LatLng(37.0, 2.0),
            LatLng(36.0, 4.0)
        )
        assertTrue(GeometryUtils.isPointInPolygon(point, polygon))
    }

    @Test
    fun `isPointInPolygon returns false for point outside triangle`() {
        val point = LatLng(38.0, 5.0)
        val polygon = listOf(
            LatLng(35.0, 2.0),
            LatLng(37.0, 2.0),
            LatLng(36.0, 4.0)
        )
        assertFalse(GeometryUtils.isPointInPolygon(point, polygon))
    }

    @Test
    fun `simplifyLine returns same list for two or fewer points`() {
        val points = listOf(
            LatLng(36.0, 3.0),
            LatLng(38.0, 5.0)
        )
        val simplified = GeometryUtils.simplifyLine(points, 0.1)
        assertEquals(2, simplified.size)
    }

    @Test
    fun `calculateBoundingBox returns zero for empty list`() {
        val bbox = GeometryUtils.calculateBoundingBox(emptyList())
        assertEquals(0.0, bbox.minLat, 0.001)
    }

    @Test
    fun `calculateBoundingBox returns correct bounds`() {
        val points = listOf(
            LatLng(36.0, 3.0),
            LatLng(38.0, 5.0)
        )
        val bbox = GeometryUtils.calculateBoundingBox(points)
        assertEquals(36.0, bbox.minLat, 0.001)
        assertEquals(38.0, bbox.maxLat, 0.001)
        assertEquals(3.0, bbox.minLon, 0.001)
        assertEquals(5.0, bbox.maxLon, 0.001)
    }

    @Test
    fun `pointsEqual returns true for same point within tolerance`() {
        val a = LatLng(36.753768, 3.058756)
        val b = LatLng(36.753769, 3.058757)
        assertTrue(GeometryUtils.pointsEqual(a, b, 0.00001))
    }

    @Test
    fun `pointsEqual returns false for different points beyond tolerance`() {
        val a = LatLng(36.753768, 3.058756)
        val b = LatLng(36.753800, 3.058800)
        assertFalse(GeometryUtils.pointsEqual(a, b, 0.000001))
    }
}
