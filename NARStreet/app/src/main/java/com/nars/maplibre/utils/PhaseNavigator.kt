package com.nars.maplibre.utils

import com.nars.maplibre.data.model.PhaseDefinition
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.data.store.FeatureStore

/**
 * Phase navigation validation matching web version (phase-nav.ts)
 */
class PhaseNavigator(private val featureStore: FeatureStore) {

    /**
     * Check if user can advance to next phase
     * Returns null if allowed, error message key if blocked
     */
    fun canAdvance(targetPhaseIndex: Int): String? {
        val currentPhase = featureStore.currentPhase.value ?: return "alert_no_phase"
        val currentIndex = currentPhase.index
        val targetPhase = Phases.getByIndex(targetPhaseIndex) ?: return "alert_invalid_phase"

        // Can only go forward
        if (targetPhaseIndex <= currentIndex) return null

        when (currentPhase.key) {
            "areas" -> {
                val areas = featureStore.getFeaturesByPhase("areas")
                if (areas.isEmpty()) return "alert_at_least_one_urban_area"
            }
            "districts" -> {
                val coverage = checkDistrictCoverage()
                if (!coverage.covered) return "alert_coverage_error: ${coverage.message}"
            }
            "roads" -> {
                val roads = featureStore.getFeaturesByPhase("roads")
                if (roads.isEmpty()) return "alert_at_least_one_road"
            }
            "houseEntrances" -> {
                val entrances = featureStore.getFeaturesByPhase("houseEntrances")
                val mainEntrances = entrances.filter { it.properties.entranceTypeKey == "main_entrance" }
                if (mainEntrances.isEmpty()) return "alert_at_least_one_entrance"
            }
        }

        return null
    }

    /**
     * Check district coverage - districts must cover all areas
     * This is a simplified implementation - full version would use polygon intersection
     */
    fun checkDistrictCoverage(): CoverageResult {
        val areas = featureStore.getFeaturesByPhase("areas")
        val districts = featureStore.getFeaturesByPhase("districts")

        if (areas.isEmpty()) {
            return CoverageResult(true, "No areas to cover")
        }

        if (districts.isEmpty()) {
            return CoverageResult(false, "No districts defined")
        }

        // Simplified check: at least one district exists
        // Full implementation would verify polygon coverage using JTS
        return CoverageResult(true, "OK")
    }

    /**
     * Try to navigate to target phase
     * @return PhaseDefinition if navigation allowed, null if blocked
     */
    fun navigateTo(targetIndex: Int): PhaseDefinition? {
        val error = canAdvance(targetIndex)
        if (error != null) {
            return null // Navigation blocked
        }
        return Phases.getByIndex(targetIndex)
    }

    /**
     * Get the previous phase index
     */
    fun getPreviousPhaseIndex(): Int? {
        val current = featureStore.currentPhase.value ?: return null
        return if (current.index > 0) current.index - 1 else null
    }

    /**
     * Get the next phase index
     */
    fun getNextPhaseIndex(): Int? {
        val current = featureStore.currentPhase.value ?: return null
        return if (current.index < Phases.ALL.size - 1) current.index + 1 else null
    }

    /**
     * Check if can go to previous phase
     */
    fun canGoBack(): Boolean {
        return getPreviousPhaseIndex() != null
    }

    /**
     * Check if can go to next phase
     */
    fun canGoForward(): Boolean {
        val nextIndex = getNextPhaseIndex() ?: return false
        return canAdvance(nextIndex) == null
    }

    /**
     * Go to previous phase
     */
    fun goBack(): PhaseDefinition? {
        val prevIndex = getPreviousPhaseIndex() ?: return null
        val phase = Phases.getByIndex(prevIndex) ?: return null
        featureStore.setCurrentPhase(phase)
        return phase
    }

    /**
     * Try to go to next phase - validates requirements first
     * @return PhaseDefinition if allowed, null if blocked
     */
    fun goNext(): PhaseDefinition? {
        val nextIndex = getNextPhaseIndex() ?: return null
        val phase = navigateTo(nextIndex) ?: return null
        featureStore.setCurrentPhase(phase)
        return phase
    }
}

/**
 * Coverage validation result
 */
data class CoverageResult(
    val covered: Boolean,
    val message: String
)