package com.nars.narstreet.repository

import com.nars.narstreet.core.network.ApiService
import com.nars.narstreet.core.session.SessionManager
import com.nars.narstreet.data.remote.dto.SignInRequestDto
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import javax.inject.Inject
import javax.inject.Singleton

sealed interface AuthResult {
    data object Success : AuthResult
    data class Error(val message: String) : AuthResult
}

@Singleton
class AuthRepository @Inject constructor(
    private val api: ApiService,
    private val session: SessionManager,
) {
    val isLoggedIn: Flow<Boolean> = session.token.map { it != null }

    suspend fun signIn(username: String, password: String): AuthResult {
        return try {
            val response = api.signIn(SignInRequestDto(username, password))
            val commune  = response.user.commune
            session.save(
                token       = response.accessToken,
                username    = response.user.username,
                communeName = commune.nameFr ?: commune.nameAr ?: "",
                communeLat  = commune.latitude  ?: 36.7,
                communeLng  = commune.longitude ?: 3.05,
            )
            AuthResult.Success
        } catch (e: Exception) {
            AuthResult.Error(e.message ?: "Sign in failed")
        }
    }

    suspend fun logout() {
        try { api.logout() } catch (_: Exception) { }
        session.clear()
    }
}
