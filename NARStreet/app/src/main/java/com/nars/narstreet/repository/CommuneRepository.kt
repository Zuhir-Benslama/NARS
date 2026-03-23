package com.nars.narstreet.repository

import com.nars.narstreet.core.network.ApiService
import com.nars.narstreet.ui.components.CommuneMapContext
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Loads and caches the commune map context (boundary + urban areas) for the
 * authenticated user's commune. Data is read-only and held in memory — no
 * local Room cache is needed since the geometry never changes during a session.
 */
@Singleton
class CommuneRepository @Inject constructor(
    private val api: ApiService,
) {
    private val _context = MutableStateFlow<CommuneMapContext?>(null)
    val context: StateFlow<CommuneMapContext?> = _context.asStateFlow()

    /** Fetches commune context from the server. Safe to call multiple times — idempotent. */
    suspend fun refresh() {
        if (_context.value != null) return   // already loaded this session
        try {
            val dto = api.communeContext()
            _context.value = CommuneMapContext(
                boundaryGeoJson         = dto.boundaryGeoJson,
                mainAreaGeoJson         = dto.mainAreaGeoJson,
                secondaryAreasGeoJson   = dto.secondaryAreasGeoJson,
            )
        } catch (_: Exception) {
            // Offline or server error — map will render without context overlay.
            // Will retry automatically on next screen open.
        }
    }
}
