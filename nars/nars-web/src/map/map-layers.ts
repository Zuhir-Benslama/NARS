// ─── MAP SOURCES & LAYERS ─────────────────────────────────────────────────────
// GeoJSON sources, feature layers, endpoint layers, and drawing preview.

import maplibregl from "maplibre-gl"
import { ctx } from "./core/state"
import { debugLog } from "../utils/debug"
import { addBoundaryClickEvents } from "./map-boundary"

export function initSources(): void {
  const map = ctx.map

  for (const name of [
    "boundaries",
    "scattered",
    "features",
    "drawing-preview",
    "selection",
    "endpoints",
  ]) {
    if (!map.getSource(name)) {
      map.addSource(name, {
        type: "geojson",
        data: { type: "FeatureCollection", features: [] },
      })
    }
  }

  ctx.boundariesSource = map.getSource("boundaries") as maplibregl.GeoJSONSource
  ctx.scatteredSource = map.getSource("scattered") as maplibregl.GeoJSONSource
  ctx.featuresSource = map.getSource("features") as maplibregl.GeoJSONSource
  ctx.endpointsSource = map.getSource("endpoints") as maplibregl.GeoJSONSource

  debugLog("[initSources] ctx.featuresSource set:", !!ctx.featuresSource)

  addFeatureLayers(map)
  addDrawingPreviewLayer(map)
  addEndpointLayers(map)
}

// ─── ENDPOINT LAYERS ──────────────────────────────────────────────────────────

export function addEndpointLayers(map: maplibregl.Map): void {
  map.addLayer({
    id: "nars-endpoint-start",
    type: "circle",
    source: "endpoints",
    filter: ["==", ["get", "endpointType"], "start"],
    paint: {
      "circle-color": ["get", "color"],
      "circle-radius": 12,
      "circle-stroke-color": "#000000",
      "circle-stroke-width": 3,
    },
  })

  map.addLayer({
    id: "nars-endpoint-start-label",
    type: "symbol",
    source: "endpoints",
    filter: ["==", ["get", "endpointType"], "start"],
    layout: {
      "text-field": ">",
      "text-size": 20,
      "text-font": ["Open Sans Bold", "Arial Unicode MS Bold"],
      "text-allow-overlap": true,
      "text-optional": true,
      "text-rotate": ["get", "angle"],
      "text-rotation-alignment": "viewport",
    },
    paint: {
      "text-color": "#ffffff",
      "text-halo-color": "#000000",
      "text-halo-width": 2,
    },
  })

  map.addLayer({
    id: "nars-endpoint-end",
    type: "circle",
    source: "endpoints",
    filter: ["==", ["get", "endpointType"], "end"],
    paint: {
      "circle-color": ["get", "color"],
      "circle-radius": 12,
      "circle-stroke-color": "#000000",
      "circle-stroke-width": 3,
    },
  })

  map.addLayer({
    id: "nars-endpoint-end-label",
    type: "symbol",
    source: "endpoints",
    filter: ["==", ["get", "endpointType"], "end"],
    layout: {
      "text-field": "✕",
      "text-size": 20,
      "text-font": ["Open Sans Bold", "Arial Unicode MS Bold"],
      "text-allow-overlap": true,
      "text-optional": true,
      "text-rotate": ["get", "angle"],
      "text-rotation-alignment": "viewport",
    },
    paint: {
      "text-color": "#ffffff",
      "text-halo-color": "#000000",
      "text-halo-width": 2,
    },
  })
}

// ─── FEATURE LAYERS ───────────────────────────────────────────────────────────

