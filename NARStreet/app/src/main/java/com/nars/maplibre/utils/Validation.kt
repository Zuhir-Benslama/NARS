package com.nars.maplibre.utils

import com.nars.maplibre.data.model.FeatureProperties
import com.nars.maplibre.data.model.PhaseDefinition

/**
 * Validation utilities for NARStreet Field Mode
 * 
 * Provides validation rules for feature data based on phase requirements.
 * Only 3 phases: Roads, HouseEntrances, NamingPanels
 */

/**
 * Validation result
 */
data class ValidationResult(
    val valid: Boolean,
    val errors: Map<String, String> = emptyMap(),
    val warnings: Map<String, String> = emptyMap()
) {
    companion object {
        fun success() = ValidationResult(valid = true)
        fun successWithWarning(field: String, message: String) = ValidationResult(
            valid = true,
            warnings = mapOf(field to message)
        )
        fun failure(field: String, message: String) = ValidationResult(
            valid = false,
            errors = mapOf(field to message)
        )
        fun failure(errors: Map<String, String>) = ValidationResult(
            valid = false,
            errors = errors
        )
    }
}

/**
 * House entrance validation result with step tracking
 */
data class HouseEntranceValidation(
    val hasEntrance: Boolean,
    val hasNumberingPanel: Boolean? = null,
    val numberingPanelCorrect: Boolean? = null,
    val numberingPanelPositionCorrect: Boolean? = null,
    val needsNotification: Boolean = false,
    val issues: List<String> = emptyList()
)

/**
 * Naming panel validation result with step tracking
 */
data class NamingPanelValidation(
    val hasLocation: Boolean,
    val hasNamingPanel: Boolean? = null,
    val namingCorrect: Boolean? = null,
    val namingPanelPositionCorrect: Boolean? = null,
    val needsNotification: Boolean = false,
    val issues: List<String> = emptyList()
)

/**
 * Validate feature properties based on phase
 * Simplified for NARStreet Field Mode
 */
fun validateFeatureProperties(
    properties: FeatureProperties,
    phase: PhaseDefinition
): ValidationResult {
    val errors = mutableMapOf<String, String>()

    when (phase.key) {
        "roads" -> validateRoadFieldProperties(properties, errors)
        "houseEntrances" -> validateHouseEntranceFieldProperties(properties, errors)
        "namingPanels" -> validateNamingPanelFieldProperties(properties, errors)
    }

    return if (errors.isEmpty()) {
        ValidationResult.success()
    } else {
        ValidationResult.failure(errors)
    }
}

/**
 * Validate road properties for field mode
 * All attributes are required for roads in field mode
 */
private fun validateRoadFieldProperties(
    properties: FeatureProperties,
    errors: MutableMap<String, String>
) {
    // Name/label required
    if (properties.name.isNullOrBlank()) {
        errors["label"] = "Road name is required"
    }

    // Road type required
    if (properties.roadTypeKey.isNullOrBlank()) {
        errors["roadType"] = "Road type is required"
    }

    // Field mode attributes - all required
    if (properties.roadTraffic.isNullOrBlank()) {
        errors["roadTraffic"] = "Road traffic level is required"
    }

    if (properties.tradActivity.isNullOrBlank()) {
        errors["tradActivity"] = "Traditional activity level is required"
    }

    if (properties.numLanes == null) {
        errors["numLanes"] = "Number of lanes is required"
    }

    if (properties.hasMedian == null) {
        errors["hasMedian"] = "Median presence is required"
    }

    if (properties.hasVegetation == null) {
        errors["hasVegetation"] = "Vegetation presence is required"
    }

    if (properties.isDeadEnd == null) {
        errors["isDeadEnd"] = "Dead-end status is required"
    }

    if (properties.hasSidewalk == null) {
        errors["hasSidewalk"] = "Sidewalk presence is required"
    }
}

/**
 * Validate house entrance properties for field mode
 * Simplified - just need entrance to exist
 */
private fun validateHouseEntranceFieldProperties(
    properties: FeatureProperties,
    errors: MutableMap<String, String>
) {
    // Main entrance requires road assignment
    if (properties.entranceTypeKey == "main_entrance") {
        if (properties.roadDbId == null) {
            errors["road"] = "Main entrance must be assigned to a road"
        }
        
        if (properties.side.isNullOrBlank()) {
            errors["side"] = "Side (left/right) is required"
        }
    }
}

