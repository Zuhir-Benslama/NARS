// ─── MAP INITIALIZATION ───────────────────────────────────────────────────────
// Creates the MapLibre map instance, initializes tile styles, sets up Geoman,
// and exposes the setBaseLayer public API.

import maplibregl from "maplibre-gl"
import { createGeomanInstance } from "@geoman-io/maplibre-geoman-free"
import { getCtx, _setCtx } from "./core/state"
import { useFeaturesStore } from "../stores/featuresStore"
import type { MapContext } from "./core/state"
import { initSources } from "./map-layers"
import { suppressGeomanFill, ensureGeomanDrawEdgesVisible } from "./edit/edit-mode"
import { updateEndpointMarkers } from "./roads/road-directions"
import { refreshLayerVisibility } from "./rendering/labels"
import { MAP_CONFIG } from "../config"
import { debugWarn } from "../utils/debug"

let currentActiveStyle: maplibregl.StyleSpecification | undefined
let _styleSwitchInFlight = false

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

  const geomanOptions = buildGeomanOptions()

  _setBaseLayer = (key: string) => switchBaseLayer(key, geomanOptions)

  await new Promise<void>((resolve) => {
    const onLoad = () => resolve()
    ctx.map.once("load", onLoad)
    setTimeout(() => {
      ctx.map.off("load", onLoad)
      debugWarn("[MAP] Map load event timed out after 15 s")
      resolve()
    }, 15_000)
  })

  await initGeoman(ctx.map, geomanOptions)
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
  getCtx().geoman = await createGeomanInstance(map, options)
  suppressGeomanFill()
  ensureGeomanDrawEdgesVisible()
  map.doubleClickZoom.disable()
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
    debugWarn("[MAP] Style switch already in progress — ignoring concurrent request")
    return
  }
  _styleSwitchInFlight = true
  currentActiveStyle = next

  try {
    const map = ctx.map
    const styleLoaded = new Promise<void>((resolve) => {
      map.once("style.load", () => resolve())
    })
    const styleTimeout = new Promise<never>((_, reject) =>
      setTimeout(() => reject(new Error("Style load timeout")), MAP_CONFIG.styleLoadTimeout),
    )

    map.setStyle(next)
    try {
      await Promise.race([styleLoaded, styleTimeout])
    } catch (err) {
      debugWarn("[MAP] Style load failed:", err)
    }

    initSources()
    const featuresStore = useFeaturesStore()
    featuresStore.updateSource()
    if (ctx.boundariesGeoJson) ctx.boundariesSource?.setData(ctx.boundariesGeoJson)
    if (ctx.scatteredGeoJson) ctx.scatteredSource?.setData(ctx.scatteredGeoJson)
    refreshLayerVisibility()

    updateEndpointMarkers()

    await disposeGeoman()
    await initGeoman(map, geomanOptions)
  } finally {
    _styleSwitchInFlight = false
  }
}
