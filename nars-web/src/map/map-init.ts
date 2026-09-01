// ─── MAP INITIALIZATION ───────────────────────────────────────────────────────
// Creates the MapLibre map instance, initializes tile styles, sets up Geoman,
// and exposes the setBaseLayer public API.

import maplibregl from "maplibre-gl"
import { getCtx, _setCtx } from "./core/state"
import { useFeaturesStore } from "../stores/featuresStore"
import type { MapContext } from "./core/state"
import { initSources } from "./map-layers"
import { updateEndpointMarkers } from "./roads/road-directions"
import { refreshLayerVisibility } from "./rendering/labels"
import { MAP_CONFIG } from "../config"
import { debugLog, debugWarn } from "../utils/debug"
import { suppressGeomanFill, ensureGeomanDrawEdgesVisible } from "./edit/edit-mode"

let currentActiveStyle: maplibregl.StyleSpecification | undefined
let _styleSwitchInFlight = false
let _pendingStyleKey: string | null = null
let _pendingWaiters: (() => void)[] = []

let _geomanOptions: ReturnType<typeof buildGeomanOptions> | null = null
let _geomanInitPromise: Promise<void> | null = null

let _setBaseLayer: (key: string) => void | Promise<void> = () => {
  debugWarn("setBaseLayer called before map initialization")
}

export function setBaseLayer(key: string): void | Promise<void> {
  return _setBaseLayer(key)
}

function buildRasterStyle(
  tiles: readonly string[],
  sourceId: string,
  maxzoom: number,
  extra?: Partial<maplibregl.RasterSourceSpecification>,
): maplibregl.StyleSpecification {
  return {
    version: 8,
    sources: {
      [sourceId]: {
        type: "raster",
        tiles: [...tiles],
        tileSize: 256,
        maxzoom,
        ...extra,
      },
    },
    layers: [{ id: sourceId, type: "raster", source: sourceId }],
  }
}

export function resetMapInit(): void {
  currentActiveStyle = undefined
  _styleSwitchInFlight = false
  _pendingStyleKey = null
  _pendingWaiters = []
  _geomanOptions = null
  _geomanInitPromise = null
  _setBaseLayer = (_key: string) => {
    debugWarn("setBaseLayer called before map initialization")
  }
}

export async function disposeGeoman(): Promise<void> {
  const { geoman } = getCtx()
  if (!geoman || geoman.destroyed) return
  try {
    await geoman.destroy({ removeSources: false })
  } catch (err) {
    debugWarn("[MAP] Geoman dispose failed:", err)
  } finally {
    getCtx().geoman = undefined
  }
}

if (import.meta.hot) {
  import.meta.hot.dispose(() => resetMapInit())
}

export async function initMap(): Promise<void> {
  const satelliteStyle = buildRasterStyle(
    MAP_CONFIG.tileUrls.satellite,
    "satellite",
    MAP_CONFIG.tileMaxZoomSatellite,
  )

  const map = new maplibregl.Map({
    container: "map",
    style: satelliteStyle,
    center: MAP_CONFIG.defaultCenter,
    zoom: MAP_CONFIG.defaultZoom,
    bearing: MAP_CONFIG.defaultBearing,
    pitch: MAP_CONFIG.defaultPitch,
    minZoom: MAP_CONFIG.minZoom,
    maxZoom: MAP_CONFIG.maxZoom,
  })

  const streetStyle = buildRasterStyle(
    MAP_CONFIG.tileUrls.street,
    "osm",
    MAP_CONFIG.tileMaxZoomStreet,
    {
      attribution:
        '© <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
    },
  )
  const lightStyle = buildRasterStyle(
    MAP_CONFIG.tileUrls.light,
    "carto",
    MAP_CONFIG.tileMaxZoomLight,
  )
  const darkStyle = buildRasterStyle(
    MAP_CONFIG.tileUrls.dark,
    "carto-dark",
    MAP_CONFIG.tileMaxZoomDark,
  )

  const ctx: MapContext = {
    map,
    satelliteStyle,
    streetStyle,
    lightStyle,
    darkStyle,
  }
  _setCtx(ctx)

  if (import.meta.env.DEV) {
    ;(window as unknown as Record<string, unknown>).__narsMap = ctx.map
  }

  currentActiveStyle = satelliteStyle

  _geomanOptions = buildGeomanOptions()

  _setBaseLayer = (key: string) => switchBaseLayer(key, _geomanOptions!)

  let loadTimer: ReturnType<typeof setTimeout> | undefined
  await new Promise<void>((resolve) => {
    const onLoad = () => {
      clearTimeout(loadTimer)
      resolve()
    }
    void ctx.map.once("load", onLoad)
    loadTimer = setTimeout(() => {
      ctx.map.off("load", onLoad)
      debugWarn("[MAP] Map load event timed out after 15 s")
      resolve()
    }, 15_000)
  })

  initSources()
}

