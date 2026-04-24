package com.nars.maplibre.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.unit.dp
import com.nars.maplibre.data.model.DrawType
import com.nars.maplibre.ui.theme.GlassBackground

/**
 * Drawing controls - REMOVED for Field Mode
 * No manual drawing/editing allowed - features loaded from backend only
 */
@Composable
fun DrawingControls(
    currentPhase: com.nars.maplibre.data.model.PhaseDefinition?,
    isDrawing: Boolean,
    isEditing: Boolean,
    onDrawToggle: () -> Unit,
    onEditToggle: () -> Unit,
    onSettingsClick: () -> Unit,
    onUndoClick: (() -> Unit)? = null,
    onGenerateNamingPanels: (() -> Unit)? = null,
    onComputeRoadDirections: (() -> Unit)? = null,
    onSetHouseNumbers: (() -> Unit)? = null,
    canUndo: Boolean = false,
    modifier: Modifier = Modifier
) {
    // Field mode - no manual controls, features loaded from backend
}

/**
 * Compact tool icon button with glass-morphism style
 */
@Composable
private fun ToolIconButton(
    icon: ImageVector,
    contentDescription: String,
    isActive: Boolean,
    activeColor: Color,
    onClick: () -> Unit
) {
    android.util.Log.d("DrawingControls", "ToolIconButton clicked: $contentDescription, isActive=$isActive")
    Box(
        modifier = Modifier
            .size(44.dp)
            .clip(RoundedCornerShape(10.dp))
            .background(
                if (isActive) {
                    activeColor.copy(alpha = 0.75f)
                } else {
                    Color(0xFF1e3a5f).copy(alpha = 0.85f) // Darker, more visible background
                }
            )
            .clickable { 
                android.util.Log.d("DrawingControls", "Button clicked: $contentDescription")
                onClick() 
            },
        contentAlignment = Alignment.Center
    ) {
        Icon(
            imageVector = icon,
            contentDescription = contentDescription,
            tint = if (isActive) Color.White else Color(0xFFe0e0e0), // Brighter tint
            modifier = Modifier.size(22.dp)
        )
    }
}

/**
 * Floating action buttons variant - REMOVED for Field Mode
 */
@Composable
fun FloatingDrawingControls(
    isDrawing: Boolean,
    isEditing: Boolean,
    onDrawToggle: () -> Unit,
    onEditToggle: () -> Unit,
    modifier: Modifier = Modifier
) {
    // Field mode - no manual controls
}
