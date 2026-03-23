package com.nars.narstreet.ui.theme

import androidx.compose.ui.graphics.Color

// ── NARS navy glassmorphism palette — mirrors the web frontend ────────────────

// Primary backgrounds
val NarsNavyDeep   = Color(0xFF0F0F23)   // darkest bg — modal / card bg
val NarsNavy       = Color(0xFF1A1A2E)   // main surface
val NarsNavyLight  = Color(0xFF2C2C4A)   // elevated surface (bottom sheets, cards)
val NarsNavyMid    = Color(0xFF3A3A5C)   // hover state

// Accent — NARS teal/green (save, confirm, primary action)
val NarsTeal       = Color(0xFF27AE60)
val NarsTealDark   = Color(0xFF1E8449)
val NarsOnTeal     = Color(0xFFFFFFFF)

// Accent — blue (focus rings, links)
val NarsBlue       = Color(0xFF3498DB)
val NarsBlueDark   = Color(0xFF2980B9)

// Glass overlays
val GlassBg        = Color(0xE01A1A2E)   // ~88% opacity navy
val GlassBorder    = Color(0x40FFFFFF)   // 25% white border

// Text
val TextPrimary    = Color(0xFFFFFFFF)
val TextSecondary  = Color(0xB3FFFFFF)   // 70%
val TextMuted      = Color(0x66FFFFFF)   // 40%
val TextHint       = Color(0x59FFFFFF)   // 35%

// Status
val SyncPending    = Color(0xFFBA7517)
val SyncError      = Color(0xFFE24B4A)
val SyncSuccess    = Color(0xFF27AE60)
val SyncOffline    = Color(0xFF888780)

// Phase step colors (match web phase colors)
val PhaseAreaColor     = Color(0xFF8E44AD)
val PhaseDistrictColor = Color(0xFFF39C12)
val PhaseCityCtrColor  = Color(0xFFE74C3C)
val PhaseRoadColor     = Color(0xFF3498DB)
val PhaseEntrColor     = Color(0xFF27AE60)
val PhaseBuildColor    = Color(0xFFE67E22)
val PhaseSpaceColor    = Color(0xFF2ECC71)
val PhasePanelColor    = Color(0xFF9B59B6)
