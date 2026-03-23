package com.nars.narstreet.ui.login

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Visibility
import androidx.compose.material.icons.filled.VisibilityOff
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.*
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nars.narstreet.R
import com.nars.narstreet.ui.theme.*

// ── Shared glass style constants ──────────────────────────────────────────────

private val CardShape   = RoundedCornerShape(15.dp)
private val InputShape  = RoundedCornerShape(8.dp)
private val ButtonShape = RoundedCornerShape(8.dp)

private val GlassBackground = Brush.verticalGradient(
    colors = listOf(Color(0xFF0F1932), Color(0xFF0A0A1E))
)

@Composable
fun LoginScreen(
    viewModel: LoginViewModel,
    onLoggedIn: () -> Unit,
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()
    var showPassword by remember { mutableStateOf(false) }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(GlassBackground),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            modifier = Modifier
                .fillMaxWidth()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 24.dp, vertical = 40.dp),
        ) {

            // ── Logo ──────────────────────────────────────────────────────────
            Text(
                text       = "🗺️ NARS",
                fontSize   = 32.sp,
                fontWeight = FontWeight.Bold,
                color      = TextPrimary,
            )
            Spacer(Modifier.height(6.dp))
            Text(
                text     = "National Addressing Reference System",
                fontSize = 13.sp,
                color    = TextSecondary,
            )

            Spacer(Modifier.height(32.dp))

            // ── Glass card ────────────────────────────────────────────────────
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(CardShape)
                    .background(GlassBg)
                    .border(1.dp, GlassBorder, CardShape),
            ) {
                Column(
                    modifier            = Modifier.padding(32.dp),
                    verticalArrangement = Arrangement.spacedBy(16.dp),
                ) {

                    // Username field
                    GlassField(
                        value         = state.username,
                        onValueChange = viewModel::onUsernameChange,
                        label         = stringResource(R.string.login_username),
                        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Next),
                    )

                    // Password field
                    GlassField(
                        value         = state.password,
                        onValueChange = viewModel::onPasswordChange,
                        label         = stringResource(R.string.login_password),
                        visualTransformation = if (showPassword) VisualTransformation.None
                                               else PasswordVisualTransformation(),
                        trailingIcon  = {
                            IconButton(onClick = { showPassword = !showPassword }) {
                                Icon(
                                    imageVector = if (showPassword) Icons.Default.VisibilityOff
                                                  else Icons.Default.Visibility,
                                    contentDescription = null,
                                    tint = TextSecondary,
                                )
                            }
                        },
                        keyboardOptions = KeyboardOptions(
                            keyboardType = KeyboardType.Password,
                            imeAction    = ImeAction.Done,
                        ),
                        keyboardActions = KeyboardActions(
                            onDone = { viewModel.signIn(onLoggedIn) }
                        ),
                    )

                    // Error message
                    state.error?.let { msg ->
                        Box(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clip(RoundedCornerShape(8.dp))
                                .background(Color(0x4DC83232))
                                .border(1.dp, Color(0x66FF6464), RoundedCornerShape(8.dp))
                                .padding(12.dp),
                        ) {
                            Text(
                                text     = msg,
                                color    = Color(0xFFFFAAAA),
                                fontSize = 13.sp,
                            )
                        }
                    }

                    Spacer(Modifier.height(4.dp))

                    // Sign in button
                    Button(
                        onClick  = { viewModel.signIn(onLoggedIn) },
                        enabled  = !state.isLoading,
                        modifier = Modifier
                            .fillMaxWidth()
                            .height(52.dp),
                        shape  = ButtonShape,
                        colors = ButtonDefaults.buttonColors(
                            containerColor = NarsTeal,
                            contentColor   = NarsOnTeal,
                        ),
                    ) {
                        if (state.isLoading) {
                            CircularProgressIndicator(
                                modifier    = Modifier.size(20.dp),
                                color       = NarsOnTeal,
                                strokeWidth = 2.dp,
                            )
                        } else {
                            Text(
                                text       = stringResource(R.string.login_button),
                                fontSize   = 15.sp,
                                fontWeight = FontWeight.SemiBold,
                            )
                        }
                    }
                }
            }
        }
    }
}

// ── Glassmorphism text field ──────────────────────────────────────────────────

@Composable
private fun GlassField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    modifier: Modifier = Modifier,
    visualTransformation: VisualTransformation = VisualTransformation.None,
    trailingIcon: @Composable (() -> Unit)? = null,
    keyboardOptions: KeyboardOptions = KeyboardOptions.Default,
    keyboardActions: KeyboardActions = KeyboardActions.Default,
) {
    Column(
        modifier            = modifier.fillMaxWidth(),
        verticalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Text(
            text       = label,
            fontSize   = 13.sp,
            fontWeight = FontWeight.SemiBold,
            color      = TextSecondary,
        )
        OutlinedTextField(
            value                = value,
            onValueChange        = onValueChange,
            singleLine           = true,
            modifier             = Modifier.fillMaxWidth(),
            shape                = InputShape,
            visualTransformation = visualTransformation,
            trailingIcon         = trailingIcon,
            keyboardOptions      = keyboardOptions,
            keyboardActions      = keyboardActions,
            colors               = OutlinedTextFieldDefaults.colors(
                focusedContainerColor   = Color(0x1AFFFFFF),
                unfocusedContainerColor = Color(0x1AFFFFFF),
                focusedBorderColor      = Color(0x8CFFFFFF),
                unfocusedBorderColor    = Color(0x4DFFFFFF),
                focusedTextColor        = TextPrimary,
                unfocusedTextColor      = TextPrimary,
                cursorColor             = TextPrimary,
            ),
        )
    }
}
