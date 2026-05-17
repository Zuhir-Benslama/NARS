package com.nars.maplibre.ui.components

import com.nars.maplibre.utils.NarsLogger
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nars.maplibre.data.model.PhaseDefinition
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.ui.theme.GlassBackground
import com.nars.maplibre.ui.theme.PrimaryColor
import com.nars.maplibre.ui.theme.SuccessColor

/**
 * Vertical Phase Navigation Sidebar
 * Matches web app design but positioned vertically on the right side
 */
@Composable
fun VerticalPhaseNav(
    currentPhaseIndex: Int,
    phaseCounts: Map<String, Int>,
    onPhaseSelected: (PhaseDefinition) -> Unit,
    modifier: Modifier = Modifier
) {

    Column(
        modifier = modifier
            .clip(RoundedCornerShape(10.dp))
            .background(
                GlassBackground.copy(alpha = 0.95f)
            )
            .padding(vertical = 8.dp, horizontal = 6.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(2.dp)
    ) {
        Phases.ALL.forEachIndexed { index, phase ->
            // Check if phase has features
            val phaseHasFeatures = (phaseCounts[phase.key] ?: 0) > 0
            
            // Phase is done if it has features
            val isDone = phaseHasFeatures
            // Phase is active if it's the current phase
            val isActive = index == currentPhaseIndex
            // Phase is locked if previous phase has no features (or if it's not the first phase and previous is empty)
            val isLocked = if (index == 0) {
                false // First phase is always unlocked
            } else {
                // Check if all previous phases have at least one feature
                val previousPhasesHaveFeatures = (0 until index).all { prevIndex ->
                    (phaseCounts[Phases.ALL[prevIndex].key] ?: 0) > 0
                }
                !previousPhasesHaveFeatures
            }

            // Phase badge with larger touch target
            Box(
                modifier = Modifier
                    .size(40.dp)
                    .clip(CircleShape),
                contentAlignment = Alignment.Center
            ) {
                PhaseBadge(
                    phase = phase,
                    isDone = isDone,
                    isActive = isActive,
                    isLocked = isLocked,
                    count = phaseCounts[phase.key] ?: 0,
                    onClick = { onPhaseSelected(phase) }
                )
            }

            // Connector (except for last phase)
            if (index < Phases.ALL.lastIndex) {
                PhaseConnector(isDone = isDone)
            }
        }
    }
}

/**
 * Phase badge (circle with number or checkmark)
 */
@Composable
private fun PhaseBadge(
    phase: PhaseDefinition,
    isDone: Boolean,
    isActive: Boolean,
    isLocked: Boolean,
    count: Int,
    onClick: () -> Unit
) {
    val backgroundColor = when {
        isDone -> Color.White.copy(alpha = 0.15f)
        isActive -> Color.White.copy(alpha = 0.28f)
        else -> Color.White.copy(alpha = 0.05f)
    }

    val borderColor = when {
        isDone -> Color.White.copy(alpha = 0.35f)
        isActive -> Color.White.copy(alpha = 0.80f)
        else -> Color.White.copy(alpha = 0.15f)
    }

    val badgeColor = when {
        isDone -> Color.White
        isActive -> Color.White
        else -> Color.White.copy(alpha = 0.30f)
    }

    val badgeContent = if (isDone) "✓" else "${Phases.ALL.indexOf(phase) + 1}"

    // Clickable badge - using pointerInput for reliable touch over MapView
    Box(
        modifier = Modifier
            .size(28.dp)
            .clip(CircleShape)
            .background(backgroundColor)
            .pointerInput(isLocked, phase.key) {
                detectTapGestures(
                    onTap = {
                        NarsLogger.d("PhaseBadge", "Tapped phase: ${phase.label}, isLocked=$isLocked")
                        if (!isLocked) {
                            NarsLogger.d("PhaseBadge", "Calling onClick for ${phase.label}")
                            onClick()
                        } else {
                            NarsLogger.d("PhaseBadge", "Phase ${phase.label} is LOCKED - ignoring click")
                        }
                    }
                )
            },
        contentAlignment = Alignment.Center
    ) {
        // Border
        Box(
            modifier = Modifier
                .size(28.dp)
                .clip(CircleShape)
                .background(borderColor)
        )

        // Badge circle
        Box(
            modifier = Modifier
                .size(16.dp)
                .clip(CircleShape)
                .background(badgeColor),
            contentAlignment = Alignment.Center
        ) {
            Text(
                text = badgeContent,
                fontSize = 9.sp,
                fontWeight = FontWeight.Bold,
                color = GlassBackground
            )
        }

        // Active glow
        if (isActive) {
            Box(
                modifier = Modifier
                    .size(28.dp)
                    .clip(CircleShape)
                    .background(
                        PrimaryColor.copy(alpha = 0.18f)
                    )
            )
        }
    }

    // Feature count indicator (shown as small dot)
    if (count > 0 && !isLocked) {
        Box(
            modifier = Modifier
                .size(6.dp)
                .clip(CircleShape)
                .background(
                    when {
                        isDone -> SuccessColor
                        isActive -> PrimaryColor
                        else -> Color.White.copy(alpha = 0.3f)
                    }
                )
        )
    }
}

/**
 * Vertical connector between phases
 */
@Composable
private fun PhaseConnector(
    isDone: Boolean
) {
    Spacer(
        modifier = Modifier
            .width(2.dp)
            .height(12.dp)
            .background(
                when {
                    isDone -> Color.White.copy(alpha = 0.50f)
                    else -> Color.White.copy(alpha = 0.12f)
                }
            )
    )
}
