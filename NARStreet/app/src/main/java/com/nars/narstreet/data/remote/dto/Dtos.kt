package com.nars.narstreet.data.remote.dto

import com.squareup.moshi.Json
import com.squareup.moshi.JsonClass

// ── Auth ──────────────────────────────────────────────────────────────────────

@JsonClass(generateAdapter = true)
data class SignInRequestDto(
    @Json(name = "username") val username: String,
    @Json(name = "password") val password: String,
)

@JsonClass(generateAdapter = true)
data class SignInResponseDto(
    @Json(name = "success")      val success: Boolean,
    @Json(name = "access_token") val accessToken: String,
    @Json(name = "user")         val user: UserDto,
)

@JsonClass(generateAdapter = true)
data class CurrentUserDto(
    @Json(name = "id")       val id: Int,
    @Json(name = "username") val username: String,
    @Json(name = "name")     val name: String,
    @Json(name = "commune")  val commune: CommuneDto,
)

@JsonClass(generateAdapter = true)
data class UserDto(
    @Json(name = "id")       val id: Int,
    @Json(name = "username") val username: String,
    @Json(name = "name")     val name: String,
    @Json(name = "commune")  val commune: CommuneDto,
)

@JsonClass(generateAdapter = true)
data class CommuneDto(
    @Json(name = "id")      val id: Int,
    @Json(name = "name_fr") val nameFr: String?,
    @Json(name = "name_ar") val nameAr: String?,
)

// ── Feature load ──────────────────────────────────────────────────────────────

@JsonClass(generateAdapter = true)
data class FeatureDto(
    @Json(name = "id")         val id: Long,
    @Json(name = "type")       val type: String,
    @Json(name = "layer")      val layer: String,
    @Json(name = "label")      val label: String,
    @Json(name = "data")       val data: Map<String, Any?>,
    @Json(name = "created_at") val createdAt: String?,
    @Json(name = "updated_at") val updatedAt: String?,
)

// ── Feature save / update ─────────────────────────────────────────────────────

@JsonClass(generateAdapter = true)
data class FeatureSaveDto(
    @Json(name = "type")  val type: String,
    @Json(name = "layer") val layer: String,
    @Json(name = "label") val label: String,
    @Json(name = "data")  val data: Map<String, Any?>,
)

@JsonClass(generateAdapter = true)
data class FeatureSaveResponseDto(
    @Json(name = "success") val success: Boolean,
    @Json(name = "id")      val id: Long,
)

@JsonClass(generateAdapter = true)
data class FeatureUpdateDto(
    @Json(name = "label") val label: String?,
    @Json(name = "data")  val data: Map<String, Any?>,
)

@JsonClass(generateAdapter = true)
data class FeatureUpdateResponseDto(
    @Json(name = "success")    val success: Boolean,
    @Json(name = "id")         val id: Long,
    @Json(name = "updated_at") val updatedAt: String?,
)
