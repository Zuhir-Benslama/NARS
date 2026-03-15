// ─── PM:CREATE HANDLER ────────────────────────────────────────────────────────
// Handles the Geoman pm:create event: validates placement, opens the feature
// modal, runs phase-specific business rules, saves to the database, and
// registers the layer in featureLayers.
//
// Also exports:
//   bindHoverPopup   — attaches hover popup to any Leaflet layer (used by
//                      draw-events.ts pm:edit and loader.ts)
//   getDistrictLabel — resolves display label for district features (used by
//                      draw-events.ts pm:edit and loader.ts)

import { t }                                                      from '../i18n'
import { PHASES, DISTRICT_TYPES }                                 from '../phases'
import { store, featureLayers, openModal, syncCounts }            from '../store'
import { validateRoad, validateDistrict, getRoadSide }            from '../validation'
import type { FeatureData, LayerEntry }                           from '../types'
import { ctx }                                                    from './state'
import { createEntranceIcon, applyStyle, buildPopup }             from './styles'
import { createPermanentLabel, createAreaPerimeterLabel,
         createPolygonEdgeLabel }                                  from './labels'
import { pointInMunicipalLimit, pointInScatteredArea,
         polylineMidpoint, refreshScatteredAreas }                 from './geometry'
import { buildFeatureData, saveToDatabase, prepareModalExtras }   from './features'
import { bindContextMenu }                                        from './context-menu'

declare const L: typeof import('leaflet')

// ─── SHARED HELPERS ───────────────────────────────────────────────────────────

// Opens the feature info popup on hover instead of click.
export function bindHoverPopup(layer: L.Layer, content: string): void {
    if (layer instanceof L.Marker) {
        const popup = L.popup({ offset: L.point(0, -10), closeButton: false }).setContent(content)
        layer.bindPopup(popup)
        layer.on('mouseover', () => layer.openPopup())
        layer.on('mouseout',  () => layer.closePopup())
    } else {
        const path = layer as L.Path
        const popup = L.popup({ closeButton: false }).setContent(content)
        path.bindPopup(popup)
        layer.on('mouseover', (e: any) => path.openPopup(e.latlng))
        layer.on('mouseout',  () => path.closePopup())
    }
}

// Returns the label to display for a district. Trade Activity Zones and
// Industry Zones show their type name when no custom label is provided.
export function getDistrictLabel(districtTypeKey: string, customLabel: string): string {
    if (customLabel) return customLabel
    if (districtTypeKey === 'trad_activities_zone' || districtTypeKey === 'industry_zone') {
        const dtype = DISTRICT_TYPES.find(d => d.key === districtTypeKey)
        return dtype?.label ?? ''
    }
    return customLabel
}

// ─── PRIVATE HELPERS ──────────────────────────────────────────────────────────

// Validates that a newly drawn layer falls inside the municipal boundary and,
// for most phase types, outside any scattered area.
async function validatePlacement(layer: L.Layer, phase: typeof PHASES[number]): Promise<boolean> {
    let checkPoint: L.LatLng
    if (phase.drawType === 'marker' || phase.drawType === 'circle') {
        checkPoint = (layer as any).getLatLng()
    } else if (phase.drawType === 'polyline') {
        checkPoint = polylineMidpoint(layer as L.Polyline)
    } else {
        const lls = (layer as L.Polygon).getLatLngs()[0] as L.LatLng[]
        const lat = lls.reduce((s, ll) => s + ll.lat, 0) / lls.length
        const lng = lls.reduce((s, ll) => s + ll.lng, 0) / lls.length
        checkPoint = L.latLng(lat, lng)
    }

    if (!pointInMunicipalLimit(checkPoint)) {
        alert(t('alert_outside_boundary', { feature: t(phase.label).replace(/s$/, '').toLowerCase() }))
        return false
    }
    if (
        phase.key !== 'publicBuildings' &&
        phase.key !== 'areas' &&
        phase.key !== 'cityCenter' &&
        phase.key !== 'districts'
    ) {
        if (pointInScatteredArea(checkPoint)) {
            alert(t('alert_in_scattered', { feature: t(phase.label).replace(/s$/, '').toLowerCase() }))
            return false
        }
    }
    return true
}

// Removes a layer that Geoman already added to the map before validation ran.
function discardCreatedLayer(layer: L.Layer): void {
    ctx.map.removeLayer(layer)
    if (ctx.drawnItems.hasLayer(layer))        ctx.drawnItems.removeLayer(layer)
    if (ctx.roadsDisplayLayer.hasLayer(layer)) ctx.roadsDisplayLayer.removeLayer(layer)
}

