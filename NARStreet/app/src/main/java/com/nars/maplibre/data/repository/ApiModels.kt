package com.nars.maplibre.data.repository

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

/**
 * API response wrapper
 */
@Serializable
data class ApiResponse<T>(
    val success: Boolean,
    val data: T? = null,
    val error: String? = null,
    val message: String? = null
)

/**
 * Feature request for API
 */
@Serializable
data class FeatureRequest(
    val type: String,
    val geometry: Map<String, String>,
    val properties: Map<String, String?>
)

/**
 * Feature response from API
 */
@Serializable
data class FeatureResponse(
    val id: String,
    val dbId: Long,
    val type: String,
    val geometry: GeometryResponse,
    val properties: PropertiesResponse,
    @SerialName("created_at") val createdAt: Long,
    @SerialName("updated_at") val updatedAt: Long
)

@Serializable
data class GeometryResponse(
    val type: String,
    val coordinates: List<Double>
)

@Serializable
data class PropertiesResponse(
    val name: String?,
    val number: String?,
    val phase: String,
    val color: String,
    val description: String?
)

/**
 * Batch save request
 */
@Serializable
data class BatchSaveRequest(
    val features: List<FeatureRequest>,
    val phase: String
)

/**
 * Batch save response
 */
@Serializable
data class BatchSaveResponse(
    val saved: List<FeatureResponse>,
    val failed: List<FailedFeature>
)

@Serializable
data class FailedFeature(
    val index: Int,
    val error: String
)

/**
 * Load response
 */
@Serializable
data class LoadResponse(
    val features: List<FeatureResponse>,
    val phases: List<PhaseInfo>
)

@Serializable
data class PhaseInfo(
    val key: String,
    val name: String,
    val count: Int
)
