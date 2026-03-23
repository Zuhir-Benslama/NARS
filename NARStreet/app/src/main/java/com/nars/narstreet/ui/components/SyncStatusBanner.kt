package com.nars.narstreet.ui.components

import androidx.compose.animation.*
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp
import com.nars.narstreet.ui.theme.*

enum class SyncState { IDLE, PENDING, OFFLINE }

@Composable
fun SyncStatusBanner(state: SyncState, modifier: Modifier = Modifier) {
    AnimatedVisibility(
        visible  = state != SyncState.IDLE,
        enter    = expandVertically() + fadeIn(),
        exit     = shrinkVertically() + fadeOut(),
        modifier = modifier,
    ) {
        val (bgColor, msg) = when (state) {
            SyncState.PENDING -> Color(0xFF1E5E2C) to "⟳  Syncing changes…"
            SyncState.OFFLINE -> Color(0xFF4A3000) to "⚠  Offline — changes will sync when connected"
            SyncState.IDLE    -> Color.Transparent to ""
        }
        Box(
            modifier         = Modifier.fillMaxWidth().background(bgColor)
                .padding(horizontal = 16.dp, vertical = 6.dp),
            contentAlignment = Alignment.Center,
        ) {
            Text(msg, fontSize = 12.sp, fontWeight = FontWeight.Medium, color = TextSecondary)
        }
    }
}
