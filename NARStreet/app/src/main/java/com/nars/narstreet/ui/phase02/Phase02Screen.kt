package com.nars.narstreet.ui.phase02

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Save
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nars.narstreet.data.model.DistrictEntity
import android.webkit.WebView
import com.nars.narstreet.ui.components.*
import com.nars.narstreet.ui.theme.*

private val DISTRICT_COLOR = "#F39C12"

private fun districtTypeLabel(layer: String) = when (layer) {
    "housing_estate"       -> "Housing Estate"
    "urban_pole"           -> "Urban Pole"
    "district"             -> "District"
    "trad_activities_zone" -> "Trad. Activities Zone"
    "industry_zone"        -> "Industry Zone"
    else -> layer.replace("_", " ").replaceFirstChar { it.uppercase() }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun Phase02Screen(
    districtId: Long = 0L,
    onNavigateTo: (String) -> Unit,
    onBack: () -> Unit,
    viewModel: Phase02ViewModel = hiltViewModel(),
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()

    if (state.isSaved) {
        LaunchedEffect(Unit) { kotlinx.coroutines.delay(800); onBack() }
    }

    // ── Detail view — polygon editor for one district ─────────────────────────
    if (districtId > 0 && state.editingDistrict != null) {
        val district = state.editingDistrict!!
        PhaseScaffold(
            title             = district.label,
            syncState         = state.syncState,
            currentPhaseIndex = PhaseIndex.DISTRICTS,
            onNavigateTo      = onNavigateTo,
            onBack            = onBack,
            username          = state.username,
            onLogout          = { onNavigateTo("login") },
            floatingActionButton = {
                FloatingActionButton(onClick = viewModel::saveGeometry,
                    containerColor = NarsTeal, contentColor = Color.White) {
                    Icon(Icons.Default.Save, null)
                }
            },
        ) { _ ->
            Box(Modifier.fillMaxSize()) {
                var wvEdit02 by remember { mutableStateOf<WebView?>(null) }
                var readyEdit02 by remember { mutableStateOf(false) }
                NarsMapView(modifier = Modifier.fillMaxSize(), onBridge = { b, wv ->
                    b.onMapReady = { readyEdit02 = true }
                    b.onGeometryChanged = { _, pts ->
                        val coords = com.nars.narstreet.ui.components.parseGeometryPoints(pts)
                        if (coords.isNotEmpty()) viewModel.onVerticesMoved(coords)
                    }
                    wvEdit02 = wv
                })
                LaunchedEffect(readyEdit02, state.vertices) {
                        if (!readyEdit02) return@LaunchedEffect
                        val wv = wvEdit02 ?: return@LaunchedEffect
                        wv.setContext(state.communeContext)
                        wv.setAreas(state.mapLayers.areaPolygons, state.mapLayers.areaLabels)
                        wv.startEditPolygon(district.id.toString(), state.vertices, DISTRICT_COLOR)
                        if (state.vertices.isNotEmpty())
                            wv.flyTo(state.vertices.map{it.latitude}.average(), state.vertices.map{it.longitude}.average(), 14.0)
                        else wv.flyTo(state.communeCenter.latitude, state.communeCenter.longitude, 12.0)
                }
                // Type badge
                Box(
                    modifier = Modifier.align(Alignment.BottomCenter).padding(bottom = 80.dp)
                        .clip(RoundedCornerShape(50.dp)).background(Color(0xCCF39C12))
                        .padding(horizontal = 14.dp, vertical = 6.dp),
                ) {
                    Text(districtTypeLabel(district.layer),
                        fontSize = 12.sp, fontWeight = FontWeight.Bold, color = Color.White)
                }
            }
        }
        return
    }

    // ── List view — ALL districts on map ──────────────────────────────────────
    PhaseScaffold(
        title             = "Districts",
        syncState         = state.syncState,
        currentPhaseIndex = PhaseIndex.DISTRICTS,
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
                state.districts.isEmpty() -> Box(Modifier.fillMaxSize(), Alignment.Center) {
                    Text("No districts drawn yet.\nUse the web app to draw districts.",
                        color = TextSecondary, textAlign = TextAlign.Center)
                }
                else -> {
                    // Show ALL district polygons
                    var wv02 by remember { mutableStateOf<WebView?>(null) }
                    var ready02 by remember { mutableStateOf(false) }
                    val L = state.mapLayers
                    NarsMapView(modifier = Modifier.fillMaxSize(), onBridge = { b, wv ->
                        b.onMapReady = { ready02 = true }
                        b.onFeatureClick    = { _, id -> state.districts.find { it.id == id }?.let { viewModel.showInfo(it) } }
                        b.onFeatureLongClick = { _, id -> state.districts.find { it.id == id }?.let { onNavigateTo("phase02/${it.id}") } }
                        wv02 = wv
                    })
                    LaunchedEffect(ready02, L, state.districts) {
                            if (!ready02) return@LaunchedEffect
                            val wv = wv02 ?: return@LaunchedEffect
                            wv.flyTo(state.communeCenter.latitude, state.communeCenter.longitude, 12.0)
                            wv.setContext(state.communeContext)
                            wv.setAreas(L.areaPolygons, L.areaLabels)
                            wv.setDistricts(
                                state.districts.map { parseCoordinatesJson(it.coordinatesJson) },
                                state.districts.map { it.label }
                            )
                    }
                }
            }
        }
    }

    state.infoDistrict?.let { district ->
        DistrictInfoSheet(district, onDismiss = viewModel::dismissInfo,
            onEdit = { onNavigateTo("phase02/${district.id}") })
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun DistrictInfoSheet(district: DistrictEntity, onDismiss: () -> Unit, onEdit: () -> Unit) {
    ModalBottomSheet(onDismissRequest = onDismiss, containerColor = NarsNavy,
        dragHandle = { GlassDragHandle() }) {
        Column(modifier = Modifier.padding(horizontal = 24.dp).padding(bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text(district.label, fontSize = 17.sp, fontWeight = FontWeight.Bold, color = TextPrimary)
            Box(modifier = Modifier.clip(RoundedCornerShape(50.dp))
                .background(Color(0x33F39C12))
                .border(1.dp, Color(0x80F39C12), RoundedCornerShape(50.dp))
                .padding(horizontal = 10.dp, vertical = 4.dp)) {
                Text(districtTypeLabel(district.layer),
                    fontSize = 11.sp, fontWeight = FontWeight.Bold, color = Color(0xFFF39C12))
            }
            HorizontalDivider(color = Color(0x1AFFFFFF))
            Button(onClick = onEdit, modifier = Modifier.fillMaxWidth().height(44.dp),
                shape = RoundedCornerShape(8.dp),
                colors = ButtonDefaults.buttonColors(containerColor = NarsTeal, contentColor = Color.White)) {
                Text("Edit Geometry", fontWeight = FontWeight.Bold)
            }
        }
    }
}

@Composable
private fun GlassDragHandle() {
    Box(modifier = Modifier.padding(vertical = 12.dp).size(width = 36.dp, height = 4.dp)
        .clip(RoundedCornerShape(2.dp)).background(Color(0x33FFFFFF)))
}