export function addFeatureLayers(map: maplibregl.Map): void {
  const layers = map.getStyle().layers || []
  const firstSymbolId = layers.find((l) => l.type === "symbol")?.id

  map.addLayer(
    {
      id: "nars-selection",
      type: "line",
      source: "selection",
      paint: {
        "line-color": "#f1c40f",
        "line-width": 4,
        "line-dasharray": [6, 3],
        "line-opacity": 0.9,
      },
    },
    firstSymbolId,
  )

  map.addLayer(
    {
      id: "nars-boundaries",
      type: "line",
      source: "boundaries",
      paint: {
        "line-color": "#e74c3c",
        "line-width": 2.5,
        "line-opacity": 0.8,
      },
    },
    firstSymbolId,
  )

  addBoundaryClickEvents(map)

  map.addLayer(
    {
      id: "nars-polygon-fill",
      type: "fill",
      source: "features",
      filter: ["==", ["get", "geomType"], "Polygon"],
      paint: {
        "fill-color": ["get", "fillColor"],
        "fill-opacity": ["get", "fillOpacity"],
      },
    },
    firstSymbolId,
  )

  map.addLayer(
    {
      id: "nars-polygon-stroke",
      type: "line",
      source: "features",
      filter: ["==", ["get", "geomType"], "Polygon"],
      paint: {
        "line-color": ["get", "lineColor"],
        "line-width": ["get", "lineWidth"],
      },
    },
    firstSymbolId,
  )

  map.addLayer(
    {
      id: "nars-polygon-label",
      type: "symbol",
      source: "features",
      filter: ["==", ["get", "geomType"], "Polygon"],
      layout: {
        "text-field": ["get", "label"],
        "text-size": 12,
        "text-anchor": "center",
        "text-allow-overlap": false,
        "text-optional": true,
      },
      paint: {
        "text-color": ["get", "lineColor"],
        "text-halo-color": "#ffffff",
        "text-halo-width": 2,
      },
    },
    firstSymbolId,
  )

  map.addLayer(
    {
      id: "nars-line",
      type: "line",
      source: "features",
      filter: ["==", ["get", "geomType"], "LineString"],
      paint: {
        "line-color": ["get", "lineColor"],
        "line-width": ["get", "lineWidth"],
      },
    },
    firstSymbolId,
  )

  map.addLayer(
    {
      id: "nars-line-label",
      type: "symbol",
      source: "features",
      filter: ["==", ["get", "geomType"], "LineString"],
      layout: {
        "text-field": ["get", "label"],
        "text-size": 11,
        "symbol-placement": "line",
        "text-rotation-alignment": "map",
        "text-font": ["Open Sans Bold", "Open Sans Regular", "Arial Unicode MS Regular"],
        "text-allow-overlap": false,
        "text-optional": true,
      },
      paint: {
        "text-color": ["get", "lineColor"],
        "text-halo-color": "#ffffff",
        "text-halo-width": 3,
      },
    },
    firstSymbolId,
  )

  map.addLayer(
    {
      id: "nars-point",
      type: "circle",
      source: "features",
      filter: ["==", ["get", "geomType"], "Point"],
      paint: {
        "circle-color": ["get", "circleColor"],
        "circle-radius": ["get", "circleRadius"],
        "circle-stroke-color": "#ffffff",
        "circle-stroke-width": 2,
      },
    },
    firstSymbolId,
  )

  map.addLayer(
    {
      id: "nars-point-label",
      type: "symbol",
      source: "features",
      filter: ["==", ["get", "geomType"], "Point"],
      layout: {
        "text-field": ["get", "label"],
        "text-size": 10,
        "text-font": ["Open Sans Regular", "Arial Unicode MS Regular"],
        "text-anchor": "center",
        "text-allow-overlap": false,
        "text-optional": true,
      },
      paint: {
        "text-color": ["get", "textColor"],
        "text-halo-color": "#ffffff",
        "text-halo-width": 1,
      },
    },
    firstSymbolId,
  )
}

// ─── DRAWING PREVIEW ──────────────────────────────────────────────────────────

export function addDrawingPreviewLayer(map: maplibregl.Map): void {
  const layers = map.getStyle().layers || []
  const firstSymbolId = layers.find((l) => l.type === "symbol")?.id
  map.addLayer(
    {
      id: "drawing-preview-line",
      type: "line",
      source: "drawing-preview",
      paint: {
        "line-color": "#3498db",
        "line-width": 3,
        "line-dasharray": [3, 2],
        "line-opacity": 0.8,
      },
    },
    firstSymbolId,
  )
}

export function updateDrawingPreview(geometry: [number, number][] | null): void {
  const source = ctx.map.getSource("drawing-preview") as maplibregl.GeoJSONSource
  if (!source) return
  const features: GeoJSON.Feature[] =
    geometry && geometry.length > 0
      ? [
          geometry.length >= 3
            ? {
                type: "Feature",
                geometry: {
                  type: "Polygon",
                  coordinates: [[...geometry, geometry[0]]],
                },
                properties: {},
              }
            : {
                type: "Feature",
                geometry: { type: "LineString", coordinates: geometry },
                properties: {},
              },
        ]
      : []
  source.setData({ type: "FeatureCollection", features })
}