/**
 * Validate naming panel properties for field mode
 * Simplified - just need naming panel location
 */
private fun validateNamingPanelFieldProperties(
    properties: FeatureProperties,
    errors: MutableMap<String, String>
) {
    // Name/label required (street name)
    if (properties.name.isNullOrBlank()) {
        errors["label"] = "Street name is required"
    }
}

/**
 * Validate house entrance based on field mode workflow
 * Returns validation result with notification flag
 */
fun validateHouseEntranceFieldWorkflow(properties: FeatureProperties): HouseEntranceValidation {
    val issues = mutableListOf<String>()
    var needsNotification = false

    // Step 1: Check entrance presence
    if (properties.hasEntrance != true) {
        issues.add("Entrance not found")
        needsNotification = true
        return HouseEntranceValidation(
            hasEntrance = false,
            needsNotification = true,
            issues = issues
        )
    }

    // Step 2: Check numbering panel presence
    if (properties.hasNumberingPanel != true) {
        issues.add("Numbering panel not found")
        needsNotification = true
        return HouseEntranceValidation(
            hasEntrance = true,
            hasNumberingPanel = false,
            needsNotification = true,
            issues = issues
        )
    }

    // Step 3: Check numbering panel correct
    if (properties.numberingPanelCorrect != true) {
        issues.add("Number is incorrect")
        needsNotification = true
        return HouseEntranceValidation(
            hasEntrance = true,
            hasNumberingPanel = true,
            numberingPanelCorrect = false,
            needsNotification = true,
            issues = issues
        )
    }

    // Step 4: Check numbering panel position
    if (properties.numberingPanelPositionCorrect != true) {
        issues.add("Numbering panel position is incorrect")
        needsNotification = true
        return HouseEntranceValidation(
            hasEntrance = true,
            hasNumberingPanel = true,
            numberingPanelCorrect = true,
            numberingPanelPositionCorrect = false,
            needsNotification = true,
            issues = issues
        )
    }

    // All checks passed
    return HouseEntranceValidation(
        hasEntrance = true,
        hasNumberingPanel = true,
        numberingPanelCorrect = true,
        numberingPanelPositionCorrect = true,
        needsNotification = false,
        issues = emptyList()
    )
}

/**
 * Validate naming panel based on field mode workflow
 * Returns validation result with notification flag
 */
fun validateNamingPanelFieldWorkflow(properties: FeatureProperties): NamingPanelValidation {
    val issues = mutableListOf<String>()
    var needsNotification = false

    // Step 1: Check naming panel location exists
    if (properties.hasNamingPanelLocation != true) {
        issues.add("Naming panel location not found")
        needsNotification = true
        return NamingPanelValidation(
            hasLocation = false,
            needsNotification = true,
            issues = issues
        )
    }

    // Step 2: Check naming panel presence
    if (properties.hasNamingPanel != true) {
        issues.add("Naming panel not found")
        needsNotification = true
        return NamingPanelValidation(
            hasLocation = true,
            hasNamingPanel = false,
            needsNotification = true,
            issues = issues
        )
    }

    // Step 3: Check naming correct
    if (properties.namingCorrect != true) {
        issues.add("Street name is incorrect")
        needsNotification = true
        return NamingPanelValidation(
            hasLocation = true,
            hasNamingPanel = true,
            namingCorrect = false,
            needsNotification = true,
            issues = issues
        )
    }

    // Step 4: Check naming panel position
    if (properties.namingPanelPositionCorrect != true) {
        issues.add("Naming panel position is incorrect")
        needsNotification = true
        return NamingPanelValidation(
            hasLocation = true,
            hasNamingPanel = true,
            namingCorrect = true,
            namingPanelPositionCorrect = false,
            needsNotification = true,
            issues = issues
        )
    }

    // All checks passed
    return NamingPanelValidation(
        hasLocation = true,
        hasNamingPanel = true,
        namingCorrect = true,
        namingPanelPositionCorrect = true,
        needsNotification = false,
        issues = emptyList()
    )
}

/**
 * Validate road length (in meters)
 */
fun validateRoadLength(lengthMeters: Double): ValidationResult {
    if (lengthMeters < Config.MIN_ROAD_LENGTH_METERS) {
        return ValidationResult.failure(
            "length",
            "Road must be at least ${Config.MIN_ROAD_LENGTH_METERS} meters long"
        )
    }
    return ValidationResult.success()
}
