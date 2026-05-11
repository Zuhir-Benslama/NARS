package com.nars.maplibre.data.model

import kotlinx.serialization.Serializable

/**
 * User data model
 * Represents authenticated user information
 */
@Serializable
data class User(
    val id: String = "",
    val username: String,
    val name: String,
    val email: String? = null,
    val role: String = "commune_user",
    val communeLatitude: Double? = null,
    val communeLongitude: Double? = null,
    val communeName: String? = null
) {
    /**
     * Get initials for avatar display
     */
    fun getInitials(): String {
        return username.firstOrNull()?.uppercase() ?: "U"
    }
    
    /**
     * Check if user has commune location
     */
    fun hasCommuneLocation(): Boolean {
        return communeLatitude != null && communeLongitude != null
    }

    /**
     * Check if the user is a field worker
     */
    fun isFieldWorker(): Boolean = role == "field_worker"
}

/**
 * Login request
 */
@Serializable
data class LoginRequest(
    val username: String,
    val password: String
)

/**
 * Login response
 */
@Serializable
data class LoginResponse(
    val user: User,
    val token: String? = null,
    val municipalityName: String? = null
)
