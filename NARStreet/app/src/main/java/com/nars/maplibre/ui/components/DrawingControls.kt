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
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.CenterFocusStrong
import androidx.compose.material.icons.filled.Draw
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.Label
import androidx.compose.material.icons.filled.LineStyle
import androidx.compose.material.icons.filled.Place
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Timeline
import androidx.compose.material.icons.filled.Undo
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
import com.nars.maplibre.ui.theme.GlassBorder
import com.nars.maplibre.ui.theme.TextPrimary

/**
 * Drawing controls - matches nars-vite-maplibre design
 * Compact icon-only buttons with glass-morphism style
 * Positioned on the left side of the map
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
    Column(
        modifier = modifier
            .padding(12.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        // Draw button based on phase type
        currentPhase?.let { phase ->
            val phaseColor = Color(android.graphics.Color.parseColor(phase.color))

            ToolIconButton(
                icon = when (phase.drawType) {
                    DrawType.POLYGON -> Icons.Default.Draw
                    DrawType.POLYLINE -> Icons.Default.LineStyle
                    DrawType.CIRCLE -> Icons.Default.CenterFocusStrong
                    DrawType.MARKER -> Icons.Default.Place
                },
                contentDescription = "Draw ${phase.label}",
                isActive = isDrawing,
                activeColor = phaseColor,
                onClick = onDrawToggle
            )

            Spacer(modifier = Modifier.height(4.dp))
        }

        // Edit button
        ToolIconButton(
            icon = Icons.Default.Edit,
            contentDescription = "Edit Features",
            isActive = isEditing,
            activeColor = MaterialTheme.colorScheme.secondary,
            onClick = onEditToggle
        )

        Spacer(modifier = Modifier.height(4.dp))

        // Undo button
        onUndoClick?.let { undoClick ->
            ToolIconButton(
                icon = Icons.Default.Undo,
                contentDescription = "Undo",
                isActive = false,
                activeColor = if (canUndo) MaterialTheme.colorScheme.error else Color.Gray,
                onClick = if (canUndo) undoClick else { {} }
            )
            Spacer(modifier = Modifier.height(4.dp))
        }

        // Phase-specific action buttons
        currentPhase?.let { phase ->
            when (phase.key) {
                "roads" -> {
                    // Compute road directions
                    onComputeRoadDirections?.let { click ->
                        ToolIconButton(
                            icon = Icons.Default.Timeline,
                            contentDescription = "Compute Directions",
                            isActive = false,
                            activeColor = Color(0xFF3498db),
                            onClick = click
                        )
                        Spacer(modifier = Modifier.height(4.dp))
                    }
                }
                "houseEntrances" -> {
                    // Set house numbers
                    onSetHouseNumbers?.let { click ->
                        ToolIconButton(
                            icon = Icons.Default.Add,
                            contentDescription = "Set House Numbers",
                            isActive = false,
                            activeColor = Color(0xFF27ae60),
                            onClick = click
                        )
                        Spacer(modifier = Modifier.height(4.dp))
                    }
                }
                "namingPanels" -> {
                    // Generate naming panels
                    onGenerateNamingPanels?.let { click ->
                        ToolIconButton(
                            icon = Icons.Default.Label,
                            contentDescription = "Generate Panels",
                            isActive = false,
                            activeColor = Color(0xFF9b59b6),
                            onClick = click
                        )
                        Spacer(modifier = Modifier.height(4.dp))
                    }
                }
            }
        }

        // Settings button
        ToolIconButton(
            icon = Icons.Default.Settings,
            contentDescription = "Settings",
            isActive = false,
            activeColor = MaterialTheme.colorScheme.primary,
            onClick = onSettingsClick
        )
    }
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
 * Floating action buttons variant (alternative design)
 */
@Composable
fun FloatingDrawingControls(
    isDrawing: Boolean,
    isEditing: Boolean,
    onDrawToggle: () -> Unit,
    onEditToggle: () -> Unit,
    modifier: Modifier = Modifier
) {
    Column(
        modifier = modifier
            .padding(12.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        // Draw FAB
        FloatingActionButton(
            icon = Icons.Default.Draw,
            contentDescription = "Draw",
            isActive = isDrawing,
            onClick = onDrawToggle
        )

        // Edit FAB
        FloatingActionButton(
            icon = Icons.Default.Edit,
            contentDescription = "Edit",
            isActive = isEditing,
            onClick = onEditToggle
        )
    }
}

/**
 * Simple floating action button
 */
@Composable
private fun FloatingActionButton(
    icon: ImageVector,
    contentDescription: String,
    isActive: Boolean,
    onClick: () -> Unit
) {
    Box(
        modifier = Modifier
            .size(50.dp)
            .clip(RoundedCornerShape(25.dp))
            .background(
                if (isActive) {
                    MaterialTheme.colorScheme.secondary
                } else {
                    GlassBackground.copy(alpha = 0.8f)
                }
            )
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center
    ) {
        Icon(
            imageVector = icon,
            contentDescription = contentDescription,
            tint = Color.White,
            modifier = Modifier.size(24.dp)
        )
    }
}
