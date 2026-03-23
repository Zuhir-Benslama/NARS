package com.nars.narstreet.ui.theme

import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

// ── NARS dark navy colour scheme ─────────────────────────────────────────────
// Mirrors the web frontend exactly — no dynamic/system colour override.

private val NarsColorScheme = darkColorScheme(
    // Primary — teal green (save / confirm actions)
    primary              = NarsTeal,
    onPrimary            = NarsOnTeal,
    primaryContainer     = Color(0xFF0F6E56),
    onPrimaryContainer   = Color(0xFF9FE1CB),

    // Secondary — blue (focus / info)
    secondary            = NarsBlue,
    onSecondary          = NarsOnTeal,
    secondaryContainer   = Color(0xFF1A3A5C),
    onSecondaryContainer = Color(0xFFADD8F7),

    // Background — deep navy
    background           = NarsNavyDeep,
    onBackground         = TextPrimary,

    // Surface hierarchy
    surface              = NarsNavy,
    onSurface            = TextPrimary,
    surfaceVariant       = NarsNavyLight,
    onSurfaceVariant     = TextSecondary,
    surfaceContainerHigh = NarsNavyLight,
    surfaceContainerLow  = NarsNavyDeep,
    surfaceContainer     = NarsNavy,

    // Outline
    outline              = GlassBorder,
    outlineVariant       = Color(0x1FFFFFFF),

    // Error
    error                = SyncError,
    onError              = NarsOnTeal,
    errorContainer       = Color(0x33E24B4A),
    onErrorContainer     = Color(0xFFFFAAAA),

    // Inverse
    inverseSurface       = TextPrimary,
    inverseOnSurface     = NarsNavyDeep,
)

@Composable
fun NARStreetTheme(
    content: @Composable () -> Unit,
) {
    MaterialTheme(
        colorScheme = NarsColorScheme,
        typography  = NARStreetTypography,
        content     = content,
    )
}
