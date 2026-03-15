// ─── DRAW EVENTS ──────────────────────────────────────────────────────────────
// Wires all Geoman (pm:*) and Leaflet map events: draw start/end, edit
// start/end, vertex added, per-feature edit persistence, feature removal,
// left-click draw restart, ESC cancel, and right-click context menu.
// Extracted from index.ts for size.

import { apiFetch }                                               from '../api'
import { PHASES }                                                 from '../phases'
import { store, featureLayers, syncCounts }                       from '../store'
import type { LayerEntry }                                        from '../types'
import { ctx }                                                    from './state'
import { buildPopup }                                             from './styles'
import { addPolylineEndpoints, createAreaPerimeterLabel,
         createPolygonEdgeLabel, refreshLayerVisibility }         from './labels'
import { refreshScatteredAreas }                                  from './geometry'
import { enableSnapping, disableSnapping,
         hookEditHandles, hookAllEditMarkers, editModeActive }    from './snapping'
import { showMapContextMenu }                                     from './context-menu'
import { handlePmCreate, bindHoverPopup, getDistrictLabel }       from './create-handler'

declare const L: typeof import('leaflet')

// ─── REGISTER ALL MAP / GEOMAN EVENTS ────────────────────────────────────────

export function registerDrawEvents(): void {
    let drawModeActive = false

    // ── Draw mode tracking ────────────────────────────────────────────────────

    ctx.map.on('pm:drawstart', (e: any) => {
        drawModeActive = true
        const key = PHASES[store.currentPhase]?.key
        if      (key === 'areas')     enableSnapping('districts', undefined, 'areas')
        else if (key === 'districts') enableSnapping('districts', undefined, 'districts')
        else if (key === 'roads')     enableSnapping('roads',     undefined, 'roads')
    })

    ctx.map.on('pm:drawend', () => {
        drawModeActive = false
        if (!editModeActive) disableSnapping()
    })

    // ── ESC — cancel draw or edit ─────────────────────────────────────────────

    document.addEventListener('keydown', (e: KeyboardEvent) => {
        if (e.key !== 'Escape') return
        if (drawModeActive) (ctx.map as any).pm.disableDraw()
        if (editModeActive) (ctx.map as any).pm.disableGlobalEditMode()
    })

    // ── Left-click — restart draw mode if idle ────────────────────────────────

    ctx.map.on('click', () => {
        if (drawModeActive || editModeActive) return
        const phase = PHASES[store.currentPhase]
        if (!phase || phase.key === 'namingPanels') return
        if      (phase.drawType === 'polygon')  (ctx.map as any).pm.enableDraw('Polygon', { snappable: false })
        else if (phase.drawType === 'polyline') (ctx.map as any).pm.enableDraw('Line',    { snappable: false })
        else if (phase.drawType === 'marker')   (ctx.map as any).pm.enableDraw('Marker',  { snappable: false })
    })

    // ── Zoom — re-apply layer visibility (Leaflet re-renders SVG on zoom) ─────

    ctx.map.on('zoomend', () => refreshLayerVisibility())

    // ── Right-click — cancel draw/edit, or open context menu ─────────────────

    ctx.map.on('contextmenu', (e: any) => {
        e.originalEvent.preventDefault()
        e.originalEvent.stopPropagation()
        if (drawModeActive) { ;(ctx.map as any).pm.disableDraw(); return }
        if (editModeActive) { ;(ctx.map as any).pm.disableGlobalEditMode(); return }
        // Leaflet always re-fires contextmenu to the map even after a layer handled it.
        if ((ctx.map as any)._narsFeatureCtxHandled) {
            ;(ctx.map as any)._narsFeatureCtxHandled = false
            return
        }
        const phase = PHASES[store.currentPhase]
        if (!phase) return
        showMapContextMenu(e.originalEvent.clientX, e.originalEvent.clientY, phase)
    })

    // ── pm:editstart — park non-current layers, enable snapping ──────────────

    ctx.map.on('pm:editstart', (e: any) => {
        try {
            const key = PHASES[store.currentPhase]?.key
            if (drawModeActive) (ctx.map as any).pm.disableDraw()

            const parked:  L.Layer[] = []
            const display: L.Layer[] = []

            if (!(ctx as any)._displayLayer) {
                (ctx as any)._displayLayer = L.layerGroup().addTo(ctx.map)
            }
            const displayLayer: L.LayerGroup = (ctx as any)._displayLayer

            Object.entries(featureLayers).forEach(([phaseKey, entries]) => {
                if (phaseKey === key) return
                ;(entries as LayerEntry[]).forEach(({ layer }) => {
                    if (phaseKey === 'roads') return
                    if (!ctx.drawnItems.hasLayer(layer)) return
                    ctx.drawnItems.removeLayer(layer)
                    if (phaseKey === 'areas') {
                        displayLayer.addLayer(layer); display.push(layer)
                    } else {
                        parked.push(layer)
                    }
                })
            })
            ;(ctx as any)._parkedLayers  = parked
            ;(ctx as any)._displayLayers = display

            if      (key === 'districts' || key === 'areas') enableSnapping('districts', undefined, key)
            else if (key === 'roads')                        enableSnapping('roads',     undefined, key)
            hookEditHandles()
        } catch (err) { console.error('pm:editstart error:', err) }
    })

    // ── pm:vertexadded — refresh edit handles after midpoint insertion ────────

    let editVertexTimeout: ReturnType<typeof setTimeout> | null = null
    ctx.map.on('pm:vertexadded', () => {
        if (editVertexTimeout) clearTimeout(editVertexTimeout)
        editVertexTimeout = setTimeout(() => hookAllEditMarkers(), 150)
    })

    // ── pm:editend — restore parked layers, re-enable draw, belt-and-suspenders save

    ctx.map.on('pm:editend', () => {
        const parked:        L.Layer[]             = (ctx as any)._parkedLayers  ?? []
        const display:       L.Layer[]             = (ctx as any)._displayLayers ?? []
        const displayLayer:  L.LayerGroup | undefined = (ctx as any)._displayLayer

        display.forEach(layer => { displayLayer?.removeLayer(layer); ctx.drawnItems.addLayer(layer) })
        parked.forEach(layer => ctx.drawnItems.addLayer(layer))
        ;(ctx as any)._parkedLayers  = []
        ;(ctx as any)._displayLayers = []
        disableSnapping()

        const phaseAfterEdit = PHASES[store.currentPhase]
        if      (phaseAfterEdit?.drawType === 'polygon')  setTimeout(() => (ctx.map as any).pm.enableDraw('Polygon', { snappable: false }), 50)
        else if (phaseAfterEdit?.drawType === 'polyline') setTimeout(() => (ctx.map as any).pm.enableDraw('Line',    { snappable: false }), 50)
        else if (phaseAfterEdit?.drawType === 'marker')   setTimeout(() => (ctx.map as any).pm.enableDraw('Marker',  { snappable: false }), 50)

        // Belt-and-suspenders save — catches any geometry changes pm:edit missed
        // (snap edge-cases, version differences). Runs 30 ms after editend to
        // let any pending snap redraw settle before reading coordinates.
        const currentKey = PHASES[store.currentPhase]?.key
        if (currentKey) {
            setTimeout(async () => {
                for (const entry of (featureLayers[currentKey] ?? []) as LayerEntry[]) {
                    const dbId = (entry.layer as any)._dbId
                    if (!dbId) continue
                    try {
                        const updatedData = { ...entry.data }
                        if (entry.layer instanceof L.Marker) {
                            const ll = (entry.layer as L.Marker).getLatLng()
                            updatedData.lat = ll.lat; updatedData.lng = ll.lng
                            entry.data.lat  = ll.lat; entry.data.lng  = ll.lng
                        } else if (entry.layer instanceof L.Polygon) {
                            let coords = ((entry.layer as L.Polygon).getLatLngs()[0] as L.LatLng[])
                                .map(ll => ({ lat: ll.lat, lng: ll.lng }))
                            if (coords.length >= 3) {
                                const f = coords[0], l = coords[coords.length - 1]
                                if (f.lat !== l.lat || f.lng !== l.lng)
                                    coords = [...coords, { lat: f.lat, lng: f.lng }]
                            }
                            updatedData.coordinates = coords; entry.data.coordinates = coords
                        } else if (entry.layer instanceof L.Polyline) {
                            const coords = ((entry.layer as L.Polyline).getLatLngs() as L.LatLng[])
                                .map(ll => ({ lat: ll.lat, lng: ll.lng }))
                            updatedData.coordinates = coords; entry.data.coordinates = coords
                        }
                        await apiFetch(`/api/update/${dbId}`, {
                            method: 'PUT', headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ data: updatedData }),
                        })
                    } catch (err) { console.error('editend save error for', dbId, err) }
                }
            }, 30)
        }

        setTimeout(refreshLayerVisibility, 0)
    })

    // ── pm:create — delegate to create-handler ────────────────────────────────

    ctx.map.on('pm:create', async (event: any) => {
        await handlePmCreate(event)
    })

    // ── pm:edit — persist geometry change for a single edited layer ───────────

    ctx.map.on('pm:edit', async (event: any) => {
        // Defer one tick so any pending snap commits (setTimeout 0) run first.
        await new Promise(r => setTimeout(r, 0))
        const layer = event.layer as L.Layer
        if (!(layer as any)._dbId) return
        try {
            const phase = PHASES.find(p => featureLayers[p.key].some((f: LayerEntry) => f.layer === layer))
            const entry = phase ? featureLayers[phase.key].find((f: LayerEntry) => f.layer === layer) : null
            if (!entry) return

            if (layer instanceof L.Marker) {
                const ll = layer.getLatLng()
                entry.data.lat = ll.lat; entry.data.lng = ll.lng
            } else if (layer instanceof L.Polyline && !(layer instanceof L.Polygon)) {
                entry.data.coordinates = (layer.getLatLngs() as L.LatLng[])
                    .map(ll => ({ lat: ll.lat, lng: ll.lng }))
            } else if (layer instanceof L.Polygon) {
                let coords = (layer.getLatLngs()[0] as L.LatLng[]).map(ll => ({ lat: ll.lat, lng: ll.lng }))
                if (coords.length >= 3) {
                    const first = coords[0], last = coords[coords.length - 1]
                    if (first.lat !== last.lat || first.lng !== last.lng)
                        coords = [...coords, { lat: first.lat, lng: first.lng }]
                }
                entry.data.coordinates = coords
            }

            await apiFetch(`/api/update/${(layer as any)._dbId}`, {
                method: 'PUT', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ data: entry.data }),
            })

            if (phase) bindHoverPopup(layer, buildPopup(entry.data, phase, (layer as any)._dbId))
            if (phase?.key === 'areas') {
                createAreaPerimeterLabel(layer, entry.data.areaTypeKey ?? 'central_urban')
                await refreshScatteredAreas()
            }
            if (phase?.key === 'districts')
                createPolygonEdgeLabel(layer, getDistrictLabel(entry.data.districtTypeKey ?? 'district', entry.data.label), '#f39c12')
        } catch (err) { console.error('Edit persist error:', err) }

        ctx.lineEndpointLayer.clearLayers()
        ctx.drawnItems.eachLayer(l => {
            if (l instanceof L.Polyline && !(l instanceof L.Polygon)) addPolylineEndpoints(l)
        })
    })

    // ── pm:remove — delete from DB and clean up all associated markers ────────

    ctx.map.on('pm:remove', async (event: any) => {
        const layer = event.layer as L.Layer
        let areaDeleted = false

        if ((layer as any)._dbId) {
            try {
                const res = await apiFetch(`/api/delete/${(layer as any)._dbId}`, { method: 'DELETE' })
                if (!res.ok) console.error(`Delete failed: ${(layer as any)._dbId}`, res.status)
                if (featureLayers.areas.some((f: LayerEntry) => f.layer === layer)) areaDeleted = true
            } catch (err) { console.error('Delete error:', err) }
        }

        if ((layer as any)._endpointMarkers)
            (layer as any)._endpointMarkers.forEach((m: L.Layer) => ctx.lineEndpointLayer.removeLayer(m))
        if ((layer as any)._perimeterLabel)
            ctx.perimeterLabelLayer.removeLayer((layer as any)._perimeterLabel)
        if ((layer as any)._edgeLabelMarkers)
            (layer as any)._edgeLabelMarkers.forEach((m: L.Marker) => ctx.polygonEdgeLabelLayer.removeLayer(m))

        ctx.lineEndpointLayer.clearLayers()
        ctx.drawnItems.eachLayer(l => {
            if (l instanceof L.Polyline && !(l instanceof L.Polygon)) addPolylineEndpoints(l)
        })

        for (const key of Object.keys(featureLayers))
            featureLayers[key] = featureLayers[key].filter((f: LayerEntry) =>
                ctx.drawnItems.hasLayer(f.layer) || ctx.roadsDisplayLayer.hasLayer(f.layer))

        if (areaDeleted) await refreshScatteredAreas()
        syncCounts()
    })
}
