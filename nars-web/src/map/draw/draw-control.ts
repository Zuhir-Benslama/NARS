// ─── DRAW CONTROL ─────────────────────────────────────────────────────────────
// Uses Geoman's native enableDraw() for the current phase's geometry type.
// Mirrors the reference implementation in nars-web/src/map/draw-control.ts

import { getCtx } from "../core/state"
import { debugLog, debugError } from "../../utils/debug"
import { ensureGeomanDrawEdgesVisible } from "../edit/edit-state"
import { patchGeomanMarkerPointerSnap } from "./draw-marker-patch"
import { DRAW_CONFIG } from "../../config"
import { useDrawStore } from "../../stores/drawStore"
import type { DrawModeName } from "@geoman-io/maplibre-geoman-free"
import { delay } from "../../utils/time"

type PhaseConfig = { key: string; drawType: string; color: string }

const DRAW_TYPE_MAP: Record<string, DrawModeName> = {
  polygon: "polygon",
  polyline: "line",
  marker: "marker",
  circle: "circle",
}

export function resetDrawControl(): void {
  const store = useDrawStore()
  store.setLastPhaseKey(null)
  store.setModeSwitchToken(0)
}

// Stops the bounded edge-visibility poll started by buildDrawControl. Must be
// called whenever draw mode is torn down so the poll never keeps ticking
// against a disabled/destroyed Geoman context.
export function clearEdgeVisibilityPoll(): void {
  const store = useDrawStore()
  if (store.edgePollId !== null) {
    clearInterval(store.edgePollId)
    store.setEdgePollId(null)
  }
  if (store.edgeTimeoutId !== null) {
    clearTimeout(store.edgeTimeoutId)
    store.setEdgeTimeoutId(null)
  }
}

export async function buildDrawControl(phase: PhaseConfig): Promise<void> {
  // Passive callers (startup watcher, phase restore) can reach this before the
  // user has opened a draw/edit session — in that case geoman is still deferred
  // and there is nothing to arm, so we no-op. Callers that represent genuine
  // draw intent (phase navigation, empty-map click) call ensureGeoman() before
  // invoking this, which lazily loads the bundle on first use.
  patchGeomanMarkerPointerSnap()

  const gm = getCtx().geoman
  if (!gm) return

  const store = useDrawStore()
  const shapeName = DRAW_TYPE_MAP[phase.drawType]
  if (!shapeName) return

  // Clear any lingering edge-visibility poll from a previous phase
  clearEdgeVisibilityPoll()

  debugLog("[DRAW CONTROL] Phase:", phase.key, "| drawType:", phase.drawType, "| shape:", shapeName)

  const activeModes = gm.getActiveDrawModes?.() || []
  if (store.lastPhaseKey === phase.key && activeModes.length > 0 && activeModes[0] === shapeName) {
    debugLog("[DRAW CONTROL] Already in correct draw mode:", shapeName)
    return
  }

  store.setLastPhaseKey(phase.key)
  const token = store.incrementModeSwitchToken()

  void (async () => {
    if (activeModes.length > 0) {
      debugLog("[DRAW CONTROL] Disabling current draw mode:", activeModes)
      try {
        await gm.disableDraw()
      } catch (err) {
        debugError("[DRAW CONTROL] Failed to disable draw mode:", err)
      }
    }

    await delay(DRAW_CONFIG.modeSwitchSettleMs)

    if (token !== store.modeSwitchToken || store.lastPhaseKey !== phase.key) return

    const currentGm = getCtx().geoman
    if (!currentGm) return

    debugLog("[DRAW CONTROL] Enabling draw mode:", shapeName)
    currentGm
      .enableDraw(shapeName)
      .then(() => {
        debugLog("[DRAW CONTROL] Enabled draw mode:", shapeName)
        if (shapeName === "polygon" || shapeName === "line") {
          ensureGeomanDrawEdgesVisible()
          let retries = 0
          const poll = setInterval(() => {
            if (++retries > DRAW_CONFIG.edgeRetryMax) {
              clearInterval(poll)
              store.setEdgePollId(null)
              return
            }
            ensureGeomanDrawEdgesVisible()
          }, DRAW_CONFIG.edgeRetryIntervalMs)
          store.setEdgePollId(poll)
          store.setEdgeTimeoutId(
            setTimeout(() => {
              clearInterval(poll)
              store.setEdgePollId(null)
              store.setEdgeTimeoutId(null)
            }, DRAW_CONFIG.edgeRetryTimeoutMs),
          )
        }
      })
      .catch((err) => debugError("[DRAW CONTROL] Failed to enable draw mode:", shapeName, err))
  })()
}
