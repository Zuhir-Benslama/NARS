// ─── MAP INITIALIZATION ───────────────────────────────────────────────────────
// Creates the MapLibre map instance, initializes tile styles, sets up Geoman,
// and exposes the setBaseLayer public API.

import maplibregl from "maplibre-gl"
import { createGeomanInstance } from "@geoman-io/maplibre-geoman-free"
import { ctx, featuresStore, _setCtx } from "./core/state"
import { initSources } from "./map-layers"
import { suppressGeomanFill } from "./edit/edit-mode"
import { updateEndpointMarkers } from "./roads/road-directions"
import { refreshLayerVisibility } from "./rendering/labels"
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
        tiles: [
          "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
        ],
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
    center: [2.5, 28.0],
    zoom: 5,
    bearing: 0,
    pitch: 0,
    minZoom: 4,
    maxZoom: 18,
  })

  if (import.meta.env.DEV) {
    /* eslint-disable-next-line @typescript-eslint/no-explicit-any */
    ;(window as any).__narsMap = ctx.map
  }

  ctx.satelliteStyle = satelliteStyle
  ctx.streetStyle = {
    version: 8,
    sources: {
      osm: {
        type: "raster",
        tiles: [
          "https://a.tile.openstreetmap.org/{z}/{x}/{y}.png",
          "https://b.tile.openstreetmap.org/{z}/{x}/{y}.png",
          "https://c.tile.openstreetmap.org/{z}/{x}/{y}.png",
        ],
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
        tiles: [
          "https://a.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png",
          "https://b.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png",
          "https://c.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png",
        ],
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
        tiles: [
          "https://a.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png",
          "https://b.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png",
          "https://c.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png",
        ],
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

    ctx.map.setStyle(next)
    await styleLoaded

    initSources()
    featuresStore.updateSource()
    if (ctx.boundariesGeoJson) ctx.boundariesSource?.setData(ctx.boundariesGeoJson)
    if (ctx.scatteredGeoJson) ctx.scatteredSource?.setData(ctx.scatteredGeoJson)
    refreshLayerVisibility()

    updateEndpointMarkers()

    ctx.geoman = await createGeomanInstance(ctx.map, geomanOptions)
    suppressGeomanFill()
    ctx.map.doubleClickZoom.disable()
  }

  await new Promise<void>((resolve) => ctx.map.once("load", resolve))

  ctx.geoman = await createGeomanInstance(ctx.map, geomanOptions)
  suppressGeomanFill()

  ctx.map.doubleClickZoom.disable()

  initSources()
}
