package com.nars.narstreet.ui.components

import androidx.compose.animation.*
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.nars.narstreet.R
import com.nars.narstreet.ui.theme.*

enum class SyncState { IDLE, PENDING, SYNCING, SYNCED, OFFLINE, ERROR }

@Composable
fun SyncStatusBanner(state: SyncState, modifier: Modifier = Modifier) {
    AnimatedVisibility(
        visible  = state != SyncState.IDLE,
        enter    = slideInVertically() + fadeIn(),
        exit     = slideOutVertically() + fadeOut(),
        modifier = modifier,
    ) {
        val (color, icon, text) = when (state) {
            SyncState.PENDING  -> Triple(SyncPending,  Icons.Default.CloudUpload,    stringResource(R.string.sync_pending))
            SyncState.SYNCING  -> Triple(SyncPending,  Icons.Default.Sync,           stringResource(R.string.sync_syncing))
            SyncState.SYNCED   -> Triple(SyncSuccess,  Icons.Default.CloudDone,      stringResource(R.string.sync_done))
            SyncState.OFFLINE  -> Triple(SyncOffline,  Icons.Default.CloudOff,       stringResource(R.string.sync_offline))
            SyncState.ERROR    -> Triple(SyncError,    Icons.Default.SyncProblem,    stringResource(R.string.sync_error))
            SyncState.IDLE     -> Triple(Color.Transparent, Icons.Default.Cloud, "")
        }

        Row(
            modifier = Modifier
                .fillMaxWidth()
                .background(color)
                .padding(horizontal = 16.dp, vertical = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            if (state == SyncState.SYNCING) {
                CircularProgressIndicator(
                    modifier  = Modifier.size(16.dp),
                    color     = Color.White,
                    strokeWidth = 2.dp,
                )
            } else {
                Icon(icon, contentDescription = null, tint = Color.White, modifier = Modifier.size(16.dp))
            }
            Text(text, color = Color.White, style = MaterialTheme.typography.labelLarge)
        }
    }
}
