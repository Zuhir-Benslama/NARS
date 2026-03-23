package com.nars.narstreet.ui.phase07

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
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nars.narstreet.R
import com.nars.narstreet.data.model.SpaceEntity
import android.webkit.WebView
import com.nars.narstreet.ui.components.*
import com.nars.narstreet.ui.theme.*

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun Phase07Screen(
    spaceId: Long,
    viewModel: Phase07ViewModel = hiltViewModel(),
    onNavigateTo: (String) -> Unit,
    onBack: () -> Unit,
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()

    if (state.isSaved) {
        LaunchedEffect(Unit) { kotlinx.coroutines.delay(800); onBack() }
    }

    // ── Detail view — polygon editor ──────────────────────────────────────────
    if (spaceId > 0 && state.space != null) {
        val space = state.space!!
        val spaceColor = if (space.layer == "garden") "#2ECC71" else "#2980B9"
        PhaseScaffold(
            title             = space.label,
            syncState         = state.syncState,
            currentPhaseIndex = PhaseIndex.SPACES,
            onNavigateTo      = onNavigateTo,
            onBack            = onBack,
            username          = state.username,
            onLogout          = { onNavigateTo("login") },
            floatingActionButton = {
                FloatingActionButton(onClick = viewModel::saveGeometry,
                    containerColor = NarsTeal, contentColor = Color.White) {
                    Icon(Icons.Default.Save, stringResource(R.string.geometry_save))
                }
            },
        ) { _ ->
            var wvEdit07 by remember { mutableStateOf<WebView?>(null) }
            var readyEdit07 by remember { mutableStateOf(false) }
            NarsMapView(modifier = Modifier.fillMaxSize(), onBridge = { b, wv ->
                b.onMapReady = { readyEdit07 = true }
                b.onGeometryChanged = { _, pts ->
                    val coords = com.nars.narstreet.ui.components.parseGeometryPoints(pts)
                    if (coords.isNotEmpty()) viewModel.onVerticesMoved(coords)
                }
                wvEdit07 = wv
            })
            LaunchedEffect(readyEdit07, state.vertices) {
                if (!readyEdit07) return@LaunchedEffect
                    val wv = wvEdit07 ?: return@LaunchedEffect
                    wv.setContext(state.communeContext)
                    wv.setAreas(state.mapLayers.areaPolygons, state.mapLayers.areaLabels)
                    wv.startEditPolygon(space.id.toString(), state.vertices, spaceColor)
                    if (state.vertices.isNotEmpty())
                        wv.flyTo(state.vertices.map{it.latitude}.average(), state.vertices.map{it.longitude}.average(), 15.0)
                    else wv.flyTo(state.communeCenter.latitude, state.communeCenter.longitude, 13.0)
                }
        }
        return
    }

    // ── List view — all spaces on map ─────────────────────────────────────────
    PhaseScaffold(
        title             = stringResource(R.string.phase07_title),
        syncState         = state.syncState,
        currentPhaseIndex = PhaseIndex.SPACES,
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
                state.spaces.isEmpty() -> Box(Modifier.fillMaxSize(), Alignment.Center) {
                    Text("No public spaces found.\nData syncs from the server.",
                        color = TextSecondary, textAlign = TextAlign.Center)
                }
                else -> {
                    var wv07 by remember { mutableStateOf<WebView?>(null) }
                    var ready07 by remember { mutableStateOf(false) }
                    val L = state.mapLayers
                    NarsMapView(modifier = Modifier.fillMaxSize(), onBridge = { b, wv ->
                        b.onMapReady = { ready07 = true }
                        b.onFeatureClick     = { _, id -> state.spaces.find { it.id == id }?.let { viewModel.showInfo(it) } }
                        b.onFeatureLongClick = { _, id -> onNavigateTo("phase07/$id") }
                        wv07 = wv
                    })
                    LaunchedEffect(ready07, L, state.spaces) {
                            if (!ready07) return@LaunchedEffect
                            val wv = wv07 ?: return@LaunchedEffect
                            wv.flyTo(state.communeCenter.latitude, state.communeCenter.longitude, 13.0)
                            wv.setContext(state.communeContext)
                            wv.setAreas(L.areaPolygons, L.areaLabels)
                            wv.setSpaces(
                                state.spaces.map { parseCoordinatesJson(it.coordinatesJson) },
                                state.spaces.map { it.label }
                            )
                    }
                }
            }
        }
    }

    state.infoSpace?.let { space ->
        SpaceInfoSheet(space, onDismiss = viewModel::dismissInfo,
            onEdit = { onNavigateTo("phase07/${space.id}") })
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SpaceInfoSheet(space: SpaceEntity, onDismiss: () -> Unit, onEdit: () -> Unit) {
    val spaceColor = if (space.layer == "garden") Color(0xFF2ECC71) else Color(0xFF2980B9)
    ModalBottomSheet(onDismissRequest = onDismiss, containerColor = NarsNavy,
        dragHandle = { GlassDragHandle() }) {
        Column(modifier = Modifier.padding(horizontal = 24.dp).padding(bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text(space.label, fontSize = 17.sp, fontWeight = FontWeight.Bold, color = TextPrimary)
            Box(modifier = Modifier.clip(RoundedCornerShape(50.dp))
                .background(spaceColor.copy(alpha = 0.15f))
                .border(1.dp, spaceColor.copy(alpha = 0.4f), RoundedCornerShape(50.dp))
                .padding(horizontal = 10.dp, vertical = 4.dp)) {
                Text(space.layer.replaceFirstChar { it.uppercase() },
                    fontSize = 11.sp, fontWeight = FontWeight.Bold, color = spaceColor)
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

@Composable private fun GlassDragHandle() {
    Box(modifier = Modifier.padding(vertical = 12.dp).size(width = 36.dp, height = 4.dp)
        .clip(RoundedCornerShape(2.dp)).background(Color(0x33FFFFFF)))
}
