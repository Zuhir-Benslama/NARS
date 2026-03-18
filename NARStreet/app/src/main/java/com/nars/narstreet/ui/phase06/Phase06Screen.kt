package com.nars.narstreet.ui.phase06

import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Save
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
import com.nars.narstreet.ui.components.PhaseScaffold
import com.nars.narstreet.ui.components.PolygonEditorMapView

@Composable
fun Phase06Screen(
    buildingId: Long,
    viewModel: Phase06ViewModel = hiltViewModel(),
    onBack: () -> Unit,
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()

    if (state.isSaved) {
        LaunchedEffect(Unit) {
            kotlinx.coroutines.delay(800)
            onBack()
        }
    }

    PhaseScaffold(
        title     = stringResource(R.string.phase06_title),
        syncState = state.syncState,
        onBack    = onBack,
        floatingActionButton = {
            if (!state.isLoading && state.building != null) {
                FloatingActionButton(onClick = viewModel::saveGeometry) {
                    Icon(Icons.Default.Save, contentDescription = stringResource(R.string.geometry_save))
                }
            }
        },
    ) { padding ->
        when {
            state.isLoading -> Box(Modifier.fillMaxSize(), Alignment.Center) {
                CircularProgressIndicator()
            }
            state.building == null -> Box(Modifier.fillMaxSize(), Alignment.Center) {
                Text("Building not found")
            }
            else -> Column(modifier = Modifier.fillMaxSize().padding(padding)) {
                Text(
                    text     = state.building!!.label,
                    style    = MaterialTheme.typography.titleMedium,
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
                )
                Text(
                    text     = stringResource(R.string.geometry_instructions),
                    style    = MaterialTheme.typography.bodyMedium,
                    color    = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(horizontal = 16.dp).padding(bottom = 8.dp),
                )
                PolygonEditorMapView(
                    vertices        = state.vertices,
                    onVerticesMoved = viewModel::onVerticesMoved,
                    modifier        = Modifier.fillMaxSize(),
                )
            }
        }
    }
}
