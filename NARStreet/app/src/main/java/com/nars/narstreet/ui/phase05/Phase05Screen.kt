package com.nars.narstreet.ui.phase05

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import android.Manifest
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nars.narstreet.R
import com.nars.narstreet.data.model.EntranceEntity
import com.nars.narstreet.ui.components.EntranceMapView
import com.nars.narstreet.ui.components.PhaseScaffold

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun Phase05Screen(
    viewModel: Phase05ViewModel = hiltViewModel(),
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
        title     = stringResource(R.string.phase05_title),
        syncState = state.syncState,
        onBack    = onBack,
        floatingActionButton = {
            ExtendedFloatingActionButton(
                onClick = viewModel::captureEntrance,
                icon    = { Icon(Icons.Default.AddLocation, contentDescription = null) },
                text    = { Text(stringResource(R.string.entrance_capture)) },
            )
        },
    ) { padding ->
        Column(modifier = Modifier.fillMaxSize().padding(padding)) {

            // Map — top half
            EntranceMapView(
                currentLat = state.currentLat,
                currentLng = state.currentLng,
                entrances  = state.entrances,
                modifier   = Modifier.fillMaxWidth().weight(1f),
            )

            // Capture success snackbar-style banner
            if (state.captureSuccess) {
                Surface(color = MaterialTheme.colorScheme.primaryContainer) {
                    Row(
                        modifier              = Modifier.fillMaxWidth().padding(12.dp),
                        verticalAlignment     = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        Icon(Icons.Default.CheckCircle, contentDescription = null,
                            tint = MaterialTheme.colorScheme.primary)
                        Text("Entrance captured", color = MaterialTheme.colorScheme.onPrimaryContainer)
                    }
                }
            }

            // Entrance list — bottom half
            LazyColumn(
                modifier            = Modifier.weight(1f),
                contentPadding      = PaddingValues(16.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                items(state.entrances, key = { it.id }) { entrance ->
                    EntranceCard(entrance = entrance, onClick = { viewModel.startEditing(entrance) })
                }
            }
        }
    }

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

@Composable
private fun EntranceCard(entrance: EntranceEntity, onClick: () -> Unit) {
    Card(
        modifier  = Modifier.fillMaxWidth().clickable(onClick = onClick),
        elevation = CardDefaults.cardElevation(defaultElevation = 2.dp),
    ) {
        Row(modifier = Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
            Column(modifier = Modifier.weight(1f)) {
                Text(entrance.label, style = MaterialTheme.typography.titleMedium)
                Text(
                    "%.5f, %.5f".format(entrance.lat, entrance.lng),
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                entrance.isNumbered?.let {
                    Text(
                        if (it) "Numbered: ${if (entrance.isNumberCorrect == true) "correct" else "incorrect"}"
                        else "Not numbered",
                        style = MaterialTheme.typography.bodyMedium,
                        color = if (it && entrance.isNumberCorrect == true)
                            MaterialTheme.colorScheme.primary
                        else MaterialTheme.colorScheme.error,
                    )
                }
            }
            Icon(Icons.Default.ChevronRight, contentDescription = null,
                tint = MaterialTheme.colorScheme.onSurfaceVariant)
        }
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

    ModalBottomSheet(onDismissRequest = onDismiss) {
        Column(
            modifier            = Modifier.padding(horizontal = 24.dp).padding(bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Text(entrance.label, style = MaterialTheme.typography.titleLarge)

            Row(
                modifier              = Modifier.fillMaxWidth(),
                verticalAlignment     = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                Text(stringResource(R.string.entrance_numbered),
                    style = MaterialTheme.typography.bodyLarge)
                Switch(checked = isNumbered, onCheckedChange = {
                    isNumbered = it
                    if (!it) isCorrect = null
                })
            }

            if (isNumbered) {
                Row(
                    modifier              = Modifier.fillMaxWidth(),
                    verticalAlignment     = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Text(stringResource(R.string.entrance_number_correct),
                        style = MaterialTheme.typography.bodyLarge)
                    Switch(
                        checked         = isCorrect ?: false,
                        onCheckedChange = { isCorrect = it },
                    )
                }

                if (isCorrect == false) {
                    Card(colors = CardDefaults.cardColors(
                        containerColor = MaterialTheme.colorScheme.errorContainer
                    )) {
                        Row(
                            modifier          = Modifier.padding(12.dp),
                            horizontalArrangement = Arrangement.spacedBy(8.dp),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Icon(Icons.Default.Warning, contentDescription = null,
                                tint = MaterialTheme.colorScheme.error)
                            Text(stringResource(R.string.entrance_order_plate),
                                color = MaterialTheme.colorScheme.onErrorContainer)
                        }
                    }
                }
            } else {
                Card(colors = CardDefaults.cardColors(
                    containerColor = MaterialTheme.colorScheme.errorContainer
                )) {
                    Row(
                        modifier          = Modifier.padding(12.dp),
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Icon(Icons.Default.NotificationsActive, contentDescription = null,
                            tint = MaterialTheme.colorScheme.error)
                        Text(stringResource(R.string.entrance_notify_plate),
                            color = MaterialTheme.colorScheme.onErrorContainer)
                    }
                }
            }

            Button(
                onClick  = { onSave(isNumbered, isCorrect) },
                modifier = Modifier.fillMaxWidth(),
            ) { Text(stringResource(R.string.save)) }
        }
    }
}
