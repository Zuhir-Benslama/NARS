package com.nars.maplibre.data.model

import kotlinx.serialization.Serializable

/**
 * User data model
 * Represents authenticated user information
 */
@Serializable
data class User(
    val id: Int,
    val username: String,
    val name: String,
    val email: String? = null,
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
