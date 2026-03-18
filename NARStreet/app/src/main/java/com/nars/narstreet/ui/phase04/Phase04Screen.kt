package com.nars.narstreet.ui.phase04

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
import com.nars.narstreet.data.model.RoadEntity
import com.nars.narstreet.data.model.SyncStatus
import com.nars.narstreet.ui.components.PhaseScaffold
import com.nars.narstreet.ui.components.SyncState

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun Phase04Screen(
    viewModel: Phase04ViewModel = hiltViewModel(),
    onLogout: () -> Unit,
    onNavigateTo: (String) -> Unit,
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()

    PhaseScaffold(
        title     = stringResource(R.string.phase04_title),
        syncState = state.syncState,
        onLogout  = { viewModel.logout(onLogout) },
    ) { padding ->
        if (state.isLoading) {
            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                CircularProgressIndicator()
            }
        } else {
            LazyColumn(
                contentPadding = PaddingValues(
                    top    = padding.calculateTopPadding() + 8.dp,
                    bottom = padding.calculateBottomPadding() + 8.dp,
                    start  = 16.dp,
                    end    = 16.dp,
                ),
                verticalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                item {
                    // Navigation row to other phases
                    PhaseNavRow(onNavigateTo)
                    Spacer(Modifier.height(8.dp))
                }
                items(state.roads, key = { it.id }) { road ->
                    RoadCard(road = road, onClick = { viewModel.startEditing(road) })
                }
            }
        }
    }

    // Bottom sheet editor
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

@Composable
private fun PhaseNavRow(onNavigateTo: (String) -> Unit) {
    Row(
        horizontalArrangement = Arrangement.spacedBy(8.dp),
        modifier = Modifier.fillMaxWidth(),
    ) {
        listOf(
            "Entrances" to "phase05",
            "Buildings" to "phase06/0",
            "Spaces"    to "phase07/0",
            "Panels"    to "phase08/0",
        ).forEach { (label, route) ->
            OutlinedButton(
                onClick  = { onNavigateTo(route) },
                modifier = Modifier.weight(1f),
            ) { Text(label, style = MaterialTheme.typography.labelLarge, maxLines = 1) }
        }
    }
}

@Composable
private fun RoadCard(road: RoadEntity, onClick: () -> Unit) {
    Card(
        modifier  = Modifier.fillMaxWidth().clickable(onClick = onClick),
        elevation = CardDefaults.cardElevation(defaultElevation = 2.dp),
    ) {
        Row(
            modifier = Modifier.padding(16.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text(road.label, style = MaterialTheme.typography.titleMedium)
                val filled = listOfNotNull(
                    road.lanes?.let { "Lanes: $it" },
                    road.trafficCapacity?.let { "Traffic: $it" },
                    road.tradActivity?.let { "Trad: $it" },
                ).joinToString(" · ")
                if (filled.isNotEmpty()) {
                    Text(filled, style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
            SyncChip(road.syncStatus)
            Spacer(Modifier.width(8.dp))
            Icon(Icons.Default.ChevronRight, contentDescription = null,
                tint = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@Composable
private fun SyncChip(status: SyncStatus) {
    val (label, color) = when (status) {
        SyncStatus.PENDING -> "Pending" to MaterialTheme.colorScheme.tertiary
        SyncStatus.SYNCED  -> "Synced"  to MaterialTheme.colorScheme.primary
        SyncStatus.ERROR   -> "Error"   to MaterialTheme.colorScheme.error
    }
    SuggestionChip(
        onClick = {},
        label   = { Text(label, style = MaterialTheme.typography.labelLarge) },
        colors  = SuggestionChipDefaults.suggestionChipColors(
            containerColor = color.copy(alpha = 0.12f),
            labelColor     = color,
        ),
    )
}

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

    val sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)

    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = sheetState) {
        Column(
            modifier            = Modifier.padding(horizontal = 24.dp).padding(bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Text(road.label, style = MaterialTheme.typography.titleLarge)

            OutlinedTextField(
                value         = lanes,
                onValueChange = { lanes = it.filter(Char::isDigit) },
                label         = { Text(stringResource(R.string.road_lanes)) },
                modifier      = Modifier.fillMaxWidth(),
                singleLine    = true,
            )

            SegmentedPicker(
                label    = stringResource(R.string.road_traffic),
                options  = listOf("HIGH", "MEDIUM", "LOW"),
                selected = traffic,
                onSelect = { traffic = it },
            )

            SegmentedPicker(
                label    = stringResource(R.string.road_trad),
                options  = listOf("HIGH", "MEDIUM", "LOW"),
                selected = trad,
                onSelect = { trad = it },
            )

            BooleanToggle(stringResource(R.string.road_median),   median,   { median = it })
            BooleanToggle(stringResource(R.string.road_greenery), greenery, { greenery = it })
            BooleanToggle(stringResource(R.string.road_dead_end), deadEnd,  { deadEnd = it })

            Button(
                onClick  = {
                    onSave(lanes.toIntOrNull(), traffic, trad, median, greenery, deadEnd)
                },
                modifier = Modifier.fillMaxWidth(),
            ) { Text(stringResource(R.string.road_save)) }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SegmentedPicker(
    label: String,
    options: List<String>,
    selected: String,
    onSelect: (String) -> Unit,
) {
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Text(label, style = MaterialTheme.typography.labelLarge,
            color = MaterialTheme.colorScheme.onSurfaceVariant)
        SingleChoiceSegmentedButtonRow(modifier = Modifier.fillMaxWidth()) {
            options.forEachIndexed { i, opt ->
                SegmentedButton(
                    selected = opt == selected,
                    onClick  = { onSelect(opt) },
                    shape    = SegmentedButtonDefaults.itemShape(i, options.size),
                ) { Text(opt, style = MaterialTheme.typography.labelLarge) }
            }
        }
    }
}

@Composable
private fun BooleanToggle(label: String, value: Boolean, onToggle: (Boolean) -> Unit) {
    Row(
        modifier              = Modifier.fillMaxWidth(),
        verticalAlignment     = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(label, style = MaterialTheme.typography.bodyLarge)
        Switch(checked = value, onCheckedChange = onToggle)
    }
}
