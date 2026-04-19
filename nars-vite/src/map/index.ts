// ─── MAP ORCHESTRATOR ─────────────────────────────────────────────────────────

import { applyInitialLang } from '../i18n'
import { ctx, featuresStore, _setCtx } from './state'
import { showToast } from '../toast'
import { initRotationControls } from './rotation'
import { refreshLayerVisibility } from './labels'
import { updateEndpointMarkers } from './road-directions'
import { registerDrawEvents } from './draw-events'
import { registerGeomanEvents } from './geoman-events'
import { suppressGeomanFill } from './edit-mode'
import { sanitizeHtml, sanitizeText } from '../utils/sanitize'
import { debugWarn, debugLog } from '../utils/debug'

import maplibregl from 'maplibre-gl'
import { createGeomanInstance } from '@geoman-io/maplibre-geoman-free'

// ─── RE-EXPORTS ───────────────────────────────────────────────────────────────

export { displayCommuneBoundary } from './geometry'
export { bindContextMenu } from './context-menu'
export { fetchRoadSide, computeBisNumber } from './features'
export { createEntranceIconHtml, areaStyle } from './styles'
export { loadFromDatabase, loadUserAndCommune } from './loader'
export { navigatePhase, goToPhase, setPhase } from './phase-nav'
export { setHouseNumbers, getFeatureType } from './house-numbering'

// ─── BASE LAYER SWITCHER ──────────────────────────────────────────────────────

let currentActiveStyle: maplibregl.StyleSpecification | undefined

// Internal implementation — assigned in initMap()
let _setBaseLayer: (key: string) => void | Promise<void> = () => {
    debugWarn('setBaseLayer called before map initialization')
}

// Public export - always uses the current implementation
export function setBaseLayer(key: string): void | Promise<void> {
    return _setBaseLayer(key)
}

// ─── MAP INITIALIZATION ───────────────────────────────────────────────────────

