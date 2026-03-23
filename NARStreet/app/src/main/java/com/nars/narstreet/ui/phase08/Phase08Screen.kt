package com.nars.narstreet.ui.phase08

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nars.narstreet.R
import com.nars.narstreet.data.model.PanelEntity
import android.webkit.WebView
import com.nars.narstreet.ui.components.*
import com.nars.narstreet.ui.theme.*
import org.maplibre.android.geometry.LatLng

private const val PANEL_COLOR = "#9B59B6"

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun Phase08Screen(
    panelId: Long,
    viewModel: Phase08ViewModel = hiltViewModel(),
    onNavigateTo: (String) -> Unit,
    onBack: () -> Unit,
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()

    if (state.isSaved) {
        LaunchedEffect(Unit) { kotlinx.coroutines.delay(800); onBack() }
    }

    // ── Detail view — map + checklist for one panel ───────────────────────────
    if (panelId > 0 && state.panel != null) {
        val panel = state.panel!!
        PhaseScaffold(
            title             = panel.label,
            syncState         = state.syncState,
            currentPhaseIndex = PhaseIndex.PANELS,
            onNavigateTo      = onNavigateTo,
            onBack            = onBack,
            username          = state.username,
            onLogout          = { onNavigateTo("login") },
        ) { _ ->
            Column(Modifier.fillMaxSize()) {
                // Map — top third, panel marker draggable via Geoman
                Box(Modifier.fillMaxWidth().weight(1f)) {
                    var wvDetail by remember { mutableStateOf<WebView?>(null) }
                    var readyDetail by remember { mutableStateOf(false) }
                    NarsMapView(modifier = Modifier.fillMaxSize(), onBridge = { b, wv ->
                        b.onMapReady = { readyDetail = true }
                        b.onGeometryChanged = { _, pts ->
                            // Panel is a point — save updated lat/lng
                            val coords = parseGeometryPoints(pts)
                            if (coords.isNotEmpty()) viewModel.onPanelMoved(coords.first())
                        }
                        wvDetail = wv
                    })
                    LaunchedEffect(readyDetail, panel) {
                            if (!readyDetail) return@LaunchedEffect
                            val wv = wvDetail ?: return@LaunchedEffect
                            val L = state.mapLayers
                            wv.flyTo(panel.lat, panel.lng, 16.0)
                            wv.setContext(L.communeContext)
                            wv.setAreas(L.areaPolygons, L.areaLabels)
                            wv.setDistricts(L.districtPolygons, L.districtLabels)
                            wv.setRoads(L.roadPolylines, L.roadDbIds)
                            wv.setBuildings(L.buildingPoints, L.buildingDbIds, L.buildingLabels)
                            wv.setSpaces(L.spacePolygons, L.spaceLabels)
                            wv.startEditPoint(panel.id.toString(), panel.lat, panel.lng, PANEL_COLOR)
                    }
                }
                // Checklist — bottom portion
                PanelChecklistContent(
                    panel    = panel,
                    onSave   = viewModel::saveChecklist,
                    modifier = Modifier.weight(2f),
                )
            }
        }
        return
    }

    // ── List view — all panels on map ─────────────────────────────────────────
    PhaseScaffold(
        title             = stringResource(R.string.phase08_title),
        syncState         = state.syncState,
        currentPhaseIndex = PhaseIndex.PANELS,
        onNavigateTo      = onNavigateTo,
        onBack            = onBack,
        username          = state.username,
        onLogout          = { onNavigateTo("login") },
    ) { _ ->
        Box(Modifier.fillMaxSize()) {
            when {
                state.isLoading -> Box(Modifier.fillMaxSize(), Alignment.Center) {
                    CircularProgressIndicator(color = NarsTeal)
                }
                state.panels.isEmpty() -> Box(Modifier.fillMaxSize(), Alignment.Center) {
                    Text(
                        "No naming panels found.\nData syncs from the server.",
                        color = TextSecondary, textAlign = TextAlign.Center,
                    )
                }
                else -> {
                    var wv08 by remember { mutableStateOf<WebView?>(null) }
                    var ready08 by remember { mutableStateOf(false) }
                    val L = state.mapLayers
                    NarsMapView(modifier = Modifier.fillMaxSize(), onBridge = { b, wv ->
                        b.onMapReady = { ready08 = true }
                        b.onFeatureClick = { _, id ->
                            state.panels.find { it.id == id }?.let { viewModel.showInfo(it) }
                        }
                        wv08 = wv
                    })
                    LaunchedEffect(ready08, L, state.panels) {
                            if (!ready08) return@LaunchedEffect
                            val wv = wv08 ?: return@LaunchedEffect
                            val cc = L.communeCenter
                            wv.flyTo(cc.latitude, cc.longitude, 13.0)
                            wv.setContext(L.communeContext)
                            wv.setAreas(L.areaPolygons, L.areaLabels)
                            wv.setDistricts(L.districtPolygons, L.districtLabels)
                            wv.setRoads(L.roadPolylines, L.roadDbIds)
                            wv.setBuildings(L.buildingPoints, L.buildingDbIds, L.buildingLabels)
                            wv.setSpaces(L.spacePolygons, L.spaceLabels)
                            wv.setPanels(L.panelPoints, state.panels.map { it.id }, L.panelLabels)
                    }
                }
            }
        }
    }

    state.infoPanel?.let { panel ->
        PanelInfoSheet(panel, onDismiss = viewModel::dismissInfo,
            onEdit = { onNavigateTo("phase08/${panel.id}") })
    }
}

