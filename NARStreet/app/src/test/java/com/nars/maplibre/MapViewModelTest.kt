package com.nars.maplibre

import com.nars.maplibre.data.api.ApiService
import com.nars.maplibre.data.model.BaseLayerType
import com.nars.maplibre.data.model.FeatureProperties
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.NarsFeatureType
import com.nars.maplibre.data.model.PhaseDefinition
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.data.model.PointGeometry
import com.nars.maplibre.data.store.FeatureStore
import io.mockk.every
import io.mockk.mockk
import io.mockk.unmockkAll
import io.mockk.verify
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

@OptIn(ExperimentalCoroutinesApi::class)
class MapViewModelTest {

    private val featureStore = mockk<FeatureStore>(relaxed = true)
    private val appPreferences = mockk<AppPreferences>(relaxed = true)
    private val apiService = mockk<ApiService>(relaxed = true)

    private val currentPhaseFlow = MutableStateFlow<PhaseDefinition?>(null)
    private val allFeaturesFlow = MutableStateFlow<List<NarsFeature>>(emptyList())
    private val selectedFeatureFlow = MutableStateFlow<NarsFeature?>(null)
    private val referenceRoadFlow = MutableStateFlow<String?>(null)
    private val testDispatcher = StandardTestDispatcher()

    @Before
    fun setup() {
        Dispatchers.setMain(testDispatcher)
        every { featureStore.currentPhase } returns currentPhaseFlow
        every { featureStore.allFeatures } returns allFeaturesFlow
        every { featureStore.selectedFeature } returns selectedFeatureFlow
        every { featureStore.referenceRoadDbId } returns referenceRoadFlow
        every { featureStore.getFeaturesByPhase(any()) } returns emptyList()
        every { appPreferences.baseLayer } returns BaseLayerType.SATELLITE
    }

    @After
    fun tearDown() {
        Dispatchers.resetMain()
        unmockkAll()
    }

    private fun createViewModel(): MapViewModel {
        val vm = MapViewModel(featureStore, appPreferences, apiService)
        testDispatcher.scheduler.advanceUntilIdle()
        return vm
    }

    @Test
    fun `init sets first phase`() {
        createViewModel()
        verify { featureStore.setCurrentPhase(Phases.ALL.first()) }
    }

    @Test
    fun `setCurrentPhase sets phase and resets modes`() {
        currentPhaseFlow.value = Phases.ALL[0]
        every { featureStore.getFeaturesByPhase("roads") } returns listOf(
            NarsFeature(id = "r1", type = NarsFeatureType.ROAD,
                geometry = PointGeometry(coordinates = listOf(0.0, 0.0)),
                properties = FeatureProperties(phase = "roads", color = "#000"))
        )
        val vm = createViewModel()

        val result = vm.setCurrentPhase(Phases.ALL[1])

        assertEquals(Phases.ALL[1], result)
        verify { featureStore.setCurrentPhase(Phases.ALL[1]) }
        verify { appPreferences.currentPhase = Phases.ALL[1].key }
        assertFalse(vm.drawingEnabled.value)
        assertFalse(vm.editModeEnabled.value)
    }

    @Test
    fun `setCurrentPhase returns null when blocked by navigator`() {
        currentPhaseFlow.value = Phases.ALL[0]
        every { featureStore.getFeaturesByPhase("roads") } returns emptyList()
        val vm = createViewModel()

        val result = vm.setCurrentPhase(Phases.ALL[1])

        assertNull(result)
    }

    @Test
    fun `goToNextPhase advances when navigator allows`() {
        currentPhaseFlow.value = Phases.ALL[0]
        every { featureStore.getFeaturesByPhase("roads") } returns listOf(
            NarsFeature(id = "r1", type = NarsFeatureType.ROAD,
                geometry = PointGeometry(coordinates = listOf(0.0, 0.0)),
                properties = FeatureProperties(phase = "roads", color = "#000"))
        )
        val vm = createViewModel()

        val result = vm.goToNextPhase()

        assertEquals(Phases.ALL[1], result)
        verify { featureStore.setCurrentPhase(Phases.ALL[1]) }
    }

    @Test
    fun `goToNextPhase returns null when blocked`() {
        currentPhaseFlow.value = Phases.ALL[0]
        every { featureStore.getFeaturesByPhase("roads") } returns emptyList()
        val vm = createViewModel()

        val result = vm.goToNextPhase()

        assertNull(result)
    }

    @Test
    fun `goToPreviousPhase goes back`() = runTest {
        currentPhaseFlow.value = Phases.ALL[1]
        val vm = MapViewModel(featureStore, appPreferences, apiService)
        advanceUntilIdle()

        val result = vm.goToPreviousPhase()

        assertEquals(Phases.ALL[0], result)
        verify { featureStore.setCurrentPhase(Phases.ALL[0]) }
    }

    @Test
    fun `canGoNextPhase delegates to navigator`() {
        currentPhaseFlow.value = Phases.ALL[0]
        every { featureStore.getFeaturesByPhase("roads") } returns listOf(
            NarsFeature(id = "r1", type = NarsFeatureType.ROAD,
                geometry = PointGeometry(coordinates = listOf(0.0, 0.0)),
                properties = FeatureProperties(phase = "roads", color = "#000"))
        )
        val vm = createViewModel()

        assertTrue(vm.canGoNextPhase())
    }

    @Test
    fun `addFeature delegates to featureStore`() {
        val vm = createViewModel()
        val feature = NarsFeature(id = "f1", type = NarsFeatureType.ROAD,
            geometry = PointGeometry(coordinates = listOf(0.0, 0.0)),
            properties = FeatureProperties(phase = "roads", color = "#000"))

        vm.addFeature(feature)

        verify { featureStore.addFeature(feature, recordUndo = true) }
    }

