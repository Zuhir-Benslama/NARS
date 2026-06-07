package com.nars.maplibre.modes

import com.nars.maplibre.data.model.FeatureProperties
import com.nars.maplibre.data.model.LineStringGeometry
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.NarsFeatureType
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.data.model.PointGeometry
import com.nars.maplibre.data.model.PolygonGeometry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

class GeometryConverterTest {

    private val converter = GeometryConverter()

    @Test
    fun `convertToGeoJson creates feature with correct id`() {
        val feature = NarsFeature(
            id = "test-1",
            type = NarsFeatureType.ROAD,
            geometry = PointGeometry(coordinates = listOf(3.0, 36.0)),
            properties = FeatureProperties.RoadProperties()
        )

        val geoJson = converter.convertToGeoJson(feature)

        assertEquals("test-1", geoJson.id)
        assertNotNull(geoJson.geometry)
    }

    @Test
    fun `convertToGeoJson preserves properties map`() {
        val feature = NarsFeature(
            id = "test-1",
            type = NarsFeatureType.ROAD,
            geometry = PointGeometry(coordinates = listOf(3.0, 36.0)),
            properties = FeatureProperties.RoadProperties(name = "Test Road")
        )

        val geoJson = converter.convertToGeoJson(feature)

        assertEquals("road", geoJson.properties["type"])
        assertEquals("Test Road", geoJson.properties["name"])
        assertEquals(Phases.ROADS_KEY, geoJson.properties["phase"])
    }

    @Test
    fun `convertGeometryToGeoJson handles PointGeometry`() {
        val narsGeometry = PointGeometry(coordinates = listOf(3.0, 36.0))
        val geoJson = converter.convertGeometryToGeoJson(narsGeometry)

        assertEquals("Point", geoJson.type)
    }

    @Test
    fun `convertGeometryToGeoJson handles LineStringGeometry`() {
        val narsGeometry = LineStringGeometry(coordinates = listOf(3.0, 36.0, 3.01, 36.01))
        val geoJson = converter.convertGeometryToGeoJson(narsGeometry)

        assertEquals("LineString", geoJson.type)
    }

    @Test
    fun `convertGeometryToGeoJson handles PolygonGeometry`() {
        val narsGeometry = PolygonGeometry(coordinates = listOf(3.0, 36.0, 3.01, 36.0, 3.005, 36.01, 3.0, 36.0))
        val geoJson = converter.convertGeometryToGeoJson(narsGeometry)

        assertEquals("Polygon", geoJson.type)
    }

    @Test
    fun `getSourceNameForGeometry returns correct source for Point`() {
        val sourceName = converter.getSourceNameForGeometry(PointGeometry(coordinates = listOf(0.0, 0.0)))
        assertEquals("gm_markers", sourceName)
    }

    @Test
    fun `getSourceNameForGeometry returns correct source for LineString`() {
        val sourceName = converter.getSourceNameForGeometry(LineStringGeometry(coordinates = listOf(0.0, 0.0, 1.0, 1.0)))
        assertEquals("gm_lines", sourceName)
    }

    @Test
    fun `buildPolygonEdgesGeoJson contains correct structure`() {
        val result = converter.buildPolygonEdgesGeoJson(listOf(3.0, 36.0, 3.01, 36.0, 3.005, 36.01))

        assertNotNull(result)
        assertTrue(result.startsWith("{"))
        assertTrue(result.contains("LineString"))
        assertTrue(result.contains("Feature"))
    }

    @Test
    fun `buildCircleGeoJson contains correct structure`() {
        val result = converter.buildCircleGeoJson(3.0, 36.0, 50.0)

        assertNotNull(result)
        assertTrue(result.startsWith("{"))
        assertTrue(result.contains("Polygon"))
        assertTrue(result.contains("Feature"))
    }
}
