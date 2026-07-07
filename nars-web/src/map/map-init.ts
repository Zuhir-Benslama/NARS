// ─── MAP INITIALIZATION ───────────────────────────────────────────────────────
// Creates the MapLibre map instance, initializes tile styles, sets up Geoman,
// and exposes the setBaseLayer public API.

import maplibregl from "maplibre-gl"
import { createGeomanInstance } from "@geoman-io/maplibre-geoman-free"
import { ctx, featuresStore, _setCtx } from "./core/state"
import { initSources } from "./map-layers"
import { suppressGeomanFill, ensureGeomanDrawEdgesVisible } from "./edit/edit-mode"
import { updateEndpointMarkers } from "./roads/road-directions"
import { refreshLayerVisibility } from "./rendering/labels"
import { MAP_CONFIG } from "../config"
import { debugWarn } from "../utils/debug"

let currentActiveStyle: maplibregl.StyleSpecification | undefined

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

export async function initMap(): Promise<void> {
  const satelliteStyle = buildRasterStyle(MAP_CONFIG.tileUrls.satellite, "satellite", 17)

  _setCtx(ctx)

  ctx.map = new maplibregl.Map({
    container: "map",
    style: satelliteStyle,
    center: MAP_CONFIG.defaultCenter,
    zoom: MAP_CONFIG.defaultZoom,
    bearing: MAP_CONFIG.defaultBearing,
    pitch: MAP_CONFIG.defaultPitch,
    minZoom: MAP_CONFIG.minZoom,
    maxZoom: MAP_CONFIG.maxZoom,
  })

  if (import.meta.env.DEV) {
    ;(window as unknown as Record<string, unknown>).__narsMap = ctx.map
  }

  ctx.satelliteStyle = satelliteStyle
  ctx.streetStyle = buildRasterStyle(MAP_CONFIG.tileUrls.street, "osm", 19, {
    attribution:
      '© <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
  })
  ctx.lightStyle = buildRasterStyle(MAP_CONFIG.tileUrls.light, "carto", 19)
  ctx.darkStyle = buildRasterStyle(MAP_CONFIG.tileUrls.dark, "carto-dark", 19)

  currentActiveStyle = satelliteStyle

  const geomanOptions = buildGeomanOptions()

  _setBaseLayer = (key: string) => switchBaseLayer(key, geomanOptions)

  await new Promise<void>((resolve) => ctx.map.once("load", resolve))

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
  ctx.geoman = await createGeomanInstance(map, options)
  suppressGeomanFill()
  ensureGeomanDrawEdgesVisible()
  map.doubleClickZoom.disable()
}

async function switchBaseLayer(
  key: string,
  geomanOptions: ReturnType<typeof buildGeomanOptions>,
): Promise<void> {
  const styles: Record<string, maplibregl.StyleSpecification | undefined> = {
    satellite: ctx.satelliteStyle,
    street: ctx.streetStyle,
    light: ctx.lightStyle,
    dark: ctx.darkStyle,
  }
  const next = styles[key]
  if (!next || next === currentActiveStyle) return
  currentActiveStyle = next

  const styleLoaded = new Promise<void>((resolve) => {
    ctx.map!.once("style.load", () => resolve())
  })
  const styleTimeout = new Promise<void>((_, reject) =>
    setTimeout(() => reject(new Error("Style load timeout")), MAP_CONFIG.styleLoadTimeout),
  )

  ctx.map.setStyle(next)
  await Promise.race([styleLoaded, styleTimeout])

  initSources()
  featuresStore.updateSource()
  if (ctx.boundariesGeoJson) ctx.boundariesSource?.setData(ctx.boundariesGeoJson)
  if (ctx.scatteredGeoJson) ctx.scatteredSource?.setData(ctx.scatteredGeoJson)
  refreshLayerVisibility()

  updateEndpointMarkers()

  await initGeoman(ctx.map!, geomanOptions)
}
