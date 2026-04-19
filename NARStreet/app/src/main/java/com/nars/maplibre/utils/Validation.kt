package com.nars.maplibre.utils

import com.nars.maplibre.data.model.FeatureProperties
import com.nars.maplibre.data.model.PhaseDefinition

/**
 * Validation utilities matching the web version (validation.ts)
 * 
 * Provides validation rules for feature data based on phase requirements.
 */

/**
 * Validation result
 */
data class ValidationResult(
    val valid: Boolean,
    val errors: Map<String, String> = emptyMap()
) {
    companion object {
        fun success() = ValidationResult(valid = true)
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
 * Validate feature properties based on phase
 * Similar to web version's validateFeature function
 */
fun validateFeatureProperties(
    properties: FeatureProperties,
    phase: PhaseDefinition
): ValidationResult {
    val errors = mutableMapOf<String, String>()

    when (phase.key) {
        "areas" -> validateAreaProperties(properties, errors)
        "districts" -> validateDistrictProperties(properties, errors)
        "cityCenter" -> validateCityCenterProperties(properties, errors)
        "roads" -> validateRoadProperties(properties, errors)
        "houseEntrances" -> validateHouseEntranceProperties(properties, errors)
        "publicBuildings" -> validatePublicBuildingProperties(properties, errors)
        "publicSpaces" -> validatePublicSpaceProperties(properties, errors)
        "namingPanels" -> validateNamingPanelProperties(properties, errors)
    }

    return if (errors.isEmpty()) {
        ValidationResult.success()
    } else {
        ValidationResult.failure(errors)
    }
}

/**
 * Validate area feature properties
 */
private fun validateAreaProperties(
    properties: FeatureProperties,
    errors: MutableMap<String, String>
) {
    // Name/label required
    if (properties.name.isNullOrBlank()) {
        errors["label"] = "Name is required"
    }

    // Decision fields required
    if (properties.decisionNumber.isNullOrBlank()) {
        errors["decisionNumber"] = "Decision number is required"
    }

    if (properties.decisionDate.isNullOrBlank()) {
        errors["decisionDate"] = "Decision date is required"
    }

    // Area type required
    if (properties.areaTypeKey.isNullOrBlank()) {
        errors["areaType"] = "Area type is required"
    }
}

/**
 * Validate district feature properties
 */
private fun validateDistrictProperties(
    properties: FeatureProperties,
    errors: MutableMap<String, String>
) {
    // Name/label required
    if (properties.name.isNullOrBlank()) {
        errors["label"] = "Name is required"
    }

    // Decision fields required
    if (properties.decisionNumber.isNullOrBlank()) {
        errors["decisionNumber"] = "Decision number is required"
    }

    if (properties.decisionDate.isNullOrBlank()) {
        errors["decisionDate"] = "Decision date is required"
    }

    // District type required
    if (properties.districtTypeKey.isNullOrBlank()) {
        errors["districtType"] = "District type is required"
    }
}

/**
 * Validate city center feature properties
 */
private fun validateCityCenterProperties(
    properties: FeatureProperties,
    errors: MutableMap<String, String>
) {
    // City center always has fixed name
    if (!properties.name.isNullOrBlank() && properties.name != "City Center") {
        errors["label"] = "City center must be named 'City Center'"
    }

    // Decision fields required
    if (properties.decisionNumber.isNullOrBlank()) {
        errors["decisionNumber"] = "Decision number is required"
    }

    if (properties.decisionDate.isNullOrBlank()) {
        errors["decisionDate"] = "Decision date is required"
    }
}

/**
 * Validate road feature properties
 */
private fun validateRoadProperties(
    properties: FeatureProperties,
    errors: MutableMap<String, String>
) {
    // Name/label required
    if (properties.name.isNullOrBlank()) {
        errors["label"] = "Road name is required"
    }

    // Decision fields required
    if (properties.decisionNumber.isNullOrBlank()) {
        errors["decisionNumber"] = "Decision number is required"
    }

    if (properties.decisionDate.isNullOrBlank()) {
        errors["decisionDate"] = "Decision date is required"
    }

    // Road type required
    if (properties.roadTypeKey.isNullOrBlank()) {
        errors["roadType"] = "Road type is required"
    }
}

/**
 * Validate house entrance feature properties
 */
private fun validateHouseEntranceProperties(
    properties: FeatureProperties,
    errors: MutableMap<String, String>
) {
    // House entrances don't require decision fields (different from other phases)
    
    // Entrance type required
    if (properties.entranceTypeKey.isNullOrBlank()) {
        errors["entranceType"] = "Entrance type is required"
    }

    // Main entrance requires road assignment
    if (properties.entranceTypeKey == "main_entrance") {
        if (properties.roadDbId == null) {
            errors["road"] = "Main entrance must be assigned to a road"
        }
        
        // Side is required for main entrances
        if (properties.side.isNullOrBlank()) {
            errors["side"] = "Side (left/right) is required"
        }
    }
}

/**
 * Validate public building feature properties
 */
private fun validatePublicBuildingProperties(
    properties: FeatureProperties,
    errors: MutableMap<String, String>
) {
    // Name/label required
    if (properties.name.isNullOrBlank()) {
        errors["label"] = "Building name is required"
    }

    // Decision fields required
    if (properties.decisionNumber.isNullOrBlank()) {
        errors["decisionNumber"] = "Decision number is required"
    }

    if (properties.decisionDate.isNullOrBlank()) {
        errors["decisionDate"] = "Decision date is required"
    }

    // Sector and building type required
    if (properties.sectorKey.isNullOrBlank()) {
        errors["sector"] = "Sector is required"
    }

    if (properties.buildingTypeKey.isNullOrBlank()) {
        errors["buildingType"] = "Building type is required"
    }
}

/**
 * Validate public space feature properties
 */
private fun validatePublicSpaceProperties(
    properties: FeatureProperties,
    errors: MutableMap<String, String>
) {
    // Name/label required
    if (properties.name.isNullOrBlank()) {
        errors["label"] = "Space name is required"
    }

    // Decision fields required
    if (properties.decisionNumber.isNullOrBlank()) {
        errors["decisionNumber"] = "Decision number is required"
    }

    if (properties.decisionDate.isNullOrBlank()) {
        errors["decisionDate"] = "Decision date is required"
    }

    // Space type required
    if (properties.spaceTypeKey.isNullOrBlank()) {
        errors["spaceType"] = "Space type is required"
    }
}

/**
 * Validate naming panel feature properties
 */
private fun validateNamingPanelProperties(
    properties: FeatureProperties,
    errors: MutableMap<String, String>
) {
    // Name/label required (street name)
    if (properties.name.isNullOrBlank()) {
        errors["label"] = "Street name is required"
    }

    // Decision fields required
    if (properties.decisionNumber.isNullOrBlank()) {
        errors["decisionNumber"] = "Decision number is required"
    }

    if (properties.decisionDate.isNullOrBlank()) {
        errors["decisionDate"] = "Decision date is required"
    }
}

/**
 * Validate road length (in meters)
 * Similar to web version's VALIDATION_CONFIG.minRoadLengthMeters
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

/**
 * Validate decision number format
 * Expected format: YYYY/NNN (e.g., 2024/001)
 */
fun validateDecisionNumber(decisionNumber: String): ValidationResult {
    if (decisionNumber.isNullOrBlank()) {
        return ValidationResult.failure("decisionNumber", "Decision number is required")
    }

    // Basic format check: should contain a year and number
    val pattern = Regex("^\\d{4}/\\d+$")
    if (!pattern.matches(decisionNumber)) {
        return ValidationResult.failure(
            "decisionNumber",
            "Format should be YYYY/NNN (e.g., 2024/001)"
        )
    }

    return ValidationResult.success()
}

/**
 * Validate decision date is not in the future
 */
fun validateDecisionDate(decisionDate: String): ValidationResult {
    if (decisionDate.isNullOrBlank()) {
        return ValidationResult.failure("decisionDate", "Decision date is required")
    }

    try {
        val date = java.time.LocalDate.parse(decisionDate)
        val today = java.time.LocalDate.now()
        
        if (date.isAfter(today)) {
            return ValidationResult.failure(
                "decisionDate",
                "Decision date cannot be in the future"
            )
        }
    } catch (e: Exception) {
        return ValidationResult.failure(
            "decisionDate",
            "Invalid date format (use YYYY-MM-DD)"
        )
    }

    return ValidationResult.success()
}