export async function initMap(): Promise<void> {
    const satelliteStyle: maplibregl.StyleSpecification = {
        version: 8,
        sources: {
            satellite: {
                type: 'raster',
                tiles: [
                    'https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}',
                ],
                tileSize: 256,
                maxzoom: 17,
            },
        },
        layers: [{ id: 'satellite', type: 'raster', source: 'satellite' }],
    }

    // Register ctx BEFORE setting any properties (Proxy guard requires it).
    _setCtx(ctx)

    ctx.map = new maplibregl.Map({
        container: 'map',
        style: satelliteStyle,
        center: [2.5, 28.0],
        zoom: 5,
        bearing: 0,
        pitch: 0,
        minZoom: 4,
        maxZoom: 18,
    })

    // Expose map instance for debugging — dev only.
    if (import.meta.env.DEV) {
        /* eslint-disable-next-line @typescript-eslint/no-explicit-any */
        ;(window as any).__narsMap = ctx.map
    }

    ctx.satelliteStyle = satelliteStyle
    ctx.streetStyle = {
        version: 8,
        sources: {
            osm: {
                type: 'raster',
                tiles: [
                    'https://a.tile.openstreetmap.org/{z}/{x}/{y}.png',
                    'https://b.tile.openstreetmap.org/{z}/{x}/{y}.png',
                    'https://c.tile.openstreetmap.org/{z}/{x}/{y}.png',
                ],
                tileSize: 256,
                maxzoom: 19,
                attribution: '© <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
            },
        },
        layers: [{ id: 'osm', type: 'raster', source: 'osm' }],
    }
    ctx.lightStyle = {
        version: 8,
        sources: {
            carto: {
                type: 'raster',
                tiles: [
                    'https://a.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png',
                    'https://b.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png',
                    'https://c.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png',
                ],
                tileSize: 256,
                maxzoom: 19,
            },
        },
        layers: [{ id: 'carto', type: 'raster', source: 'carto' }],
    }
    ctx.darkStyle = {
        version: 8,
        sources: {
            'carto-dark': {
                type: 'raster',
                tiles: [
                    'https://a.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png',
                    'https://b.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png',
                    'https://c.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png',
                ],
                tileSize: 256,
                maxzoom: 19,
            },
        },
        layers: [{ id: 'carto-dark', type: 'raster', source: 'carto-dark' }],
    }

    currentActiveStyle = satelliteStyle

    // Geoman options — used both at init and after every setStyle() call
    const geomanOptions = {
        settings: {
            useControlsUi: false,
            useDefaultLayers: true, // Required: Geoman needs its own layers for vertex/edge handles
        },
        controls: {
            draw: {
                polygon: { active: false },
                line: { active: false },
                marker: { active: false },
                circle: { active: false },
            },
            edit: {
                change: { active: false }, // Only enabled during edit mode (enableEditMode)
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

        // Attach the style.load listener BEFORE calling setStyle() to ensure
        // we don't miss the event. MapLibre fires style.load after the new
        // style is fully loaded, but we need our listener registered first.
        const styleLoaded = new Promise<void>((resolve) => {
            ctx.map!.once('style.load', () => resolve())
        })

        ctx.map.setStyle(next)
        await styleLoaded

        // Rebuild NARS GeoJSON sources and rendering layers
        initSources()
        featuresStore.updateSource()
        /* eslint-disable-next-line @typescript-eslint/no-explicit-any */
        if (ctx.boundariesGeoJson) (ctx.boundariesSource as any)?.setData(ctx.boundariesGeoJson)
        /* eslint-disable-next-line @typescript-eslint/no-explicit-any */
        if (ctx.scatteredGeoJson) (ctx.scatteredSource as any)?.setData(ctx.scatteredGeoJson)
        refreshLayerVisibility()

        // Restore road endpoint markers after style change (initSources cleared them)
        updateEndpointMarkers()

        // Rebuild Geoman — its gm_main / gm_temporary / gm_internal sources and
        // all vertex/edge marker layers are gone after setStyle().
        // createGeomanInstance adds fresh sources + layers to the map.
        ctx.geoman = await createGeomanInstance(ctx.map, geomanOptions)
        suppressGeomanFill()
        ctx.map.doubleClickZoom.disable()
    }

    await new Promise<void>((resolve) => ctx.map.once('load', resolve))

    ctx.geoman = await createGeomanInstance(ctx.map, geomanOptions)
    suppressGeomanFill()

    // Disable double-click zoom to allow vertex removal via double-click in edit mode
    ctx.map.doubleClickZoom.disable()

    initSources()
    initRotationControls()
    await applyInitialLang()
    // buildDrawControl is called by watchDrawType (immediate: true) — no need to call here
    registerDrawEvents()
    registerGeomanEvents()
}

// ─── SOURCES ──────────────────────────────────────────────────────────────────

function initSources(): void {
    const map = ctx.map

    for (const name of ['boundaries', 'scattered', 'features', 'drawing-preview', 'selection', 'endpoints']) {
        if (!map.getSource(name)) {
            map.addSource(name, { type: 'geojson', data: { type: 'FeatureCollection', features: [] } })
        }
    }

    /* eslint-disable-next-line @typescript-eslint/no-explicit-any */
    ctx.boundariesSource = map.getSource('boundaries') as any
    /* eslint-disable-next-line @typescript-eslint/no-explicit-any */
    ctx.scatteredSource = map.getSource('scattered') as any
    /* eslint-disable-next-line @typescript-eslint/no-explicit-any */
    ctx.featuresSource = map.getSource('features') as any
    /* eslint-disable-next-line @typescript-eslint/no-explicit-any */
    ctx.endpointsSource = map.getSource('endpoints') as any

    debugLog('[initSources] ctx.featuresSource set:', !!ctx.featuresSource)

    addFeatureLayers(map)
    addDrawingPreviewLayer(map)
    addEndpointLayers(map)
}

function addEndpointLayers(map: maplibregl.Map): void {
    // Note: no beforeId — layers added at end (top z-order)

    // Road start marker (circle)
    map.addLayer({
        id: 'nars-endpoint-start',
        type: 'circle',
        source: 'endpoints',
        filter: ['==', ['get', 'endpointType'], 'start'],
        paint: {
            'circle-color': ['get', 'color'],
            'circle-radius': 12,
            'circle-stroke-color': '#000000',
            'circle-stroke-width': 3,
        },
    })

    // Road start label (arrow character)
    map.addLayer({
        id: 'nars-endpoint-start-label',
        type: 'symbol',
        source: 'endpoints',
        filter: ['==', ['get', 'endpointType'], 'start'],
        layout: {
            'text-field': '>',
            'text-size': 20,
            'text-font': ['Open Sans Bold', 'Arial Unicode MS Bold'],
            'text-allow-overlap': true,
            'text-optional': true,
            'text-rotate': ['get', 'angle'],
            'text-rotation-alignment': 'viewport',
        },
        paint: {
            'text-color': '#ffffff',
            'text-halo-color': '#000000',
            'text-halo-width': 2,
        },
    })

    // Road end marker (circle)
    map.addLayer({
        id: 'nars-endpoint-end',
        type: 'circle',
        source: 'endpoints',
        filter: ['==', ['get', 'endpointType'], 'end'],
        paint: {
            'circle-color': ['get', 'color'],
            'circle-radius': 12,
            'circle-stroke-color': '#000000',
            'circle-stroke-width': 3,
        },
    })

    // Road end label (X character)
    map.addLayer({
        id: 'nars-endpoint-end-label',
        type: 'symbol',
        source: 'endpoints',
        filter: ['==', ['get', 'endpointType'], 'end'],
        layout: {
            'text-field': '✕',
            'text-size': 20,
            'text-font': ['Open Sans Bold', 'Arial Unicode MS Bold'],
            'text-allow-overlap': true,
            'text-optional': true,
            'text-rotate': ['get', 'angle'],
            'text-rotation-alignment': 'viewport',
        },
        paint: {
            'text-color': '#ffffff',
            'text-halo-color': '#000000',
            'text-halo-width': 2,
        },
    })
}

function addFeatureLayers(map: maplibregl.Map): void {
    const layers = map.getStyle().layers || []
    const firstSymbolId = layers.find((l) => l.type === 'symbol')?.id

    // Selection highlight — dashed yellow outline around selected feature
    map.addLayer(
        {
            id: 'nars-selection',
            type: 'line',
            source: 'selection',
            paint: { 'line-color': '#f1c40f', 'line-width': 4, 'line-dasharray': [6, 3], 'line-opacity': 0.9 },
        },
        firstSymbolId,
    )

    // Commune boundary outline
    map.addLayer(
        {
            id: 'nars-boundaries',
            type: 'line',
            source: 'boundaries',
            paint: { 'line-color': '#e74c3c', 'line-width': 2.5, 'line-opacity': 0.8 },
        },
        firstSymbolId,
    )

    addBoundaryClickEvents(map)

    // Polygon fill
    map.addLayer(
        {
            id: 'nars-polygon-fill',
            type: 'fill',
            source: 'features',
            filter: ['==', ['get', 'geomType'], 'Polygon'],
            paint: { 'fill-color': ['get', 'fillColor'], 'fill-opacity': ['get', 'fillOpacity'] },
        },
        firstSymbolId,
    )

    // Polygon stroke
    map.addLayer(
        {
            id: 'nars-polygon-stroke',
            type: 'line',
            source: 'features',
            filter: ['==', ['get', 'geomType'], 'Polygon'],
            paint: { 'line-color': ['get', 'lineColor'], 'line-width': ['get', 'lineWidth'] },
        },
        firstSymbolId,
    )

    // Polygon label
    map.addLayer(
        {
            id: 'nars-polygon-label',
            type: 'symbol',
            source: 'features',
            filter: ['==', ['get', 'geomType'], 'Polygon'],
            layout: {
                'text-field': ['get', 'label'],
                'text-size': 12,
                'text-anchor': 'center',
                'text-allow-overlap': false,
                'text-optional': true,
            },
            paint: {
                'text-color': ['get', 'lineColor'],
                'text-halo-color': '#ffffff',
                'text-halo-width': 2,
            },
        },
        firstSymbolId,
    )

    // Line (roads)
    map.addLayer(
        {
            id: 'nars-line',
            type: 'line',
            source: 'features',
            filter: ['==', ['get', 'geomType'], 'LineString'],
            paint: { 'line-color': ['get', 'lineColor'], 'line-width': ['get', 'lineWidth'] },
        },
        firstSymbolId,
    )

    // Line label (road names) — placed AFTER line so text renders on top
    map.addLayer(
        {
            id: 'nars-line-label',
            type: 'symbol',
            source: 'features',
            filter: ['==', ['get', 'geomType'], 'LineString'],
            layout: {
                'text-field': ['get', 'label'],
                'text-size': 11,
                'symbol-placement': 'line',
                'text-rotation-alignment': 'map',
                'text-font': ['Open Sans Bold', 'Open Sans Regular', 'Arial Unicode MS Regular'],
                'text-allow-overlap': false,
                'text-optional': true,
            },
            paint: {
                'text-color': ['get', 'lineColor'],
                'text-halo-color': '#ffffff',
                'text-halo-width': 3,
            },
        },
        firstSymbolId,
    )

    // Point (markers)
    map.addLayer(
        {
            id: 'nars-point',
            type: 'circle',
            source: 'features',
            filter: ['==', ['get', 'geomType'], 'Point'],
            paint: {
                'circle-color': ['get', 'circleColor'],
                'circle-radius': ['get', 'circleRadius'],
                'circle-stroke-color': '#ffffff',
                'circle-stroke-width': 2,
            },
        },
        firstSymbolId,
    )

    // Point label
    map.addLayer(
        {
            id: 'nars-point-label',
            type: 'symbol',
            source: 'features',
            filter: ['==', ['get', 'geomType'], 'Point'],
            layout: {
                'text-field': ['get', 'label'],
                'text-size': 10,
                'text-font': ['Open Sans Regular', 'Arial Unicode MS Regular'],
                'text-anchor': 'center',
                'text-allow-overlap': false,
                'text-optional': true,
            },
            paint: {
                'text-color': ['get', 'textColor'],
                'text-halo-color': '#ffffff',
                'text-halo-width': 1,
            },
        },
        firstSymbolId,
    )
}

function addDrawingPreviewLayer(map: maplibregl.Map): void {
    const layers = map.getStyle().layers || []
    const firstSymbolId = layers.find((l) => l.type === 'symbol')?.id
    map.addLayer(
        {
            id: 'drawing-preview-line',
            type: 'line',
            source: 'drawing-preview',
            paint: { 'line-color': '#3498db', 'line-width': 3, 'line-dasharray': [3, 2], 'line-opacity': 0.8 },
        },
        firstSymbolId,
    )
}

export function updateDrawingPreview(geometry: [number, number][] | null): void {
    /* eslint-disable-next-line @typescript-eslint/no-explicit-any */
    const source = ctx.map.getSource('drawing-preview') as any
    if (!source) return
    const features: GeoJSON.Feature[] =
        geometry && geometry.length > 0
            ? [
                  geometry.length >= 3
                      ? {
                            type: 'Feature',
                            geometry: { type: 'Polygon', coordinates: [[...geometry, geometry[0]]] },
                            properties: {},
                        }
                      : { type: 'Feature', geometry: { type: 'LineString', coordinates: geometry }, properties: {} },
              ]
            : []
    source.setData({ type: 'FeatureCollection', features })
}

// ─── BOUNDARY CLICK EVENTS ────────────────────────────────────────────────────

// Track whether boundary event handlers have been registered to prevent
// duplicate handlers after setStyle() rebuilds sources and layers.
let boundaryEventsRegistered = false

function addBoundaryClickEvents(map: maplibregl.Map): void {
    if (boundaryEventsRegistered) return
    boundaryEventsRegistered = true

    map.on('click', 'nars-boundaries', (e: maplibregl.MapLayerMouseEvent) => {
        const name = sanitizeText(e.features?.[0]?.properties?.communeName || 'Commune')
        new maplibregl.Popup({ closeButton: true, closeOnClick: true })
            .setLngLat(e.lngLat)
            .setHTML(`<strong>${name}</strong><br><small>Commune Boundary</small>`)
            .addTo(map)
    })
    map.on('mouseenter', 'nars-boundaries', () => {
        map.getCanvas().style.setProperty('cursor', 'pointer', 'important')
    })
    map.on('mouseleave', 'nars-boundaries', () => {
        map.getCanvas().style.removeProperty('cursor')
    })

    map.on('contextmenu', 'nars-boundaries', (e: maplibregl.MapLayerMouseEvent) => {
        e.preventDefault()
        e.originalEvent?.preventDefault()
        showBoundaryContextMenu(e.point.x, e.point.y, e.features?.[0]?.properties?.communeName || 'Commune')
    })
}

function showBoundaryContextMenu(x: number, y: number, communeName: string): void {
    document.getElementById('nars-boundary-ctx-menu')?.remove()

    const menu = document.createElement('div')
    menu.id = 'nars-boundary-ctx-menu'
    menu.className = 'nars-ctx-menu'
    menu.innerHTML = sanitizeHtml(`
        <div class="nars-ctx-item" style="font-weight:bold;color:#666;cursor:default;">${sanitizeText(communeName)}</div>
        <div style="border-top:1px solid #eee;margin:4px 0;"></div>
        <div class="nars-ctx-item" data-action="copy-name">📋 Copy Name</div>
    `)
    document.body.appendChild(menu)

    menu.style.left = (x + 180 > window.innerWidth ? x - 180 : x) + 'px'
    menu.style.top = (y + 100 > window.innerHeight ? y - 100 : y) + 'px'

    const hide = () => {
        menu.remove()
        document.removeEventListener('click', hide)
    }
    setTimeout(() => document.addEventListener('click', hide), 100)

    menu.querySelectorAll('.nars-ctx-item[data-action]').forEach((item) => {
        ;(item as HTMLElement).onclick = (e) => {
            e.stopPropagation()
            if ((item as HTMLElement).dataset.action === 'copy-name') {
                navigator.clipboard.writeText(communeName)
                showToast(`Copied: ${communeName}`, 'success')
            }
            hide()
        }
    })
}
