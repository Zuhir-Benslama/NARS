package com.nars.narstreet.ui.phase03

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import android.webkit.WebView
import com.nars.narstreet.ui.components.*
import com.nars.narstreet.ui.theme.*

@Composable
fun Phase03Screen(
    onNavigateTo: (String) -> Unit,
    onBack: () -> Unit,
    viewModel: Phase03ViewModel = hiltViewModel(),
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()

    PhaseScaffold(
        title             = "City Center",
        syncState         = state.syncState,
        currentPhaseIndex = PhaseIndex.CITY_CENTER,
        onNavigateTo      = onNavigateTo,
        onBack            = onBack,
        username          = state.username,
        onLogout          = { onNavigateTo("login") },
    ) { _ ->
        Box(Modifier.fillMaxSize()) {
            when {
                state.isLoading -> Box(Modifier.fillMaxSize(), Alignment.Center) {
                    CircularProgressIndicator(color = NarsTeal)
                }

                state.cityCenter == null -> {
                    // Empty state — read-only message, no add button
                    Box(Modifier.fillMaxSize().background(NarsNavyDeep), Alignment.Center) {
                        Column(
                            horizontalAlignment = Alignment.CenterHorizontally,
                            verticalArrangement = Arrangement.spacedBy(12.dp),
                            modifier            = Modifier.padding(32.dp),
                        ) {
                            Text("📍", fontSize = 48.sp)
                            Text(
                                "City Center not placed yet",
                                fontSize   = 18.sp,
                                fontWeight = FontWeight.Bold,
                                color      = TextPrimary,
                                textAlign  = TextAlign.Center,
                            )
                            Text(
                                "The city center marker is placed on the web app.\nOnce placed it will appear here.",
                                fontSize  = 13.sp,
                                color     = TextSecondary,
                                textAlign = TextAlign.Center,
                            )
                        }
                    }
                }

                else -> {
                    val cc = state.cityCenter!!

                    // Full-screen map with the pin
                    var wv03 by remember { mutableStateOf<WebView?>(null) }
                    var ready03 by remember { mutableStateOf(false) }
                    val L = state.mapLayers
                    NarsMapView(modifier = Modifier.fillMaxSize(), onBridge = { b, wv ->
                        b.onMapReady = { ready03 = true }
                        wv03 = wv
                    })
                    LaunchedEffect(ready03, cc, L) {
                            if (!ready03) return@LaunchedEffect
                            val wv = wv03 ?: return@LaunchedEffect
                            wv.flyTo(cc.lat, cc.lng, 16.0)
                            wv.setContext(state.communeContext)
                            wv.setAreas(L.areaPolygons, L.areaLabels)
                            wv.setCityCenter(cc.lat, cc.lng)
                    }

                    // Info pill at the bottom
                    Row(
                        modifier = Modifier
                            .align(Alignment.BottomCenter)
                            .padding(bottom = 32.dp)
                            .clip(RoundedCornerShape(50.dp))
                            .background(GlassBg)
                            .border(1.dp, GlassBorder, RoundedCornerShape(50.dp))
                            .padding(horizontal = 16.dp, vertical = 10.dp),
                        verticalAlignment     = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(10.dp),
                    ) {
                        Box(
                            modifier = Modifier
                                .size(24.dp)
                                .clip(CircleShape)
                                .background(Color(0xFFE74C3C)),
                            contentAlignment = Alignment.Center,
                        ) { Text("📍", fontSize = 12.sp) }
                        Column {
                            Text(cc.label, fontSize = 13.sp, fontWeight = FontWeight.Bold, color = TextPrimary)
                            Text(
                                "%.5f, %.5f".format(cc.lat, cc.lng),
                                fontSize = 11.sp, color = TextMuted,
                            )
                        }
                        Box(
                            modifier = Modifier
                                .clip(RoundedCornerShape(50.dp))
                                .background(Color(0x1AE74C3C))
                                .border(1.dp, Color(0x66E74C3C), RoundedCornerShape(50.dp))
                                .padding(horizontal = 8.dp, vertical = 3.dp),
                        ) {
                            Text("Placed", fontSize = 10.sp, fontWeight = FontWeight.Bold, color = Color(0xFFE74C3C))
                        }
                    }
                }
            }
        }
    }
}

