// ─── SHARED MAP STATE ─────────────────────────────────────────────────────────
// All Leaflet layer instances live here so every sub-module can import them
// without creating circular dependencies with map.ts.
// Populated once by initMap() on startup.

declare const L: typeof import('leaflet') & {
    Draw: any
    Control: typeof import('leaflet').Control & { Draw: new (opts: any) => any }
    DrawEvents: any
}

export const POLYLINE_WEIGHT = 8

export const ctx: {
    map:                   L.Map
    drawnItems:            L.FeatureGroup
    lineEndpointLayer:     L.LayerGroup
    scatteredLayer:        L.LayerGroup
    perimeterLabelLayer:   L.LayerGroup
    polygonEdgeLabelLayer: L.LayerGroup
    boundariesLayer:       L.GeoJSON | null
    drawControl:           L.Control.Draw | null
} = {} as any
