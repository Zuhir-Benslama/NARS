package com.nars.narstreet.ui.phase01

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
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nars.narstreet.data.model.AreaEntity
import com.nars.narstreet.data.model.SyncStatus
import android.webkit.WebView
import com.nars.narstreet.ui.components.*
import com.nars.narstreet.ui.theme.*

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun Phase01Screen(
    areaId: Long = 0L,
    onNavigateTo: (String) -> Unit,
    onBack: () -> Unit,
    viewModel: Phase01ViewModel = hiltViewModel(),
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()

    if (state.isSaved) {
        LaunchedEffect(Unit) { kotlinx.coroutines.delay(800); onBack() }
    }

    // ── Detail view — polygon editor ──────────────────────────────────────────
    if (areaId > 0 && state.editingArea != null) {
        val area = state.editingArea!!
        val typeColor = if (area.layer == "central_urban") Color(0xFFC0392B) else Color(0xFF8E44AD)
        val featureColor = if (area.layer == "central_urban") "#C0392B" else "#8E44AD"

        PhaseScaffold(
            title             = area.label,
            syncState         = state.syncState,
            currentPhaseIndex = PhaseIndex.AREAS,
            onNavigateTo      = onNavigateTo,
            onBack            = onBack,
            username          = state.username,
            onLogout          = { onNavigateTo("login") },
            floatingActionButton = {
                FloatingActionButton(
                    onClick        = viewModel::saveGeometry,
                    containerColor = NarsTeal,
                    contentColor   = Color.White,
                ) { Icon(Icons.Default.Save, null) }
            },
        ) { _ ->
            Box(Modifier.fillMaxSize()) {
                var webViewEdit01 by remember { mutableStateOf<WebView?>(null) }
                var readyEdit01 by remember { mutableStateOf(false) }
                NarsMapView(modifier = Modifier.fillMaxSize(), onBridge = { b, wv ->
                    b.onMapReady = { readyEdit01 = true }
                    b.onGeometryChanged = { _, pts ->
                        val coords = com.nars.narstreet.ui.components.parseGeometryPoints(pts)
                        if (coords.isNotEmpty()) viewModel.onVerticesMoved(coords)
                    }
                    webViewEdit01 = wv
                })
                LaunchedEffect(readyEdit01, state.vertices, area) {
                        if (!readyEdit01) return@LaunchedEffect
                        val wv = webViewEdit01 ?: return@LaunchedEffect
                        val cc = state.communeCenter
                        wv.setContext(state.communeContext)
                        wv.startEditPolygon(area.id.toString(), state.vertices, featureColor)
                        if (state.vertices.isNotEmpty())
                            wv.flyTo(state.vertices.map{it.latitude}.average(), state.vertices.map{it.longitude}.average(), 15.0)
                        else wv.flyTo(cc.latitude, cc.longitude, 13.0)
                }

                // Type badge
                Row(
                    modifier = Modifier
                        .align(Alignment.BottomCenter)
                        .padding(bottom = 80.dp)
                        .clip(RoundedCornerShape(50.dp))
                        .background(typeColor.copy(alpha = 0.85f))
                        .padding(horizontal = 14.dp, vertical = 6.dp),
                ) {
                    Text(
                        if (area.layer == "central_urban") "Main Urban Area" else "Secondary Urban Area",
                        fontSize = 12.sp, fontWeight = FontWeight.Bold, color = Color.White,
                    )
                }
            }
        }
        return
    }

    // ── List view — areas on map ───────────────────────────────────────────────
    PhaseScaffold(
        title             = "Areas",
        syncState         = state.syncState,
        currentPhaseIndex = PhaseIndex.AREAS,
        onNavigateTo      = onNavigateTo,
        onBack            = onBack,
        username          = state.username,
        onLogout          = { onNavigateTo("login") },
    ) { _ ->
        Box(Modifier.fillMaxSize()) {
            if (state.isLoading) {
                Box(Modifier.fillMaxSize(), Alignment.Center) {
                    CircularProgressIndicator(color = NarsTeal)
                }
            } else {
                var webView01 by remember { mutableStateOf<WebView?>(null) }
                var ready01 by remember { mutableStateOf(false) }
                NarsMapView(modifier = Modifier.fillMaxSize(), onBridge = { b, wv ->
                    b.onMapReady = { ready01 = true }
                    b.onFeatureClick = { _, dbId ->
                        state.areas.find { it.id == dbId }?.let { viewModel.showInfo(it) }
                    }
                    webView01 = wv
                })
                val L = state.mapLayers
                LaunchedEffect(ready01, L) {
                    if (!ready01) return@LaunchedEffect
                    
                        val wv = webView01 ?: return@LaunchedEffect
                        wv.flyTo(state.communeCenter.latitude, state.communeCenter.longitude, 12.0)
                        wv.setContext(state.communeContext)
                        wv.setAreas(L.areaPolygons, L.areaLabels)
                    }

                if (state.areas.isEmpty()) {
                    Box(Modifier.fillMaxSize(), Alignment.Center) {
                        Text("No areas drawn yet.\nUse the web app to draw areas.",
                            color = TextSecondary,
                            textAlign = androidx.compose.ui.text.style.TextAlign.Center,
                        )
                    }
                }
            }
        }
    }

    // Info sheet (tap)
    state.infoArea?.let { area ->
        AreaInfoSheet(area = area, onDismiss = viewModel::dismissInfo,
            onEdit = { onNavigateTo("phase01/${area.id}") })
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun AreaInfoSheet(area: AreaEntity, onDismiss: () -> Unit, onEdit: () -> Unit) {
    val typeColor = if (area.layer == "central_urban") Color(0xFFC0392B) else Color(0xFF8E44AD)
    val typeLabel = if (area.layer == "central_urban") "Main Urban Area" else "Secondary Urban Area"

    ModalBottomSheet(onDismissRequest = onDismiss, containerColor = NarsNavy,
        dragHandle = { GlassDragHandle() }) {
        Column(modifier = Modifier.padding(horizontal = 24.dp).padding(bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text(area.label, fontSize = 17.sp, fontWeight = FontWeight.Bold, color = TextPrimary)
            Box(modifier = Modifier.clip(RoundedCornerShape(50.dp))
                .background(typeColor.copy(alpha = 0.15f))
                .border(1.dp, typeColor.copy(alpha = 0.4f), RoundedCornerShape(50.dp))
                .padding(horizontal = 10.dp, vertical = 4.dp)) {
                Text(typeLabel, fontSize = 11.sp, fontWeight = FontWeight.Bold, color = typeColor)
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
