package com.nars.narstreet.ui.login

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nars.narstreet.repository.AuthRepository
import com.nars.narstreet.repository.AuthResult
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import javax.inject.Inject

data class LoginUiState(
    val username: String  = "",
    val password: String  = "",
    val isLoading: Boolean = false,
    val error: String?    = null,
)

@HiltViewModel
class LoginViewModel @Inject constructor(
    private val auth: AuthRepository,
) : ViewModel() {

    private val _uiState = MutableStateFlow(LoginUiState())
    val uiState: StateFlow<LoginUiState> = _uiState.asStateFlow()

    fun onUsernameChange(v: String) { _uiState.update { it.copy(username = v, error = null) } }
    fun onPasswordChange(v: String) { _uiState.update { it.copy(password = v, error = null) } }

    fun signIn(onSuccess: () -> Unit) {
        val state = _uiState.value
        if (state.username.isBlank() || state.password.isBlank()) {
            _uiState.update { it.copy(error = "Username and password are required") }
            return
        }
        viewModelScope.launch {
            _uiState.update { it.copy(isLoading = true, error = null) }
            when (val result = auth.signIn(state.username, state.password)) {
                is AuthResult.Success      -> onSuccess()
                is AuthResult.Error        -> _uiState.update { it.copy(error = result.message) }
            }
            _uiState.update { it.copy(isLoading = false) }
        }
    }
}
