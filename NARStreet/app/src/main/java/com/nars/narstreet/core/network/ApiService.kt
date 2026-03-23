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

    // ── Load features ─────────────────────────────────────────────────────────

    /** Load by specific sub-layer (e.g. "boulevard", "garden", "city_center") */
    @GET("/api/load/layer/{layer}")
    suspend fun loadLayer(@Path("layer") layer: String): List<FeatureDto>

    /** Load ALL features of a top-level type regardless of sub-layer.
     *  Used for types with many sub-layers: "public_building", "naming_panel" */
    @GET("/api/load/type/{type}")
    suspend fun loadByType(@Path("type") type: String): List<FeatureDto>

    // ── Commune context ───────────────────────────────────────────────────────

    /** Returns commune boundary + urban area polygons as GeoJSON strings. */
    @GET("/api/commune/context")
    suspend fun communeContext(): CommuneContextDto

    // ── Feature update (all phases) ───────────────────────────────────────────
    // All entity types share the same PUT /api/update/{id} endpoint.
    // Using a single method removes the bug where areas and districts were
    // incorrectly routed through updateRoad.

    @PUT("/api/update/{id}")
    suspend fun updateFeature(
        @Path("id") id: Long,
        @Body body: FeatureUpdateDto,
    ): FeatureUpdateResponseDto

    // ── House Entrances (Phase 05) — save new entrance only ───────────────────

    @POST("/api/save")
    suspend fun saveEntrance(@Body body: FeatureSaveDto): FeatureSaveResponseDto
}
