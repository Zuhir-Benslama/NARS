package com.nars.narstreet.ui.phase04

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nars.narstreet.R
import com.nars.narstreet.data.model.RoadEntity
import com.nars.narstreet.data.model.SyncStatus
import android.webkit.WebView
import com.nars.narstreet.ui.components.*
import com.nars.narstreet.ui.theme.*

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun Phase04Screen(
    viewModel: Phase04ViewModel = hiltViewModel(),
    onLogout: () -> Unit,
    onNavigateTo: (String) -> Unit,
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()

    // True while the edit sheet OR info sheet is open — locks map gestures
    val mapLocked = state.editingRoad != null || state.infoRoad != null

    PhaseScaffold(
        title             = "Roads",
        syncState         = state.syncState,
        currentPhaseIndex = PhaseIndex.ROADS,
        onNavigateTo      = onNavigateTo,
        username          = state.username,
        onLogout          = { viewModel.logout(onLogout) },
    ) { _ ->
        if (state.isLoading) {
            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                CircularProgressIndicator(color = NarsTeal)
            }
        } else {
            Box(Modifier.fillMaxSize()) {
                var webView by remember { mutableStateOf<WebView?>(null) }
                var ready   by remember { mutableStateOf(false) }

                NarsMapView(
                    modifier = Modifier.fillMaxSize(),
                    onBridge = { b, wv ->
                        b.onMapReady = { ready = true }
                        b.onFeatureClick = { _, dbId ->
                            state.roads.find { it.remoteId == dbId }?.let { viewModel.showInfo(it) }
                        }
                        b.onFeatureLongClick = { _, dbId ->
                            state.roads.find { it.remoteId == dbId }?.let { viewModel.startEditing(it) }
                        }
                        webView = wv
                    },
                )

                // Push data to map whenever state changes and map is ready
                val L = state.mapLayers
                LaunchedEffect(ready, L) {
                    if (!ready) return@LaunchedEffect
                    
                        val wv = webView ?: return@LaunchedEffect
                        val cc = L.communeCenter
                        wv.flyTo(cc.latitude, cc.longitude, 14.0)
                        wv.setContext(L.communeContext)
                        wv.setAreas(L.areaPolygons, L.areaLabels)
                        L.cityCenterPoint?.let { wv.setCityCenter(it.latitude, it.longitude) }
                        wv.setRoads(L.roadPolylines, L.roadDbIds, L.roadLabels, L.highlightPolyline)
                }
            }
        }
    }

    // ── Error banner — non-blocking, auto-dismisses when error clears ──────────
    state.loadError?.let { err ->
        LaunchedEffect(err) {
            kotlinx.coroutines.delay(4000)
            viewModel.clearError()
        }
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .background(androidx.compose.ui.graphics.Color(0xFFB71C1C))
                .padding(horizontal = 16.dp, vertical = 8.dp),
        ) {
            Text(err, color = androidx.compose.ui.graphics.Color.White,
                fontSize = 12.sp, fontWeight = FontWeight.Medium)
        }
    }

    // ── Info sheet (single tap) ────────────────────────────────────────────────
    state.infoRoad?.let { road ->
        RoadInfoSheet(road = road, onDismiss = viewModel::dismissInfo)
    }

    // ── Edit sheet (long press) ────────────────────────────────────────────────
    state.editingRoad?.let { road ->
        RoadCharacteristicsSheet(
            road      = road,
            onDismiss = viewModel::cancelEditing,
            onSave    = { lanes, traffic, trad, median, greenery, deadEnd ->
                viewModel.saveCharacteristics(road, lanes, traffic, trad, median, greenery, deadEnd)
            },
        )
    }
}

