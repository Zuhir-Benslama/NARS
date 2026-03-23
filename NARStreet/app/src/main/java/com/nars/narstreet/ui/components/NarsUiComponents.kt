package com.nars.narstreet.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nars.narstreet.ui.theme.*

// ── Phase index constants ─────────────────────────────────────────────────────

object PhaseIndex {
    const val AREAS       = 0
    const val DISTRICTS   = 1
    const val CITY_CENTER = 2
    const val ROADS       = 3
    const val ENTRANCES   = 4
    const val BUILDINGS   = 5
    const val SPACES      = 6
    const val PANELS      = 7
}

data class PhaseStep(val index: Int, val label: String, val color: Color, val route: String)

val PHASE_STEPS = listOf(
    PhaseStep(0, "Areas",     PhaseAreaColor,     "phase01"),
    PhaseStep(1, "Districts", PhaseDistrictColor, "phase02"),
    PhaseStep(2, "City Ctr",  PhaseCityCtrColor,  "phase03"),
    PhaseStep(3, "Roads",     PhaseRoadColor,     "phase04"),
    PhaseStep(4, "Entrances", PhaseEntrColor,     "phase05"),
    PhaseStep(5, "Buildings", PhaseBuildColor,    "phase06"),
    PhaseStep(6, "Spaces",    PhaseSpaceColor,    "phase07"),
    PhaseStep(7, "Panels",    PhasePanelColor,    "phase08"),
)

// ─────────────────────────────────────────────────────────────────────────────
// PhaseBar — vertical strip for the right edge of the screen
// ─────────────────────────────────────────────────────────────────────────────

@Composable
fun PhaseBar(
    currentPhaseIndex: Int,
    onPhaseClick: (Int) -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(
        modifier            = modifier,
        verticalArrangement = Arrangement.spacedBy(6.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        PHASE_STEPS.forEachIndexed { i, step ->
            val isDone   = i < currentPhaseIndex
            val isActive = i == currentPhaseIndex

            val bgColor     = when { isActive -> Color(0x47FFFFFF); isDone -> Color(0x26FFFFFF); else -> Color(0x0DFFFFFF) }
            val borderColor = when { isActive -> Color(0xCCFFFFFF); isDone -> Color(0x59FFFFFF); else -> Color(0x26FFFFFF) }
            val dotColor    = when { isActive -> step.color; isDone -> step.color.copy(alpha = 0.7f); else -> Color(0x33FFFFFF) }

            Box(
                modifier         = Modifier
                    .size(28.dp)
                    .clip(CircleShape)
                    .background(bgColor)
                    .border(1.5.dp, borderColor, CircleShape)
                    .clickable { onPhaseClick(i) },
                contentAlignment = Alignment.Center,
            ) {
                Box(
                    modifier         = Modifier
                        .size(20.dp)
                        .clip(CircleShape)
                        .background(dotColor),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(
                        text       = if (isDone) "✓" else (i + 1).toString(),
                        fontSize   = 9.sp,
                        fontWeight = FontWeight.ExtraBold,
                        color      = Color.White,
                        textAlign  = TextAlign.Center,
                        lineHeight = 9.sp,
                    )
                }
            }
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// InfoPanel
// ─────────────────────────────────────────────────────────────────────────────

data class NarsFeatureCounts(
    val areas: Int = 0, val districts: Int = 0, val cityCenter: Int = 0,
    val roads: Int = 0, val mainEntrances: Int = 0, val secEntrances: Int = 0,
    val buildings: Int = 0, val spaces: Int = 0,
)

@Composable
fun InfoPanel(counts: NarsFeatureCounts, modifier: Modifier = Modifier) {
    Box(
        modifier = modifier.clip(RoundedCornerShape(10.dp)).background(GlassBg)
            .border(1.dp, GlassBorder, RoundedCornerShape(10.dp))
            .padding(horizontal = 14.dp, vertical = 10.dp),
    ) {
        Column(verticalArrangement = Arrangement.spacedBy(3.dp)) {
            Text("Survey Progress", fontSize = 11.sp, fontWeight = FontWeight.Bold, color = TextPrimary)
            Spacer(Modifier.height(2.dp))
            InfoRow("⬟",  "Areas",        counts.areas)
            InfoRow("📍", "City Center",  counts.cityCenter, if (counts.cityCenter > 0) "✓" else "—")
            InfoRow("🏘️", "Districts",    counts.districts)
            InfoRow("🛣️", "Roads",        counts.roads)
            InfoRow("🚪", "Main Entr.",   counts.mainEntrances)
            InfoRow("🔢", "Sec. Entr.",   counts.secEntrances)
            InfoRow("🏛️", "Buildings",    counts.buildings)
            InfoRow("🌳", "Spaces",       counts.spaces)
        }
    }
}

@Composable
private fun InfoRow(emoji: String, label: String, count: Int, valueText: String? = null) {
    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
        Text("$emoji $label", fontSize = 11.sp, color = TextSecondary, modifier = Modifier.weight(1f))
        Text(valueText ?: count.toString(), fontSize = 11.sp, fontWeight = FontWeight.SemiBold, color = TextPrimary)
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// AccountMenu — avatar only, uses DropdownMenu so tapping avatar always toggles
// ─────────────────────────────────────────────────────────────────────────────

@Composable
fun AccountMenu(
    username: String,
    onLogout: () -> Unit,
    onSettings: () -> Unit = {},
    modifier: Modifier = Modifier,
) {
    var expanded by remember { mutableStateOf(false) }
    val initial = username.firstOrNull()?.uppercaseChar()?.toString() ?: "U"

    Box(modifier = modifier) {
        // Avatar — tap opens, tap again closes (DropdownMenu handles dismiss correctly)
        Box(
            modifier = Modifier
                .size(36.dp)
                .clip(CircleShape)
                .background(Brush.linearGradient(listOf(Color(0xFF667EEA), Color(0xFF764BA2))))
                .border(1.5.dp, GlassBorder, CircleShape)
                .clickable { expanded = true },
            contentAlignment = Alignment.Center,
        ) {
            Text(initial, fontSize = 14.sp, fontWeight = FontWeight.Bold,
                color = Color.White, textAlign = TextAlign.Center)
        }

        // DropdownMenu closes cleanly when tapping outside OR the avatar again
        DropdownMenu(
            expanded         = expanded,
            onDismissRequest = { expanded = false },
        ) {
            DropdownMenuItem(
                text         = { Text("⚙️  Settings", fontSize = 13.sp) },
                onClick      = { expanded = false; onSettings() },
                contentPadding = PaddingValues(horizontal = 16.dp, vertical = 4.dp),
            )
            HorizontalDivider()
            DropdownMenuItem(
                text         = { Text("🚪  Logout", fontSize = 13.sp, color = Color(0xFFE74C3C)) },
                onClick      = { expanded = false; onLogout() },
                contentPadding = PaddingValues(horizontal = 16.dp, vertical = 4.dp),
            )
        }
    }
}
