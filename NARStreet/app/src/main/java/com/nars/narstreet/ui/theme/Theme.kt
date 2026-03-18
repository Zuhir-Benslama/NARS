package com.nars.narstreet.ui.theme

import android.os.Build
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.platform.LocalContext

private val LightFallback = lightColorScheme(
    primary       = NarsTeal,
    onPrimary     = NarsOnTeal,
    primaryContainer    = Color(0xFFE1F5EE),
    onPrimaryContainer  = Color(0xFF04342C),
)

private val DarkFallback = darkColorScheme(
    primary       = Color(0xFF5DCAA5),
    onPrimary     = Color(0xFF04342C),
    primaryContainer    = Color(0xFF0F6E56),
    onPrimaryContainer  = Color(0xFF9FE1CB),
)

@Composable
fun NARStreetTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit,
) {
    val colorScheme = when {
        Build.VERSION.SDK_INT >= Build.VERSION_CODES.S -> {
            val context = LocalContext.current
            if (darkTheme) dynamicDarkColorScheme(context)
            else           dynamicLightColorScheme(context)
        }
        darkTheme -> DarkFallback
        else      -> LightFallback
    }

    MaterialTheme(
        colorScheme = colorScheme,
        typography  = NARStreetTypography,
        content     = content,
    )
}
