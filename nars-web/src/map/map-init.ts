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

export async function initMap(): Promise<void> {
  const satelliteStyle: maplibregl.StyleSpecification = {
    version: 8,
    sources: {
      satellite: {
        type: "raster",
        tiles: [...MAP_CONFIG.tileUrls.satellite],
        tileSize: 256,
        maxzoom: 17,
      },
    },
    layers: [{ id: "satellite", type: "raster", source: "satellite" }],
  }

  _setCtx(ctx)

  ctx.map = new maplibregl.Map({
    container: "map",
    style: satelliteStyle,
    center: MAP_CONFIG.defaultCenter,
    zoom: MAP_CONFIG.defaultZoom,
    bearing: MAP_CONFIG.defaultBearing,
    pitch: MAP_CONFIG.defaultPitch,
    minZoom: 4,
    maxZoom: 18,
  })

  if (import.meta.env.DEV) {
    ;(window as unknown as Record<string, unknown>).__narsMap = ctx.map
  }

  ctx.satelliteStyle = satelliteStyle
  ctx.streetStyle = {
    version: 8,
    sources: {
      osm: {
        type: "raster",
        tiles: [...MAP_CONFIG.tileUrls.street],
        tileSize: 256,
        maxzoom: 19,
        attribution:
          '© <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
      },
    },
    layers: [{ id: "osm", type: "raster", source: "osm" }],
  }
  ctx.lightStyle = {
    version: 8,
    sources: {
      carto: {
        type: "raster",
        tiles: [...MAP_CONFIG.tileUrls.light],
        tileSize: 256,
        maxzoom: 19,
      },
    },
    layers: [{ id: "carto", type: "raster", source: "carto" }],
  }
  ctx.darkStyle = {
    version: 8,
    sources: {
      "carto-dark": {
        type: "raster",
        tiles: [...MAP_CONFIG.tileUrls.dark],
        tileSize: 256,
        maxzoom: 19,
      },
    },
    layers: [{ id: "carto-dark", type: "raster", source: "carto-dark" }],
  }

  currentActiveStyle = satelliteStyle

  const geomanOptions = {
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

  _setBaseLayer = async (key: string) => {
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
      setTimeout(() => reject(new Error("Style load timeout")), 10000),
    )

    ctx.map.setStyle(next)
    await Promise.race([styleLoaded, styleTimeout])

    initSources()
    featuresStore.updateSource()
    if (ctx.boundariesGeoJson) ctx.boundariesSource?.setData(ctx.boundariesGeoJson)
    if (ctx.scatteredGeoJson) ctx.scatteredSource?.setData(ctx.scatteredGeoJson)
    refreshLayerVisibility()

    updateEndpointMarkers()

    ctx.geoman = await createGeomanInstance(ctx.map, geomanOptions)
    suppressGeomanFill()
    ensureGeomanDrawEdgesVisible()
    ctx.map.doubleClickZoom.disable()
  }

  await new Promise<void>((resolve) => ctx.map.once("load", resolve))

  ctx.geoman = await createGeomanInstance(ctx.map, geomanOptions)
  suppressGeomanFill()
  ensureGeomanDrawEdgesVisible()

  ctx.map.doubleClickZoom.disable()

  initSources()
}