    @Test
    fun `updateFeature updates and records undo`() {
        val vm = createViewModel()
        val oldFeature = NarsFeature(id = "f1", type = NarsFeatureType.ROAD,
            geometry = PointGeometry(coordinates = listOf(0.0, 0.0)),
            properties = FeatureProperties(phase = "roads", color = "#000", name = "Old"))
        val newFeature = oldFeature.copy(properties = oldFeature.properties.copy(name = "New"))

        every { featureStore.getFeatureById("f1") } returns oldFeature

        vm.updateFeature(newFeature)

        verify { featureStore.updateFeature("f1", newFeature) }
        verify { featureStore.addUndoAction(any()) }
    }

    @Test
    fun `deleteFeature removes and records undo`() {
        val vm = createViewModel()
        val feature = NarsFeature(id = "f1", type = NarsFeatureType.ROAD,
            geometry = PointGeometry(coordinates = listOf(0.0, 0.0)),
            properties = FeatureProperties(phase = "roads", color = "#000"))

        every { featureStore.getFeatureById("f1") } returns feature

        vm.deleteFeature("f1")

        verify { featureStore.removeFeature("f1") }
        verify { featureStore.addUndoAction(any()) }
    }

    @Test
    fun `selectFeature delegates to featureStore`() {
        val vm = createViewModel()
        val feature = NarsFeature(id = "f1", type = NarsFeatureType.ROAD,
            geometry = PointGeometry(coordinates = listOf(0.0, 0.0)),
            properties = FeatureProperties(phase = "roads", color = "#000"))

        vm.selectFeature(feature)

        verify { featureStore.selectFeature(feature) }
    }

    @Test
    fun `toggleDrawing enables drawing and disables edit mode`() {
        val vm = createViewModel()

        vm.toggleDrawing(true)

        assertTrue(vm.drawingEnabled.value)
        assertFalse(vm.editModeEnabled.value)
    }

    @Test
    fun `toggleEditMode enables edit and disables drawing`() {
        val vm = createViewModel()

        vm.toggleEditMode(true)

        assertTrue(vm.editModeEnabled.value)
        assertFalse(vm.drawingEnabled.value)
    }

    @Test
    fun `undo executes undo and shows success`() {
        val vm = createViewModel()

        vm.undo()

        verify { featureStore.executeUndo() }
    }

    @Test
    fun `undo shows error when nothing to undo`() {
        every { featureStore.executeUndo() } returns null
        val vm = createViewModel()

        vm.undo()

        assertEquals("Nothing to undo", vm.uiState.value.errorMessage)
    }

    @Test
    fun `setBaseLayer updates layer and preference`() {
        val vm = createViewModel()

        vm.setBaseLayer(BaseLayerType.STREET)

        assertEquals(BaseLayerType.STREET, vm.baseLayer.value)
        verify { appPreferences.baseLayer = BaseLayerType.STREET }
    }

    @Test
    fun `setReferenceRoad delegates to featureStore`() {
        val vm = createViewModel()

        vm.setReferenceRoad("road-123")

        verify { featureStore.setReferenceRoad("road-123") }
    }

    @Test
    fun `clearSelection delegates to featureStore`() {
        val vm = createViewModel()

        vm.clearSelection()

        verify { featureStore.selectFeature(null) }
    }

    @Test
    fun `showError updates uiState`() {
        val vm = createViewModel()

        vm.showError("Test error")

        assertEquals("Test error", vm.uiState.value.errorMessage)
    }

    @Test
    fun `clearError resets error message`() {
        val vm = createViewModel()
        vm.showError("Test error")

        vm.clearError()

        assertNull(vm.uiState.value.errorMessage)
    }

    @Test
    fun `showSuccess updates uiState`() {
        val vm = createViewModel()

        vm.showSuccess("Test success")

        assertEquals("Test success", vm.uiState.value.successMessage)
    }

    @Test
    fun `clearSuccess resets success message`() {
        val vm = createViewModel()
        vm.showSuccess("Test success")

        vm.clearSuccess()

        assertNull(vm.uiState.value.successMessage)
    }

    @Test
    fun `setLoading updates uiState`() {
        val vm = createViewModel()

        vm.setLoading(true)

        assertTrue(vm.uiState.value.isLoading)

        vm.setLoading(false)

        assertFalse(vm.uiState.value.isLoading)
    }

    @Test
    fun `selectedFeatureId maps from featureStore selectedFeature`() {
        every { featureStore.selectedFeature } returns MutableStateFlow(null)
        val vm = createViewModel()

        assertNull(vm.selectedFeatureId.value)
    }

    @Test
    fun `undo with Delete action restores feature`() {
        val feature = NarsFeature(id = "f1", type = NarsFeatureType.ROAD,
            geometry = PointGeometry(coordinates = listOf(0.0, 0.0)),
            properties = FeatureProperties(phase = "roads", color = "#000", name = "Restored Road"))
        val undoAction = io.mockk.mockk<com.nars.maplibre.data.store.UndoAction.Delete>(relaxed = true)
        every { undoAction.feature } returns feature
        every { featureStore.executeUndo() } returns undoAction

        val vm = createViewModel()
        vm.undo()

        assertEquals("Restored: Restored Road", vm.uiState.value.successMessage)
    }

    @Test
    fun `canUndo delegates to featureStore`() {
        every { featureStore.canUndo } returns true
        val vm = createViewModel()

        assertTrue(vm.canUndo)
    }
}
