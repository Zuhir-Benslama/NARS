package com.nars.narstreet.ui.components

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.nars.narstreet.ui.theme.*

@Composable
fun PhaseScaffold(
    title: String,
    syncState: SyncState,
    currentPhaseIndex: Int,
    onNavigateTo: (String) -> Unit,
    onBack: (() -> Unit)?   = null,
    onLogout: (() -> Unit)? = null,
    username: String        = "",
    floatingActionButton: @Composable () -> Unit = {},
    content: @Composable (PaddingValues) -> Unit,
) {
    if (onBack != null) BackHandler(onBack = onBack)

    // ── Use statusBarsPadding only for floating controls, not for map content ──
    // This avoids the black strip at the top that appears when innerPadding is
    // applied to a full-screen MapLibre view.

    Box(modifier = Modifier.fillMaxSize()) {

        // ── Content fills the ENTIRE screen (behind status bar too) ───────────
        content(PaddingValues(0.dp))

        // ── Vertical PhaseBar — right edge, centered, overlaid on content ─────
        PhaseBar(
            currentPhaseIndex = currentPhaseIndex,
            onPhaseClick      = { idx -> onNavigateTo(PHASE_STEPS[idx].route) },
            modifier          = Modifier
                .align(Alignment.CenterEnd)
                .statusBarsPadding()           // pushes bar below status bar
                .padding(end = 4.dp, bottom = 4.dp)
                .background(GlassBg, RoundedCornerShape(14.dp))
                .padding(horizontal = 4.dp, vertical = 8.dp),
        )

        // ── AccountMenu — top right, respects status bar ──────────────────────
        if (onLogout != null) {
            AccountMenu(
                username = username,
                onLogout = onLogout,
                modifier = Modifier
                    .align(Alignment.TopEnd)
                    .statusBarsPadding()
                    .padding(top = 8.dp, end = 48.dp),  // 48dp = clear of PhaseBar
            )
        }

        // ── Sync banner — below the account menu ──────────────────────────────
        SyncStatusBanner(
            state    = syncState,
            modifier = Modifier
                .align(Alignment.TopCenter)
                .statusBarsPadding()
                .padding(top = 52.dp),
        )

        // ── FAB ───────────────────────────────────────────────────────────────
        Box(
            modifier = Modifier
                .align(Alignment.BottomEnd)
                .navigationBarsPadding()
                .padding(end = 56.dp, bottom = 16.dp),  // 56dp = clear of PhaseBar
        ) {
            floatingActionButton()
        }
    }
}
