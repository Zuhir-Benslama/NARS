package com.nars.narstreet.repository

import com.nars.narstreet.core.network.ApiService
import com.nars.narstreet.core.session.SessionManager
import com.nars.narstreet.data.remote.dto.SignInRequestDto
import kotlinx.coroutines.flow.Flow
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
    val isLoggedIn: Flow<Boolean> = kotlinx.coroutines.flow.map(session.token) { it != null }

    suspend fun signIn(username: String, password: String): AuthResult {
        return try {
            val response = api.signIn(SignInRequestDto(username, password))
            session.save(
                token       = response.accessToken,
                username    = response.user.username,
                communeName = response.user.commune.nameFr ?: response.user.commune.nameAr ?: "",
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
