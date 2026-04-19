package com.nars.maplibre.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nars.maplibre.data.model.PhaseDefinition
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.ui.theme.GlassBackground
import com.nars.maplibre.ui.theme.GlassBorder
import com.nars.maplibre.ui.theme.TextPrimary
import com.nars.maplibre.ui.theme.TextSecondary
import com.nars.maplibre.ui.theme.TextMuted

/**
 * Phase bar component - matches nars-vite-maplibre design
 * Shows all phases with connectors, badges, and active/highlighted states
 */
@Composable
fun PhaseBar(
    currentPhase: PhaseDefinition?,
    phaseCounts: Map<String, Int>,
    onPhaseSelected: (PhaseDefinition) -> Unit,
    modifier: Modifier = Modifier
) {
    Column(
        modifier = modifier
            .fillMaxWidth()
            .padding(horizontal = 16.dp, vertical = 12.dp)
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceEvenly,
            verticalAlignment = Alignment.CenterVertically
        ) {
            Phases.ALL.forEachIndexed { index, phase ->
                val isDone = currentPhase?.let { cp -> 
                    Phases.ALL.indexOfFirst { it.key == cp.key } > index 
                } ?: false
                val isActive = currentPhase?.key == phase.key
                val count = phaseCounts[phase.key] ?: 0
                val phaseColor = Color(android.graphics.Color.parseColor(phase.color))
                val badge = if (isDone) "✓" else "${index + 1}"

                PhaseStep(
                    badge = badge,
                    phaseColor = phaseColor,
                    isActive = isActive,
                    isDone = isDone,
                    count = count,
                    onClick = { onPhaseSelected(phase) }
                )

                // Connector between phases (except after last)
                if (index < Phases.ALL.size - 1) {
                    val isConnectorDone = isDone
                    Spacer(modifier = Modifier.width(4.dp))
                    PhaseConnector(
                        isDone = isConnectorDone,
                        modifier = Modifier
                            .width(24.dp)
                            .height(3.dp)
                    )
                    Spacer(modifier = Modifier.width(4.dp))
                }
            }
        }
    }
}

/**
 * Individual phase step button
 */
@Composable
private fun PhaseStep(
    badge: String,
    phaseColor: Color,
    isActive: Boolean,
    isDone: Boolean,
    count: Int,
    onClick: () -> Unit
) {
    Column(
        horizontalAlignment = Alignment.CenterHorizontally,
        modifier = Modifier.clickable(onClick = onClick)
    ) {
        // Phase badge button
        Box(
            modifier = Modifier
                .size(42.dp)
                .clip(RoundedCornerShape(10.dp))
                .background(
                    when {
                        isDone -> phaseColor
                        isActive -> phaseColor.copy(alpha = 0.8f)
                        else -> GlassBackground.copy(alpha = 0.6f)
                    }
                )
                .clickable(onClick = onClick),
            contentAlignment = Alignment.Center
        ) {
            Text(
                text = badge,
                fontSize = 16.sp,
                fontWeight = androidx.compose.ui.text.font.FontWeight.Bold,
                color = when {
                    isDone || isActive -> Color.White
                    else -> TextSecondary
                }
            )
        }

        Spacer(modifier = Modifier.height(6.dp))

        // Count badge
        if (count > 0) {
            Box(
                modifier = Modifier
                    .clip(RoundedCornerShape(8.dp))
                    .background(
                        if (isActive) phaseColor.copy(alpha = 0.2f) else GlassBackground
                    )
                    .padding(horizontal = 6.dp, vertical = 2.dp)
            ) {
                Text(
                    text = count.toString(),
                    fontSize = 11.sp,
                    color = if (isActive) phaseColor else TextSecondary
                )
            }
        }
    }
}

/**
 * Phase connector line
 */
@Composable
private fun PhaseConnector(
    isDone: Boolean,
    modifier: Modifier = Modifier
) {
    Box(
        modifier = modifier
            .clip(RoundedCornerShape(2.dp))
            .background(
                if (isDone) GlassBorder else GlassBackground.copy(alpha = 0.4f)
            )
    )
}

/**
 * Compact phase selector (for smaller screens)
 */
@Composable
fun CompactPhaseSelector(
    currentPhase: PhaseDefinition?,
    phaseCounts: Map<String, Int>,
    onPhaseSelected: (PhaseDefinition) -> Unit,
    modifier: Modifier = Modifier
) {
    Row(
        modifier = modifier
            .fillMaxWidth()
            .padding(horizontal = 12.dp, vertical = 8.dp),
        horizontalArrangement = Arrangement.SpaceEvenly,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Phases.ALL.forEachIndexed { index, phase ->
            val isDone = currentPhase?.let { cp -> 
                Phases.ALL.indexOfFirst { it.key == cp.key } > index 
            } ?: false
            val isActive = currentPhase?.key == phase.key
            val count = phaseCounts[phase.key] ?: 0
            val phaseColor = Color(android.graphics.Color.parseColor(phase.color))
            val badge = if (isDone) "✓" else "${index + 1}"

            CompactPhaseItem(
                badge = badge,
                count = count,
                phaseColor = phaseColor,
                isActive = isActive,
                isDone = isDone,
                onClick = { onPhaseSelected(phase) }
            )
        }
    }
}

/**
 * Compact phase item
 */
@Composable
private fun CompactPhaseItem(
    badge: String,
    count: Int,
    phaseColor: Color,
    isActive: Boolean,
    isDone: Boolean,
    onClick: () -> Unit
) {
    Column(
        horizontalAlignment = Alignment.CenterHorizontally,
        modifier = Modifier.clickable(onClick = onClick)
    ) {
        Box(
            modifier = Modifier
                .size(36.dp)
                .clip(RoundedCornerShape(10.dp))
                .background(
                    when {
                        isDone -> phaseColor
                        isActive -> phaseColor.copy(alpha = 0.8f)
                        else -> GlassBackground.copy(alpha = 0.5f)
                    }
                )
                .clickable(onClick = onClick),
            contentAlignment = Alignment.Center
        ) {
            Text(
                text = badge,
                fontSize = 14.sp,
                fontWeight = androidx.compose.ui.text.font.FontWeight.Bold,
                color = when {
                    isDone || isActive -> Color.White
                    else -> TextSecondary
                }
            )
        }

        if (count > 0) {
            Spacer(modifier = Modifier.height(4.dp))
            Text(
                text = count.toString(),
                fontSize = 10.sp,
                color = if (isActive) phaseColor else TextMuted
            )
        }
    }
}
