import type * as maplibregl from "maplibre-gl"
import { getCtx } from "./core/state"
import { debugLog } from "../utils/debug"
import { addBoundaryClickEvents } from "./map-boundary"

function getGeoJSON(map: maplibregl.Map, id: string): maplibregl.GeoJSONSource {
  const source = map.getSource(id)
  if (!source || source.type !== "geojson") {
    throw new Error(`[map-layers] Source "${id}" not found or not GeoJSON`)
  }
  return source as maplibregl.GeoJSONSource
}

const FEATURE_LAYERS: maplibregl.LayerSpecification[] = [
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
  {
    id: "nars-draft-polygon-fill",
    type: "fill",
    source: "drafts",
    filter: ["==", ["get", "geomType"], "Polygon"],
    paint: {
      "fill-color": ["get", "fillColor"],
      "fill-opacity": ["get", "fillOpacity"],
    },
  },
  {
    id: "nars-draft-line",
    type: "line",
    source: "drafts",
    filter: ["in", ["get", "geomType"], ["literal", ["LineString", "Polygon"]]],
    paint: {
      "line-color": ["get", "lineColor"],
      "line-width": ["get", "lineWidth"],
      "line-dasharray": [4, 3],
    },
  },
  {
    id: "nars-point",
    type: "circle",
    source: "features",
    layout: { visibility: "visible" },
    filter: ["==", ["get", "geomType"], "Point"],
    paint: {
      "circle-color": [
        "case",
        ["==", ["get", "circleColor"], ""],
        "#27ae60",
        ["coalesce", ["get", "circleColor"], "#27ae60"],
      ],
      // Scale the marker with zoom (never below 4px) so zoomed-out entrances
      // don't merge into one blob, while keeping the full 8px size once the
      // map is zoomed in. The circle layer has no collision handling, so the
      // radius floor is what guarantees it is always drawn.
      "circle-radius": ["interpolate", ["linear"], ["zoom"], 12, 4, 16, 8],
      "circle-opacity": 1,
      "circle-stroke-color": "#ffffff",
      "circle-stroke-width": ["interpolate", ["linear"], ["zoom"], 12, 1, 16, 2],
      "circle-stroke-opacity": 1,
    },
  },
  {
    id: "nars-point-label",
    type: "symbol",
    source: "features",
    filter: ["==", ["get", "geomType"], "Point"],
    layout: {
      "text-field": ["get", "label"],
      "text-size": ["interpolate", ["linear"], ["zoom"], 12, 8, 16, 10],
      "text-font": ["Open Sans Regular", "Arial Unicode MS Regular"],
      "text-anchor": "center",
      // Marker numbers (house entrances) must never be culled by MapLibre's
      // collision detection. With allow-overlap off they vanished whenever
      // they touched a road label, an endpoint arrow or a neighbouring number,
      // which is what happened when zooming out. Ignoring placement also stops
      // them from pushing other labels away.
      "text-allow-overlap": true,
      "text-ignore-placement": true,
      "text-optional": true,
    },
    paint: {
      "text-color": ["get", "textColor"],
      "text-halo-color": "#ffffff",
      "text-halo-width": 1,
    },
  },
]

const ENDPOINT_LAYERS: Array<{ type: "start" | "end"; layers: maplibregl.LayerSpecification[] }> = [
  {
    type: "start",
    layers: [
      {
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
      },
      {
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
      },
    ],
  },
  {
    type: "end",
    layers: [
      {
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
      },
      {
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
      },
    ],
  },
]

export function initSources(): void {
  const ctx = getCtx()
  const map = ctx.map

  for (const name of ["boundaries", "scattered", "features", "selection", "endpoints", "drafts"]) {
    if (!map.getSource(name)) {
      map.addSource(name, {
        type: "geojson",
        data: { type: "FeatureCollection", features: [] },
      })
    }
  }

  ctx.boundariesSource = getGeoJSON(map, "boundaries")
  ctx.scatteredSource = getGeoJSON(map, "scattered")
  ctx.featuresSource = getGeoJSON(map, "features")
  ctx.endpointsSource = getGeoJSON(map, "endpoints")
  ctx.draftsSource = getGeoJSON(map, "drafts")

  debugLog("[initSources] ctx.featuresSource set:", !!ctx.featuresSource)

  addFeatureLayers(map)
  addEndpointLayers(map)
}

export function addEndpointLayers(map: maplibregl.Map): void {
  // Endpoint markers must render on top of the road polyline (and other
  // feature layers), so they are appended without a before-id instead of
  // being inserted before the first symbol layer (which would place them
  // underneath the line layers).
  for (const group of ENDPOINT_LAYERS) {
    for (const layer of group.layers) {
      map.addLayer(layer)
    }
  }
}

export function addFeatureLayers(map: maplibregl.Map): void {
  const layers = map.getStyle().layers || []
  const firstSymbolId = layers.find((l) => l.type === "symbol")?.id

  for (const layer of FEATURE_LAYERS) {
    map.addLayer(layer, firstSymbolId)
  }

  addBoundaryClickEvents(map)
}
