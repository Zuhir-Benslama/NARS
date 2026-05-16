package com.nars.maplibre.utils

import com.nars.maplibre.data.model.FeatureProperties
import com.nars.maplibre.data.model.PhaseDefinition

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

data class HouseEntranceValidation(
    val hasEntrance: Boolean,
    val hasNumberingPanel: Boolean? = null,
    val numberingPanelCorrect: Boolean? = null,
    val numberingPanelPositionCorrect: Boolean? = null,
    val needsNotification: Boolean = false,
    val issues: List<String> = emptyList()
)

data class NamingPanelValidation(
    val hasLocation: Boolean,
    val hasNamingPanel: Boolean? = null,
    val namingCorrect: Boolean? = null,
    val namingPanelPositionCorrect: Boolean? = null,
    val needsNotification: Boolean = false,
    val issues: List<String> = emptyList()
)

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

private fun validateRoadFieldProperties(
    properties: FeatureProperties,
    errors: MutableMap<String, String>
) {
    if (properties.name.isNullOrBlank()) {
        errors["label"] = "Road name is required"
    }

    if (properties.roadTypeKey.isNullOrBlank()) {
        errors["roadType"] = "Road type is required"
    }

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

private fun validateHouseEntranceFieldProperties(
    properties: FeatureProperties,
    errors: MutableMap<String, String>
) {
    if (properties.entranceTypeKey == "main_entrance") {
        if (properties.roadDbId == null) {
            errors["road"] = "Main entrance must be assigned to a road"
        }

        if (properties.side.isNullOrBlank()) {
            errors["side"] = "Side (left/right) is required"
        }
    }
}

private fun validateNamingPanelFieldProperties(
    properties: FeatureProperties,
    errors: MutableMap<String, String>
) {
    if (properties.name.isNullOrBlank()) {
        errors["label"] = "Street name is required"
    }
}

fun validateHouseEntranceFieldWorkflow(properties: FeatureProperties): HouseEntranceValidation {
    val issues = mutableListOf<String>()

    if (properties.hasEntrance != true) {
        issues.add("Entrance not found")
        return HouseEntranceValidation(
            hasEntrance = false,
            needsNotification = true,
            issues = issues
        )
    }

    if (properties.hasNumberingPanel != true) {
        issues.add("Numbering panel not found")
        return HouseEntranceValidation(
            hasEntrance = true,
            hasNumberingPanel = false,
            needsNotification = true,
            issues = issues
        )
    }

    if (properties.numberingPanelCorrect != true) {
        issues.add("Number is incorrect")
        return HouseEntranceValidation(
            hasEntrance = true,
            hasNumberingPanel = true,
            numberingPanelCorrect = false,
            needsNotification = true,
            issues = issues
        )
    }

    if (properties.numberingPanelPositionCorrect != true) {
        issues.add("Numbering panel position is incorrect")
        return HouseEntranceValidation(
            hasEntrance = true,
            hasNumberingPanel = true,
            numberingPanelCorrect = true,
            numberingPanelPositionCorrect = false,
            needsNotification = true,
            issues = issues
        )
    }

    return HouseEntranceValidation(
        hasEntrance = true,
        hasNumberingPanel = true,
        numberingPanelCorrect = true,
        numberingPanelPositionCorrect = true,
        needsNotification = false,
        issues = emptyList()
    )
}

fun validateNamingPanelFieldWorkflow(properties: FeatureProperties): NamingPanelValidation {
    val issues = mutableListOf<String>()

    if (properties.hasNamingPanelLocation != true) {
        issues.add("Naming panel location not found")
        return NamingPanelValidation(
            hasLocation = false,
            needsNotification = true,
            issues = issues
        )
    }

    if (properties.hasNamingPanel != true) {
        issues.add("Naming panel not found")
        return NamingPanelValidation(
            hasLocation = true,
            hasNamingPanel = false,
            needsNotification = true,
            issues = issues
        )
    }

    if (properties.namingCorrect != true) {
        issues.add("Street name is incorrect")
        return NamingPanelValidation(
            hasLocation = true,
            hasNamingPanel = true,
            namingCorrect = false,
            needsNotification = true,
            issues = issues
        )
    }

    if (properties.namingPanelPositionCorrect != true) {
        issues.add("Naming panel position is incorrect")
        return NamingPanelValidation(
            hasLocation = true,
            hasNamingPanel = true,
            namingCorrect = true,
            namingPanelPositionCorrect = false,
            needsNotification = true,
            issues = issues
        )
    }

    return NamingPanelValidation(
        hasLocation = true,
        hasNamingPanel = true,
        namingCorrect = true,
        namingPanelPositionCorrect = true,
        needsNotification = false,
        issues = emptyList()
    )
}

fun validateRoadLength(lengthMeters: Double): ValidationResult {
    if (lengthMeters < Config.MIN_ROAD_LENGTH_METERS) {
        return ValidationResult.failure(
            "length",
            "Road must be at least ${Config.MIN_ROAD_LENGTH_METERS} meters long"
        )
    }
    return ValidationResult.success()
}
