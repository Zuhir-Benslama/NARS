package com.nars.narstreet.ui.phase05

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import android.Manifest
import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nars.narstreet.R
import com.nars.narstreet.data.model.EntranceEntity
import android.webkit.WebView
import org.maplibre.android.geometry.LatLng
import com.nars.narstreet.ui.components.*
import com.nars.narstreet.ui.theme.*

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun Phase05Screen(
    viewModel: Phase05ViewModel = hiltViewModel(),
    onNavigateTo: (String) -> Unit,
    onBack: () -> Unit,
    onLogout: () -> Unit,
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()

    val permissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted -> if (granted) viewModel.onPermissionGranted() }

    LaunchedEffect(Unit) {
        if (!state.locationPermission) {
            permissionLauncher.launch(Manifest.permission.ACCESS_FINE_LOCATION)
        }
    }

    if (state.captureSuccess) {
        LaunchedEffect(Unit) {
            kotlinx.coroutines.delay(1500)
            viewModel.dismissCaptureSuccess()
        }
    }

    PhaseScaffold(
        title             = stringResource(R.string.phase05_title),
        syncState         = state.syncState,
        currentPhaseIndex = PhaseIndex.ENTRANCES,
        onNavigateTo      = onNavigateTo,
        onBack            = onBack,
        username          = state.username,
        onLogout          = onLogout,
        floatingActionButton = {
            ExtendedFloatingActionButton(
                onClick        = viewModel::captureEntrance,
                icon           = { Icon(Icons.Default.AddLocation, contentDescription = null) },
                text           = { Text(stringResource(R.string.entrance_capture)) },
                containerColor = NarsTeal,
                contentColor   = Color.White,
            )
        },
    ) { _ ->
        Box(modifier = Modifier.fillMaxSize()) {

            // Full-screen map — locked while the edit sheet is open
            var wv05 by remember { mutableStateOf<WebView?>(null) }
            var ready05 by remember { mutableStateOf(false) }
            NarsMapView(modifier = Modifier.fillMaxSize(), onBridge = { b, wv ->
                b.onMapReady = { ready05 = true }
                b.onFeatureLongClick = { _, id ->
                    state.entrances.find { it.id == id }?.let { viewModel.startEditing(it) }
                }
                wv05 = wv
            })
            val L = state.mapLayers
            LaunchedEffect(ready05, L, state.entrances, state.currentLat, state.currentLng) {
                if (!ready05) return@LaunchedEffect
                    val wv = wv05 ?: return@LaunchedEffect
                    wv.flyTo(L.communeCenter.latitude, L.communeCenter.longitude, 15.0)
                    wv.setContext(L.communeContext)
                    wv.setAreas(L.areaPolygons, L.areaLabels)
                    L.cityCenterPoint?.let { wv.setCityCenter(it.latitude, it.longitude) }
                    wv.setRoads(L.roadPolylines, L.roadDbIds)
                    wv.setEntrances(
                        state.entrances.map { LatLng(it.lat, it.lng) },
                        state.entrances.map { it.id },
                        state.entrances.map { it.label }
                    )
                    wv.setGps(state.currentLat, state.currentLng)
                }

            // Capture success banner
            if (state.captureSuccess) {
                Surface(
                    color    = MaterialTheme.colorScheme.primaryContainer,
                    modifier = Modifier.align(Alignment.BottomCenter).fillMaxWidth(),
                ) {
                    Row(
                        modifier              = Modifier.padding(12.dp),
                        verticalAlignment     = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        Icon(Icons.Default.CheckCircle, null,
                            tint = MaterialTheme.colorScheme.primary)
                        Text("Entrance captured",
                            color = MaterialTheme.colorScheme.onPrimaryContainer)
                    }
                }
            }
        }
    }

    // Edit sheet (long-press on marker)
    state.editingEntrance?.let { entrance ->
        NumberingCheckSheet(
            entrance  = entrance,
            onDismiss = viewModel::cancelEditing,
            onSave    = { numbered, correct ->
                viewModel.saveNumberingCheck(entrance, numbered, correct)
            },
        )
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun NumberingCheckSheet(
    entrance: EntranceEntity,
    onDismiss: () -> Unit,
    onSave: (Boolean, Boolean?) -> Unit,
) {
    var isNumbered by remember { mutableStateOf(entrance.isNumbered ?: false) }
    var isCorrect  by remember { mutableStateOf(entrance.isNumberCorrect) }

    ModalBottomSheet(onDismissRequest = onDismiss, containerColor = NarsNavy) {
        Column(
            modifier            = Modifier.padding(horizontal = 24.dp).padding(bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Text(entrance.label, style = MaterialTheme.typography.titleLarge, color = TextPrimary)

            Row(modifier = Modifier.fillMaxWidth(),
                verticalAlignment     = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween) {
                Text(stringResource(R.string.entrance_numbered),
                    style = MaterialTheme.typography.bodyLarge, color = TextSecondary)
                Switch(checked = isNumbered,
                    onCheckedChange = { isNumbered = it; if (!it) isCorrect = null },
                    colors = SwitchDefaults.colors(checkedTrackColor = NarsTeal, checkedThumbColor = Color.White))
            }

            if (isNumbered) {
                Row(modifier = Modifier.fillMaxWidth(),
                    verticalAlignment     = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween) {
                    Text(stringResource(R.string.entrance_number_correct),
                        style = MaterialTheme.typography.bodyLarge, color = TextSecondary)
                    Switch(checked = isCorrect ?: false, onCheckedChange = { isCorrect = it },
                        colors = SwitchDefaults.colors(checkedTrackColor = NarsTeal, checkedThumbColor = Color.White))
                }
                if (isCorrect == false) {
                    Card(colors = CardDefaults.cardColors(containerColor = Color(0x33E24B4A))) {
                        Row(Modifier.padding(12.dp), Arrangement.spacedBy(8.dp), Alignment.CenterVertically) {
                            Icon(Icons.Default.Warning, null, tint = SyncError)
                            Text(stringResource(R.string.entrance_order_plate), color = TextSecondary)
                        }
                    }
                }
            } else {
                Card(colors = CardDefaults.cardColors(containerColor = Color(0x33E24B4A))) {
                    Row(Modifier.padding(12.dp), Arrangement.spacedBy(8.dp), Alignment.CenterVertically) {
                        Icon(Icons.Default.NotificationsActive, null, tint = SyncError)
                        Text(stringResource(R.string.entrance_notify_plate), color = TextSecondary)
                    }
                }
            }

            Button(onClick = { onSave(isNumbered, isCorrect) },
                modifier = Modifier.fillMaxWidth().height(48.dp),
                colors   = ButtonDefaults.buttonColors(containerColor = NarsTeal, contentColor = Color.White)) {
                Text(stringResource(R.string.save))
            }
        }
    }
}
