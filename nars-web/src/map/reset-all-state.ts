// ─── RESET ALL MODULE STATE ───────────────────────────────────────────────────
// Barrel that resets all module-level mutable state. Used in test setup
// (beforeEach) to ensure clean test isolation. Called once for every store
// covered here so a fresh Pinia instance (or HMR) starts from defaults.
//
// Uses dynamic imports internally so these modules are only resolved at
// call time.  This avoids vitest mocking errors when a test file mocks a
// transitive dependency without exporting every name that this barrel
// references — since vitest validates *static* imports eagerly against
// the mock's export table.

export async function resetAllState(): Promise<void> {
  const safe = async (path: string, name: string) => {
    try {
      const mod = await import(path)
      const fn = (mod as Record<string, unknown>)[name]
      if (typeof fn === "function") (fn as () => void)()
    } catch {
      /* skip — module may be partially mocked */
    }
  }

  await safe("./draw/draw-state", "resetDrawState")
  await safe("./edit/edit-state", "resetEditState")
  await safe("./undo", "resetUndoStack")
  await safe("./snapping/snapping", "resetSnapState")
  await safe("./draw/draw-control", "resetDrawControl")
  await safe("./map-boundary", "resetBoundaryEvents")
  await safe("./rotation", "resetRotation")
  await safe("./core/state", "resetMapState")
  await safe("./rendering/geometry", "resetGeometryState")
  await safe("./map-init", "resetMapInit")
  await safe("../../lib/logger", "resetLoggerState")
  await safe("../../stores/modalStore", "resetModalBridge")
  await safe("../../stores/modalStore", "resetModalStore")
  await safe("../../stores/confirmStore", "resetConfirmBridge")
  await safe("../../stores/toastStore", "resetToastStore")
  await safe("../../stores/layerStore", "resetLayerCache")
  await safe("../../stores/appStore", "resetAppStore")
  await safe("../../stores/featuresStore", "resetFeaturesStore")
  await safe("../../stores/selectionStore", "resetSelectionStore")
  await safe("../../stores/fieldStore", "resetFieldStore")
  await safe("../../stores/contextMenuStore", "resetContextMenuStore")
}