@Composable
private fun PanelChecklistContent(
    panel: PanelEntity,
    onSave: (Boolean, Boolean?) -> Unit,
    modifier: Modifier = Modifier,
) {
    var isPlaced  by remember { mutableStateOf(panel.isPlaced ?: false) }
    var isCorrect by remember { mutableStateOf(panel.isCorrectLocation) }

    Column(
        modifier            = modifier.fillMaxWidth().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(20.dp),
    ) {
        Text(panel.label, fontSize = 17.sp, fontWeight = FontWeight.Bold, color = TextPrimary)
        Text("%.5f, %.5f".format(panel.lat, panel.lng), fontSize = 12.sp, color = TextMuted)

        HorizontalDivider(color = Color(0x1AFFFFFF))

        ChecklistQuestion(stringResource(R.string.panel_placed), isPlaced) {
            isPlaced = it; isCorrect = null
        }
        if (isPlaced) {
            ChecklistQuestion(stringResource(R.string.panel_correct_location), isCorrect ?: false) {
                isCorrect = it
            }
        }

        val notice: Triple<String, Color, androidx.compose.ui.graphics.vector.ImageVector>? = when {
            !isPlaced          -> Triple(stringResource(R.string.panel_order),    Color(0x33E24B4A), Icons.Default.AddShoppingCart)
            isCorrect == false -> Triple(stringResource(R.string.panel_relocate), Color(0x33E24B4A), Icons.Default.WrongLocation)
            isCorrect == true  -> Triple("Panel is correctly placed",             Color(0x33279E60), Icons.Default.CheckCircle)
            else -> null
        }

        notice?.let { (noticeText, noticeColor, noticeIcon) ->
            Card(colors = CardDefaults.cardColors(containerColor = noticeColor)) {
                Row(
                    modifier              = Modifier.padding(16.dp),
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                    verticalAlignment     = Alignment.CenterVertically,
                ) {
                    Icon(noticeIcon, null, tint = TextSecondary)
                    Text(noticeText, fontSize = 14.sp, color = TextSecondary)
                }
            }
        }

        Spacer(Modifier.weight(1f))

        Button(
            onClick  = { onSave(isPlaced, isCorrect) },
            modifier = Modifier.fillMaxWidth().height(48.dp),
            shape    = RoundedCornerShape(8.dp),
            colors   = ButtonDefaults.buttonColors(containerColor = NarsTeal, contentColor = Color.White),
        ) { Text(stringResource(R.string.save), fontWeight = FontWeight.Bold) }
    }
}

@Composable
private fun ChecklistQuestion(question: String, value: Boolean, onToggle: (Boolean) -> Unit) {
    Row(
        modifier              = Modifier.fillMaxWidth(),
        verticalAlignment     = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(question, fontSize = 14.sp, color = TextSecondary, modifier = Modifier.weight(1f))
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            FilterChip(!value, { onToggle(false) }, { Text(stringResource(R.string.no)) })
            FilterChip(value,  { onToggle(true) },  { Text(stringResource(R.string.yes)) })
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun PanelInfoSheet(panel: PanelEntity, onDismiss: () -> Unit, onEdit: () -> Unit) {
    ModalBottomSheet(
        onDismissRequest = onDismiss,
        containerColor   = NarsNavy,
        dragHandle       = { GlassDragHandle() },
    ) {
        Column(
            modifier            = Modifier.padding(horizontal = 24.dp).padding(bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text(panel.label, fontSize = 17.sp, fontWeight = FontWeight.Bold, color = TextPrimary)
            Text("%.5f, %.5f".format(panel.lat, panel.lng), fontSize = 12.sp, color = TextMuted)
            HorizontalDivider(color = Color(0x1AFFFFFF))
            val statusText = when {
                panel.isPlaced == true && panel.isCorrectLocation == true  -> "✓ Correctly placed"
                panel.isPlaced == true && panel.isCorrectLocation == false -> "⚠ Needs relocation"
                panel.isPlaced == false                                    -> "✗ Not placed"
                else                                                       -> "Not checked yet"
            }
            Text(statusText, fontSize = 13.sp, color = TextSecondary)
            Button(
                onClick  = onEdit,
                modifier = Modifier.fillMaxWidth().height(44.dp),
                shape    = RoundedCornerShape(8.dp),
                colors   = ButtonDefaults.buttonColors(containerColor = NarsTeal, contentColor = Color.White),
            ) { Text("Check Panel", fontWeight = FontWeight.Bold) }
        }
    }
}

@Composable
private fun GlassDragHandle() {
    Box(
        modifier = Modifier
            .padding(vertical = 12.dp)
            .size(width = 36.dp, height = 4.dp)
            .clip(RoundedCornerShape(2.dp))
            .background(Color(0x33FFFFFF)),
    )
}
