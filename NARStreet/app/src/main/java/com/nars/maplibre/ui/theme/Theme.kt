package com.nars.maplibre.ui.theme

import android.app.Activity
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.platform.LocalView
import androidx.core.view.WindowCompat

/**
 * Theme modes
 */
enum class ThemeMode {
    LIGHT,
    DARK,
    AUTO
}

// Glass-morphism colors (matching nars-vite-maplibre)
val GlassBackground = Color(0xFF0f1932)
val GlassBackgroundTransparent = Color(0x330f1932)
val GlassSurface = Color(0x330f1932)
val GlassBorder = Color(0x40FFFFFF)
val GlassBorderStrong = Color(0x66FFFFFF)

val TextPrimary = Color(0xFFFFFFFF)
val TextSecondary = Color(0xB3FFFFFF)
val TextMuted = Color(0x66FFFFFF)

val PrimaryGradientStart = Color(0xFF667eea)
val PrimaryGradientEnd = Color(0xFF764ba2)
val PrimaryColor = Color(0xFF667eea)

val InputBackground = Color(0x1AFFFFFF)
val InputBorder = Color(0x4DFFFFFF)
val InputBorderFocus = Color(0x8CFFFFFF)

val DropdownBackground = Color(0xF20A1937)
val DropdownBorder = Color(0x33FFFFFF)
val DropdownItem = Color(0xD9FFFFFF)
val DropdownHover = Color(0x1FFFFFFF)

val DangerColor = Color(0xFFe74c3c)
val SuccessColor = Color(0xFF27ae60)
val InfoColor = Color(0xFF3498db)

// Light color scheme (glass-morphism inspired)
private val LightColorScheme = lightColorScheme(
    primary = PrimaryColor,
    onPrimary = Color.White,
    primaryContainer = Color(0xFFBBDEFB),
    onPrimaryContainer = Color(0xFF1976D2),
    secondary = Color(0xFFFF5722),
    onSecondary = Color.White,
    secondaryContainer = Color(0xFFFFCCBC),
    onSecondaryContainer = Color(0xFFE64A19),
    tertiary = Color(0xFF8e44ad),
    onTertiary = Color.White,
    tertiaryContainer = Color(0xFFE1BEE7),
    onTertiaryContainer = Color(0xFF7B1FA2),
    background = Color(0xFFFAFAFA),
    onBackground = Color(0xFF212121),
    surface = Color(0xFFFFFFFF),
    onSurface = Color(0xFF212121),
    surfaceVariant = Color(0xFFEEEEEE),
    onSurfaceVariant = Color(0xFF757575),
    error = Color(0xFFF44336),
    onError = Color.White,
    outline = Color(0xFFBDBDBD)
)

// Dark color scheme (glass-morphism)
private val DarkColorScheme = darkColorScheme(
    primary = PrimaryColor,
    onPrimary = Color.White,
    primaryContainer = Color(0xFF1565C0),
    onPrimaryContainer = Color(0xFF90CAF9),
    secondary = Color(0xFFFFAB91),
    onSecondary = Color(0xFF3E2723),
    secondaryContainer = Color(0xFFE64A19),
    onSecondaryContainer = Color(0xFFFFCCBC),
    tertiary = Color(0xFFCE93D8),
    onTertiary = Color(0xFF4A148C),
    tertiaryContainer = Color(0xFF7B1FA2),
    onTertiaryContainer = Color(0xFFE1BEE7),
    background = GlassBackground,
    onBackground = TextPrimary,
    surface = GlassSurface,
    onSurface = TextPrimary,
    surfaceVariant = GlassBackgroundTransparent,
    onSurfaceVariant = TextSecondary,
    error = DangerColor,
    onError = Color.White,
    outline = GlassBorder
)

/**
 * NARS Theme composable
 */
@Composable
fun NARSTheme(
    themeMode: ThemeMode = ThemeMode.AUTO,
    content: @Composable () -> Unit
) {
    val darkTheme = when (themeMode) {
        ThemeMode.LIGHT -> false
        ThemeMode.DARK -> true
        ThemeMode.AUTO -> isSystemInDarkTheme()
    }
    
    val colorScheme = if (darkTheme) DarkColorScheme else LightColorScheme
    
    val view = LocalView.current
    if (!view.isInEditMode) {
        SideEffect {
            val window = (view.context as Activity).window
            @Suppress("DEPRECATION")
            window.statusBarColor = colorScheme.primary.toArgb()
            WindowCompat.getInsetsController(window, view).isAppearanceLightStatusBars = !darkTheme
        }
    }
    
    MaterialTheme(
        colorScheme = colorScheme,
        content = content
    )
}