// ─── PM:CREATE HANDLER ────────────────────────────────────────────────────────

export async function handlePmCreate(event: any): Promise<void> {
    const layer = event.layer as L.Layer
    const phase = PHASES[store.currentPhase]

    if (!await validatePlacement(layer, phase)) { discardCreatedLayer(layer); return }

    // Districts: open modal first to get the type, then validate.
    let modalResult: any = null
    if (phase.key === 'districts') {
        await prepareModalExtras(phase, layer)
        modalResult = await openModal(store.currentPhase, layer)
        if (!modalResult) { discardCreatedLayer(layer); return }

        const districtTypeKey = modalResult.districtTypeKey as string
        const check = await validateDistrict(layer as L.Polygon, districtTypeKey)
        if (!check.valid) {
            discardCreatedLayer(layer)
            alert(t('alert_district_save_failed', { error: check.error ?? 'Validation failed' }))
            return
        }
    } else {
        if (phase.key === 'roads') {
            const check = await validateRoad(layer as L.Polyline)
            if (!check.valid) {
                discardCreatedLayer(layer)
                alert(t('alert_road_save_failed', { error: check.error ?? 'Validation failed' }))
                return
            }
        }
    }

    // House entrances: no modal — type and number derived from active reference.
    if (phase.key === 'houseEntrances') {
        const ll = (layer as L.Marker).getLatLng()

        if (store.referenceEntranceDbId != null) {
            // ── Secondary entrance — BIS number auto-assigned at placement ──
            const mainEntry = (featureLayers.houseEntrances as LayerEntry[])
                .find(e => (e.layer as any)._dbId === store.referenceEntranceDbId)
            if (!mainEntry) { discardCreatedLayer(layer); alert(t('alert_ref_entrance_not_found')); return }

            const bisCount  = (featureLayers.houseEntrances as LayerEntry[])
                .filter(e =>
                    e.data.entranceTypeKey === 'secondary_entrance' &&
                    e.data.mainEntranceDbId === store.referenceEntranceDbId
                ).length
            const bisNumber = bisCount + 1
            const label     = 'BIS' + String(bisNumber).padStart(2, '0')
            modalResult = {
                entranceTypeKey:   'secondary_entrance',
                mainEntranceDbId:  store.referenceEntranceDbId,
                mainEntranceLabel: mainEntry.data.label,
                bisNumber,
                label,
                decisionNumber: '',
                decisionDate:   '',
            }

        } else if (store.referenceRoadDbId != null) {
            // ── Main entrance — number assigned later via "Set House Numbers" ──
            const roadEntry = (featureLayers.roads as LayerEntry[])
                .find(r => (r.layer as any)._dbId === store.referenceRoadDbId)
            if (!roadEntry) { discardCreatedLayer(layer); alert(t('alert_ref_road_not_found')); return }

            const sideResult = await getRoadSide(store.referenceRoadDbId, ll.lat, ll.lng)
            const side = sideResult?.side ?? 'left'
            modalResult = {
                entranceTypeKey: 'main_entrance',
                roadDbId:        store.referenceRoadDbId,
                roadLabel:       roadEntry.data.label,
                side,
                entranceNumber:  undefined,
                label:           '?',
                decisionNumber:  '',
                decisionDate:    '',
            }
        } else {
            discardCreatedLayer(layer)
            alert(t('alert_no_reference_set'))
            return
        }

    } else if (phase.key !== 'districts') {
        // All other phases: open modal after validation.
        await prepareModalExtras(phase, layer)
        modalResult = await openModal(store.currentPhase, layer)
        if (!modalResult) { discardCreatedLayer(layer); return }
    }

    // ── Area count rules ──────────────────────────────────────────────────────
    if (phase.key === 'areas') {
        const areaTypeKey    = (modalResult as any).areaTypeKey as string
        const mainCount      = featureLayers.areas.filter((e: LayerEntry) => e.data.areaTypeKey === 'central_urban').length
        const secondaryCount = featureLayers.areas.filter((e: LayerEntry) => e.data.areaTypeKey === 'secondary_urban').length

        if (areaTypeKey === 'central_urban' && mainCount >= 1) {
            discardCreatedLayer(layer); alert(t('alert_only_one_main_urban_area')); return
        }
        if (areaTypeKey === 'secondary_urban' && secondaryCount >= 10) {
            discardCreatedLayer(layer); alert(t('alert_max_10_secondary_urban_areas')); return
        }
    }

    // ── City center: one per urban area ───────────────────────────────────────
    if (phase.key === 'cityCenter') {
        const markerLL  = (layer as L.Circle).getLatLng()
        const pointInRing = (ring: L.LatLng[], x: number, y: number): boolean => {
            let inside = false
            for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
                const xi = ring[i].lat, yi = ring[i].lng
                const xj = ring[j].lat, yj = ring[j].lng
                if (((yi > y) !== (yj > y)) && (x < (xj - xi) * (y - yi) / (yj - yi) + xi))
                    inside = !inside
            }
            return inside
        }

        const parentArea = featureLayers.areas.find((e: LayerEntry) => {
            if (!(e.layer instanceof L.Polygon)) return false
            const ring = (e.layer.getLatLngs()[0] as L.LatLng[])
            return pointInRing(ring, markerLL.lat, markerLL.lng)
        })

        if (!parentArea) {
            discardCreatedLayer(layer); alert(t('alert_cc_must_be_in_urban_area')); return
        }

        const parentRing = (parentArea.layer as L.Polygon).getLatLngs()[0] as L.LatLng[]
        const duplicate  = featureLayers.cityCenter.some((e: LayerEntry) => {
            if (!(e.layer instanceof L.Circle)) return false
            const ll = (e.layer as L.Circle).getLatLng()
            return pointInRing(parentRing, ll.lat, ll.lng)
        })

        if (duplicate) {
            discardCreatedLayer(layer)
            alert(t('alert_cc_already_exists', { areaLabel: parentArea.data.label || 'this urban area' }))
            return
        }
    }

    // ── District scattered-area check (type-aware) ────────────────────────────
    if (phase.key === 'districts') {
        const districtTypeKey = (modalResult as any).districtTypeKey as string
        const dtype = DISTRICT_TYPES.find(d => d.key === districtTypeKey)
        if (!dtype?.allowInScattered) {
            const lls = (layer as L.Polygon).getLatLngs()[0] as L.LatLng[]
            const lat = lls.reduce((s: number, ll: L.LatLng) => s + ll.lat, 0) / lls.length
            const lng = lls.reduce((s: number, ll: L.LatLng) => s + ll.lng, 0) / lls.length
            if (pointInScatteredArea(L.latLng(lat, lng))) {
                discardCreatedLayer(layer); alert(t('alert_district_type_in_scattered')); return
            }
        }
    }

    applyStyle(layer, phase, modalResult as unknown as FeatureData)

    const featureData = buildFeatureData(layer, phase, modalResult as unknown as Record<string, unknown>)
    const saveResult  = await saveToDatabase(featureData)
    if (!saveResult.ok) {
        discardCreatedLayer(layer)
        alert(t('alert_feature_save_failed', { error: saveResult.error ?? 'Please try again.' }))
        return
    }

    ;(layer as any)._dbId = saveResult.data!.id
    // Roads live ONLY in roadsDisplayLayer — never in drawnItems.
    if (phase.key === 'roads') ctx.roadsDisplayLayer.addLayer(layer)
    else                       ctx.drawnItems.addLayer(layer)

    bindContextMenu(layer, saveResult.data!.id, phase.key)
    createPermanentLabel(layer, modalResult.label as string, phase.key)
    if (phase.key === 'areas')     createAreaPerimeterLabel(layer, (modalResult as any).areaTypeKey as string)
    if (phase.key === 'districts') createPolygonEdgeLabel(layer, getDistrictLabel((modalResult as any).districtTypeKey as string, modalResult.label as string), '#f39c12')
    bindHoverPopup(layer, buildPopup(featureData, phase, saveResult.data!.id))

    featureLayers[phase.key].push({ layer, data: featureData })

    if (phase.key === 'cityCenter') {
        const ll = (layer as L.Circle).getLatLng()
        store.cityCenterMode   = 'city_center'
        store.cityCenterLatLng = { lat: ll.lat, lng: ll.lng }
        setTimeout(() => (ctx.map as any).pm.enableDraw('Circle', { snappable: false }), 0)
    }

    if (phase.drawType === 'marker')
        setTimeout(() => (ctx.map as any).pm.enableDraw('Marker', { snappable: false }), 0)

    if (phase.key === 'areas') await refreshScatteredAreas()

    if (phase.drawType === 'polygon')
        setTimeout(() => (ctx.map as any).pm.enableDraw('Polygon', { snappable: false }), 0)
    else if (phase.drawType === 'polyline')
        setTimeout(() => (ctx.map as any).pm.enableDraw('Line', { snappable: false }), 0)

    syncCounts()
}
