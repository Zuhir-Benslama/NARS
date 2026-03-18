package com.nars.narstreet.ui.phase08

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
import com.nars.narstreet.ui.components.PhaseScaffold

@Composable
fun Phase08Screen(
    panelId: Long,
    viewModel: Phase08ViewModel = hiltViewModel(),
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
        title     = stringResource(R.string.phase08_title),
        syncState = state.syncState,
        onBack    = onBack,
    ) { padding ->
        when {
            state.isLoading -> Box(Modifier.fillMaxSize(), Alignment.Center) {
                CircularProgressIndicator()
            }
            state.panel == null -> Box(Modifier.fillMaxSize(), Alignment.Center) {
                Text("Panel not found")
            }
            else -> PanelChecklistContent(
                panel   = state.panel!!,
                onSave  = viewModel::saveChecklist,
                padding = padding,
            )
        }
    }
}

@Composable
private fun PanelChecklistContent(
    panel: com.nars.narstreet.data.model.PanelEntity,
    onSave: (Boolean, Boolean?) -> Unit,
    padding: PaddingValues,
) {
    var isPlaced  by remember { mutableStateOf(panel.isPlaced ?: false) }
    var isCorrect by remember { mutableStateOf(panel.isCorrectLocation) }

    Column(
        modifier            = Modifier
            .fillMaxSize()
            .padding(padding)
            .padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(20.dp),
    ) {
        Text(panel.label, style = MaterialTheme.typography.titleLarge)
        Text(
            "Location: %.5f, %.5f".format(panel.lat, panel.lng),
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )

        HorizontalDivider()

        // Step 1: Is it placed?
        ChecklistQuestion(
            question = stringResource(R.string.panel_placed),
            value    = isPlaced,
            onToggle = {
                isPlaced  = it
                isCorrect = null
            },
        )

        // Step 2 (conditional): Is it in the correct location?
        if (isPlaced) {
            ChecklistQuestion(
                question = stringResource(R.string.panel_correct_location),
                value    = isCorrect ?: false,
                onToggle = { isCorrect = it },
            )
        }

        // Outcome notice
        val (noticeText, noticeColor, noticeIcon) = when {
            !isPlaced             -> Triple(
                stringResource(R.string.panel_order),
                MaterialTheme.colorScheme.errorContainer,
                Icons.Default.AddShoppingCart,
            )
            isCorrect == false    -> Triple(
                stringResource(R.string.panel_relocate),
                MaterialTheme.colorScheme.errorContainer,
                Icons.Default.WrongLocation,
            )
            isCorrect == true     -> Triple(
                "Panel is correctly placed",
                MaterialTheme.colorScheme.primaryContainer,
                Icons.Default.CheckCircle,
            )
            else -> null
        }

        noticeText?.let {
            Card(colors = CardDefaults.cardColors(containerColor = noticeColor)) {
                Row(
                    modifier          = Modifier.padding(16.dp),
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Icon(noticeIcon!!, contentDescription = null)
                    Text(it, style = MaterialTheme.typography.bodyLarge)
                }
            }
        }

        Spacer(Modifier.weight(1f))

        Button(
            onClick  = { onSave(isPlaced, isCorrect) },
            modifier = Modifier.fillMaxWidth().height(52.dp),
        ) { Text(stringResource(R.string.save)) }
    }
}

@Composable
private fun ChecklistQuestion(
    question: String,
    value: Boolean,
    onToggle: (Boolean) -> Unit,
) {
    Row(
        modifier              = Modifier.fillMaxWidth(),
        verticalAlignment     = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(question, style = MaterialTheme.typography.bodyLarge, modifier = Modifier.weight(1f))
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            FilterChip(
                selected = !value,
                onClick  = { onToggle(false) },
                label    = { Text(stringResource(R.string.no)) },
            )
            FilterChip(
                selected = value,
                onClick  = { onToggle(true) },
                label    = { Text(stringResource(R.string.yes)) },
            )
        }
    }
}