function buildGeomanOptions() {
  return {
    settings: {
      useControlsUi: false,
      useDefaultLayers: true,
    },
    controls: {
      draw: {
        polygon: { active: false },
        line: { active: false },
        marker: { active: false },
        circle: { active: false },
      },
      edit: {
        change: { active: false },
        drag: { active: false },
        delete: { active: true },
      },
    },
  }
}

async function initGeoman(
  map: maplibregl.Map,
  options: ReturnType<typeof buildGeomanOptions>,
): Promise<void> {
  // Load the geoman stylesheet together with the library so the editing
  // controls/vertex markers are styled the first time an edit session opens.
  await import("@geoman-io/maplibre-geoman-free/dist/maplibre-geoman.css")
  const { createGeomanInstance } = await import("@geoman-io/maplibre-geoman-free")
  getCtx().geoman = await createGeomanInstance(map, options)
  suppressGeomanFill()
  ensureGeomanDrawEdgesVisible()
  map.doubleClickZoom.disable()
  debugLog("[MAP] Geoman initialized")
}

/**
 * Lazily ensure the Geoman editor is initialized. The heavy geoman bundle is
 * deferred until the user first enters a draw or edit session (see initMap),
 * then this becomes the single choke point that creates the instance on
 * demand. Concurrent callers share one in-flight initialization promise.
 */
export async function ensureGeoman(): Promise<void> {
  const ctx = getCtx()
  if (ctx.geoman && !ctx.geoman.destroyed) return
  if (!_geomanOptions) throw new Error("[MAP] ensureGeoman called before initMap()")
  if (!_geomanInitPromise) {
    _geomanInitPromise = initGeoman(ctx.map, _geomanOptions).finally(() => {
      _geomanInitPromise = null
    })
  }
  await _geomanInitPromise
}

async function switchBaseLayer(
  key: string,
  geomanOptions: ReturnType<typeof buildGeomanOptions>,
): Promise<void> {
  const ctx = getCtx()
  const styles: Record<string, maplibregl.StyleSpecification | undefined> = {
    satellite: ctx.satelliteStyle,
    street: ctx.streetStyle,
    light: ctx.lightStyle,
    dark: ctx.darkStyle,
  }
  const next = styles[key]
  if (!next || next === currentActiveStyle) return
  if (!ctx.map) return
  if (_styleSwitchInFlight) {
    // Don't drop the user's selection: remember the LATEST requested style,
    // apply it once the in-flight switch finishes, and resolve the caller
    // only when its request has actually been applied (or superseded).
    _pendingStyleKey = key
    debugWarn("[MAP] Style switch already in progress — queued:", key)
    return new Promise<void>((resolve) => {
      _pendingWaiters.push(resolve)
    })
  }
  _styleSwitchInFlight = true
  const previous = currentActiveStyle
  // Capture whether geoman was already initialized before setStyle wipes the
  // map (and with it geoman's layers). With lazy geoman init, an editor that
  // was never opened stays deferred across style switches. Only a session
  // that already initialized geoman must be recreated on the new style.
  const geomanWasPresent = !!getCtx().geoman

  try {
    const map = ctx.map
    let styleResolve!: () => void
    let styleTimeoutId: ReturnType<typeof setTimeout> | undefined
    const styleListener = () => {
      clearTimeout(styleTimeoutId)
      styleResolve()
    }
    const styleLoaded = new Promise<void>((resolve) => {
      styleResolve = resolve
    })
    const styleTimeout = new Promise<never>((_, reject) => {
      styleTimeoutId = setTimeout(
        () => reject(new Error("Style load timeout")),
        MAP_CONFIG.styleLoadTimeout,
      )
    })

    void map.once("style.load", styleListener)
    map.setStyle(next)
    let styleOk = false
    try {
      await Promise.race([styleLoaded, styleTimeout])
      styleOk = true
    } catch (err) {
      debugWarn("[MAP] Style load failed:", err)
    } finally {
      map.off("style.load", styleListener)
    }
    // Commit the new style as active only once it actually loaded. If the load
    // failed the map still shows the old style — leaving currentActiveStyle
    // pointing at the new one would make later switches for this key a no-op
    // (see the early return above) and desync the UI's active-style state.
    currentActiveStyle = styleOk ? next : previous

    initSources()
    const featuresStore = useFeaturesStore()
    featuresStore.updateSource()
    if (ctx.boundariesGeoJson) ctx.boundariesSource?.setData(ctx.boundariesGeoJson)
    if (ctx.scatteredGeoJson) ctx.scatteredSource?.setData(ctx.scatteredGeoJson)
    refreshLayerVisibility()

    updateEndpointMarkers()

    if (geomanWasPresent) {
      await disposeGeoman()
      await initGeoman(map, geomanOptions)
    }
  } finally {
    _styleSwitchInFlight = false
    const waiters = _pendingWaiters
    _pendingWaiters = []
    const pending = _pendingStyleKey
    _pendingStyleKey = null
    if (pending) {
      void switchBaseLayer(pending, geomanOptions).finally(() => {
        waiters.forEach((w) => w())
      })
    } else {
      waiters.forEach((w) => w())
    }
  }
}
