package com.nars.narstreet.ui.phase06

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
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nars.narstreet.R
import com.nars.narstreet.data.model.BuildingEntity
import android.webkit.WebView
import com.nars.narstreet.ui.components.*
import com.nars.narstreet.ui.theme.*

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun Phase06Screen(
    buildingId: Long,
    viewModel: Phase06ViewModel = hiltViewModel(),
    onNavigateTo: (String) -> Unit,
    onBack: () -> Unit,
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()

    // Buildings are POINT features — no polygon editor.
    // buildingId > 0 navigates here from a long-press; just show the info sheet.
    LaunchedEffect(buildingId) {
        if (buildingId > 0) {
            val building = state.buildings.find { it.id == buildingId }
            if (building != null) viewModel.showInfo(building)
        }
    }

    PhaseScaffold(
        title             = stringResource(R.string.phase06_title),
        syncState         = state.syncState,
        currentPhaseIndex = PhaseIndex.BUILDINGS,
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
                state.buildings.isEmpty() -> Box(Modifier.fillMaxSize(), Alignment.Center) {
                    Text(
                        "No buildings found.\nData syncs from the server.",
                        color = TextSecondary, textAlign = TextAlign.Center,
                    )
                }
                else -> {
                    var wv06 by remember { mutableStateOf<WebView?>(null) }
                    var ready06 by remember { mutableStateOf(false) }
                    val L = state.mapLayers
                    NarsMapView(modifier = Modifier.fillMaxSize(), onBridge = { b, wv ->
                        b.onMapReady = { ready06 = true }
                        b.onFeatureClick = { _, id -> state.buildings.find { it.id == id }?.let { viewModel.showInfo(it) } }
                        wv06 = wv
                    })
                    LaunchedEffect(ready06, L, state.buildings) {
                            if (!ready06) return@LaunchedEffect
                            val wv = wv06 ?: return@LaunchedEffect
                            wv.flyTo(state.communeCenter.latitude, state.communeCenter.longitude, 13.0)
                            wv.setContext(L.communeContext)
                            wv.setAreas(L.areaPolygons, L.areaLabels)
                            wv.setBuildings(L.buildingPoints, L.buildingDbIds, L.buildingLabels)
                    }
                }
            }
        }
    }

    state.infoBuilding?.let { b ->
        BuildingInfoSheet(b, onDismiss = viewModel::dismissInfo)
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun BuildingInfoSheet(building: BuildingEntity, onDismiss: () -> Unit) {
    ModalBottomSheet(
        onDismissRequest = onDismiss,
        containerColor   = NarsNavy,
        dragHandle       = { GlassDragHandle() },
    ) {
        Column(
            modifier            = Modifier.padding(horizontal = 24.dp).padding(bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text(building.label, fontSize = 17.sp, fontWeight = FontWeight.Bold, color = TextPrimary)
            Box(
                modifier = Modifier
                    .clip(RoundedCornerShape(50.dp))
                    .background(Color(0x33E67E22))
                    .border(1.dp, Color(0x80E67E22), RoundedCornerShape(50.dp))
                    .padding(horizontal = 10.dp, vertical = 4.dp),
            ) {
                Text(
                    building.layer.replace("_", " ").replaceFirstChar { it.uppercase() },
                    fontSize = 11.sp, fontWeight = FontWeight.Bold, color = Color(0xFFE67E22),
                )
            }
            HorizontalDivider(color = Color(0x1AFFFFFF))
            Text(
                "%.5f, %.5f".format(building.lat, building.lng),
                fontSize = 12.sp, color = TextMuted,
            )
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