// ── Info bottom sheet ─────────────────────────────────────────────────────────

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun RoadInfoSheet(road: RoadEntity, onDismiss: () -> Unit) {
    ModalBottomSheet(
        onDismissRequest = onDismiss,
        containerColor   = NarsNavy,
        dragHandle       = { GlassDragHandle() },
    ) {
        Column(
            modifier            = Modifier
                .padding(horizontal = 24.dp)
                .padding(bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text(road.label, fontSize = 17.sp, fontWeight = FontWeight.Bold, color = TextPrimary)

            val layerLabel = road.layer.replace("_", " ").replaceFirstChar { it.uppercase() }
            InfoChip(layerLabel, Color(0xFF3498DB))

            HorizontalDivider(color = Color(0x1AFFFFFF))

            if (road.lanes != null || road.trafficCapacity != null || road.tradActivity != null) {
                InfoRow("Lanes",    road.lanes?.toString() ?: "—")
                InfoRow("Traffic",  road.trafficCapacity ?: "—")
                InfoRow("Activity", road.tradActivity ?: "—")
                InfoRow("Median strip",      if (road.hasMedianStrip == true) "Yes" else "No")
                InfoRow("Greenery",          if (road.hasGreenery == true) "Yes" else "No")
                InfoRow("Dead-end",          if (road.isDeadEnd == true) "Yes" else "No")
            } else {
                Text("No characteristics recorded yet.\nLong-press the road on the map to fill them in.",
                    fontSize = 13.sp, color = TextMuted)
            }

            NarsSyncBadge(road.syncStatus)
        }
    }
}

// ── Edit characteristics sheet ────────────────────────────────────────────────

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun RoadCharacteristicsSheet(
    road: RoadEntity,
    onDismiss: () -> Unit,
    onSave: (Int?, String?, String?, Boolean?, Boolean?, Boolean?) -> Unit,
) {
    var lanes    by remember { mutableStateOf(road.lanes?.toString() ?: "") }
    var traffic  by remember { mutableStateOf(road.trafficCapacity ?: "MEDIUM") }
    var trad     by remember { mutableStateOf(road.tradActivity ?: "LOW") }
    var median   by remember { mutableStateOf(road.hasMedianStrip ?: false) }
    var greenery by remember { mutableStateOf(road.hasGreenery ?: false) }
    var deadEnd  by remember { mutableStateOf(road.isDeadEnd ?: false) }

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState       = rememberModalBottomSheetState(skipPartiallyExpanded = true),
        containerColor   = NarsNavy,
        dragHandle       = { GlassDragHandle() },
    ) {
        Column(
            modifier            = Modifier
                .padding(horizontal = 24.dp)
                .padding(bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Text(road.label, fontSize = 17.sp, fontWeight = FontWeight.Bold, color = TextPrimary)

            GlassTextField(value = lanes, onValueChange = { lanes = it.filter(Char::isDigit) },
                label = stringResource(R.string.road_lanes))
            GlassSegmentedPicker(label = stringResource(R.string.road_traffic),
                options = listOf("HIGH", "MEDIUM", "LOW"), selected = traffic, onSelect = { traffic = it })
            GlassSegmentedPicker(label = stringResource(R.string.road_trad),
                options = listOf("HIGH", "MEDIUM", "LOW"), selected = trad, onSelect = { trad = it })
            GlassToggle(stringResource(R.string.road_median),   median,   { median = it })
            GlassToggle(stringResource(R.string.road_greenery), greenery, { greenery = it })
            GlassToggle(stringResource(R.string.road_dead_end), deadEnd,  { deadEnd = it })

            Button(
                onClick  = { onSave(lanes.toIntOrNull(), traffic, trad, median, greenery, deadEnd) },
                modifier = Modifier.fillMaxWidth().height(48.dp),
                shape    = RoundedCornerShape(8.dp),
                colors   = ButtonDefaults.buttonColors(containerColor = NarsTeal, contentColor = Color.White),
            ) { Text(stringResource(R.string.road_save), fontWeight = FontWeight.Bold) }
        }
    }
}

// ── Shared helpers ────────────────────────────────────────────────────────────

@Composable
private fun GlassDragHandle() {
    Box(
        modifier = Modifier
            .padding(vertical = 12.dp)
            .size(width = 36.dp, height = 4.dp)
            .clip(RoundedCornerShape(2.dp))
            .background(Color(0x33FFFFFF))
    )
}

@Composable
private fun InfoChip(label: String, color: Color) {
    Box(
        modifier = Modifier
            .clip(RoundedCornerShape(50.dp))
            .background(color.copy(alpha = 0.15f))
            .border(1.dp, color.copy(alpha = 0.4f), RoundedCornerShape(50.dp))
            .padding(horizontal = 10.dp, vertical = 4.dp),
    ) { Text(label, fontSize = 11.sp, fontWeight = FontWeight.Bold, color = color) }
}

@Composable
private fun InfoRow(label: String, value: String) {
    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
        Text(label, fontSize = 13.sp, color = TextSecondary)
        Text(value, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = TextPrimary)
    }
}

