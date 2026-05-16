package com.nars.maplibre.ui.components

import androidx.compose.foundation.background
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
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.ui.theme.GlassBackground
import com.nars.maplibre.ui.theme.GlassBorder
import com.nars.maplibre.ui.theme.TextPrimary
import com.nars.maplibre.ui.theme.TextSecondary
import com.nars.maplibre.ui.theme.TextMuted

/**
 * Info panel showing feature counts
 * Matches nars-vite-maplibre design with glass-morphism style
 */
@Composable
fun InfoPanel(
    featureCounts: Map<String, Int>,
    totalFeatures: Int,
    currentPhaseKey: String?,
    modifier: Modifier = Modifier
) {
    Card(
        modifier = modifier
            .fillMaxWidth()
            .padding(12.dp),
        shape = RoundedCornerShape(12.dp),
        colors = CardDefaults.cardColors(
            containerColor = GlassBackground.copy(alpha = 0.75f)
        ),
        elevation = CardDefaults.cardElevation(defaultElevation = 0.dp)
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(14.dp)
        ) {
            // Header with total
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    text = "Feature Summary",
                    fontSize = 13.sp,
                    fontWeight = androidx.compose.ui.text.font.FontWeight.SemiBold,
                    color = TextSecondary
                )

                // Total badge
                TotalBadge(total = totalFeatures)
            }

            Spacer(modifier = Modifier.height(10.dp))

            // Phase counts
            Column(
                verticalArrangement = Arrangement.spacedBy(5.dp)
            ) {
                Phases.ALL.forEach { phase ->
                    val count = featureCounts[phase.key] ?: 0
                    val isCurrentPhase = currentPhaseKey == phase.key
                    val phaseColor = phase.parsedColor

                    PhaseCountRow(
                        phaseName = phase.label,
                        count = count,
                        phaseColor = phaseColor,
                        isHighlighted = isCurrentPhase
                    )
                }
            }
        }
    }
}

/**
 * Compact info panel variant
 */
@Composable
fun CompactInfoPanel(
    featureCounts: Map<String, Int>,
    totalFeatures: Int,
    modifier: Modifier = Modifier
) {
    Box(
        modifier = modifier
            .clip(RoundedCornerShape(10.dp))
            .background(GlassBackground.copy(alpha = 0.7f))
            .padding(12.dp)
    ) {
        Column(
            verticalArrangement = Arrangement.spacedBy(4.dp)
        ) {
            // Header
            Row(
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    text = "Features",
                    fontSize = 12.sp,
                    fontWeight = androidx.compose.ui.text.font.FontWeight.SemiBold,
                    color = TextSecondary
                )

                // Total badge (small)
                Box(
                    modifier = Modifier
                        .clip(RoundedCornerShape(8.dp))
                        .background(MaterialTheme.colorScheme.primary)
                        .padding(horizontal = 6.dp, vertical = 2.dp)
                ) {
                    Text(
                        text = totalFeatures.toString(),
                        fontSize = 11.sp,
                        color = Color.White
                    )
                }
            }

            // Quick counts
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceEvenly
            ) {
                Phases.ALL.take(4).forEach { phase ->
                    val count = featureCounts[phase.key] ?: 0
                    val phaseColor = phase.parsedColor

                    Column(
                        horizontalAlignment = Alignment.CenterHorizontally
                    ) {
                        Text(
                            text = count.toString(),
                            fontSize = 14.sp,
                            fontWeight = androidx.compose.ui.text.font.FontWeight.Bold,
                            color = phaseColor
                        )
                        Text(
                            text = phase.label.take(3),
                            fontSize = 9.sp,
                            color = TextMuted
                        )
                    }
                }
            }
        }
    }
}

/**
 * Total features badge
 */
@Composable
private fun TotalBadge(total: Int) {
    Row(
        modifier = Modifier
            .clip(RoundedCornerShape(12.dp))
            .background(MaterialTheme.colorScheme.primary)
            .padding(horizontal = 10.dp, vertical = 3.dp),
        horizontalArrangement = Arrangement.Center,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(
            text = total.toString(),
            fontSize = 12.sp,
            fontWeight = androidx.compose.ui.text.font.FontWeight.Bold,
            color = Color.White,
        )
        Spacer(modifier = Modifier.width(4.dp))
        Text(
            text = "total",
            fontSize = 10.sp,
            color = Color.White.copy(alpha = 0.75f)
        )
    }
}

/**
 * Phase count row
 */
@Composable
private fun PhaseCountRow(
    phaseName: String,
    count: Int,
    phaseColor: Color,
    isHighlighted: Boolean
) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Row(
            verticalAlignment = Alignment.CenterVertically
        ) {
            // Color indicator
            Box(
                modifier = Modifier
                    .size(7.dp)
                    .clip(RoundedCornerShape(2.dp))
                    .background(phaseColor)
            )

            Spacer(modifier = Modifier.width(7.dp))

            Text(
                text = phaseName,
                fontSize = 12.sp,
                color = if (isHighlighted) TextPrimary else TextSecondary.copy(alpha = 0.7f)
            )
        }

        Text(
            text = count.toString(),
            fontSize = 13.sp,
            fontWeight = if (isHighlighted) androidx.compose.ui.text.font.FontWeight.SemiBold else androidx.compose.ui.text.font.FontWeight.Normal,
            color = if (isHighlighted) phaseColor else TextSecondary.copy(alpha = 0.7f)
        )
    }
}
