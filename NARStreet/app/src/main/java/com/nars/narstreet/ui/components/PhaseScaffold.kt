package com.nars.narstreet.ui.components

import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Logout
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import com.nars.narstreet.R

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PhaseScaffold(
    title: String,
    syncState: SyncState,
    onBack: (() -> Unit)? = null,
    onLogout: (() -> Unit)? = null,
    floatingActionButton: @Composable () -> Unit = {},
    content: @Composable (PaddingValues) -> Unit,
) {
    Scaffold(
        topBar = {
            Column {
                TopAppBar(
                    title = { Text(title) },
                    navigationIcon = {
                        if (onBack != null) {
                            IconButton(onClick = onBack) {
                                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
                            }
                        }
                    },
                    actions = {
                        if (onLogout != null) {
                            IconButton(onClick = onLogout) {
                                Icon(Icons.Default.Logout, contentDescription = stringResource(R.string.logout))
                            }
                        }
                    },
                )
                SyncStatusBanner(state = syncState)
            }
        },
        floatingActionButton = floatingActionButton,
        content = content,
    )
}
