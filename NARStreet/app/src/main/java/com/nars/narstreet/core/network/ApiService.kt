package com.nars.narstreet.core.network

import com.nars.narstreet.data.remote.dto.*
import retrofit2.http.*

interface ApiService {

    // ── Auth ──────────────────────────────────────────────────────────────────

    @POST("/api/signin")
    suspend fun signIn(@Body body: SignInRequestDto): SignInResponseDto

    @POST("/api/logout")
    suspend fun logout()

    @GET("/api/current_user")
    suspend fun currentUser(): CurrentUserDto

    // ── Load features (read-only, used on first sync) ─────────────────────────

    @GET("/api/load/layer/{layer}")
    suspend fun loadLayer(@Path("layer") layer: String): List<FeatureDto>

    // ── Roads (Phase 04) ──────────────────────────────────────────────────────

    @PUT("/api/update/{id}")
    suspend fun updateRoad(
        @Path("id") id: Long,
        @Body body: FeatureUpdateDto,
    ): FeatureUpdateResponseDto

    // ── House Entrances (Phase 05) ────────────────────────────────────────────

    @POST("/api/save")
    suspend fun saveEntrance(@Body body: FeatureSaveDto): FeatureSaveResponseDto

    @PUT("/api/update/{id}")
    suspend fun updateEntrance(
        @Path("id") id: Long,
        @Body body: FeatureUpdateDto,
    ): FeatureUpdateResponseDto

    // ── Public Buildings (Phase 06) ───────────────────────────────────────────

    @PUT("/api/update/{id}")
    suspend fun updateBuilding(
        @Path("id") id: Long,
        @Body body: FeatureUpdateDto,
    ): FeatureUpdateResponseDto

    // ── Public Spaces (Phase 07) ──────────────────────────────────────────────

    @PUT("/api/update/{id}")
    suspend fun updateSpace(
        @Path("id") id: Long,
        @Body body: FeatureUpdateDto,
    ): FeatureUpdateResponseDto

    // ── Naming Panels (Phase 08) ──────────────────────────────────────────────

    @PUT("/api/update/{id}")
    suspend fun updatePanel(
        @Path("id") id: Long,
        @Body body: FeatureUpdateDto,
    ): FeatureUpdateResponseDto
}