@Composable
private fun NarsSyncBadge(status: SyncStatus) {
    val (label, color) = when (status) {
        SyncStatus.PENDING -> "Pending sync" to NarsTeal
        SyncStatus.SYNCED  -> "Synced"       to Color(0xFF27AE60)
        SyncStatus.ERROR   -> "Sync error"   to SyncError
    }
    Box(
        modifier = Modifier
            .clip(RoundedCornerShape(50.dp))
            .background(color.copy(alpha = 0.15f))
            .border(1.dp, color.copy(alpha = 0.4f), RoundedCornerShape(50.dp))
            .padding(horizontal = 10.dp, vertical = 4.dp),
    ) { Text(label, fontSize = 10.sp, fontWeight = FontWeight.Bold, color = color) }
}

@Composable
private fun GlassTextField(value: String, onValueChange: (String) -> Unit, label: String) {
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(label, fontSize = 12.sp, fontWeight = FontWeight.SemiBold, color = TextSecondary)
        OutlinedTextField(
            value = value, onValueChange = onValueChange, singleLine = true,
            modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(8.dp),
            colors = OutlinedTextFieldDefaults.colors(
                focusedContainerColor   = Color(0x1AFFFFFF),
                unfocusedContainerColor = Color(0x1AFFFFFF),
                focusedBorderColor      = Color(0x8CFFFFFF),
                unfocusedBorderColor    = Color(0x33FFFFFF),
                focusedTextColor        = TextPrimary, unfocusedTextColor = TextPrimary,
                cursorColor             = TextPrimary,
            ),
        )
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun GlassSegmentedPicker(label: String, options: List<String>, selected: String, onSelect: (String) -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(label, fontSize = 12.sp, fontWeight = FontWeight.SemiBold, color = TextSecondary)
        SingleChoiceSegmentedButtonRow(modifier = Modifier.fillMaxWidth()) {
            options.forEachIndexed { i, opt ->
                SegmentedButton(
                    selected = opt == selected, onClick = { onSelect(opt) },
                    shape    = SegmentedButtonDefaults.itemShape(i, options.size),
                    colors   = SegmentedButtonDefaults.colors(
                        activeContainerColor   = Color(0x33FFFFFF), activeContentColor  = TextPrimary,
                        inactiveContainerColor = Color(0x0DFFFFFF), inactiveContentColor = TextSecondary,
                        activeBorderColor      = Color(0x66FFFFFF), inactiveBorderColor  = Color(0x1AFFFFFF),
                    ),
                ) { Text(opt, fontSize = 12.sp, fontWeight = FontWeight.SemiBold) }
            }
        }
    }
}

@Composable
private fun GlassToggle(label: String, value: Boolean, onToggle: (Boolean) -> Unit) {
    Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween) {
        Text(label, fontSize = 14.sp, color = TextSecondary)
        Switch(checked = value, onCheckedChange = onToggle,
            colors = SwitchDefaults.colors(
                checkedThumbColor   = Color.White, checkedTrackColor   = NarsTeal,
                uncheckedThumbColor = TextMuted,   uncheckedTrackColor = Color(0x33FFFFFF),
            ))
    }
}

