// ─── HOUSE ENTRANCE REFERENCE HELPERS ────────────────────────────────────────
// Manages the reference road / reference entrance selection used when placing
// house entrance markers in Phase 4, and implements the Set House Numbers
// algorithm (project markers onto road, sort by arc-length, assign odd/even).
// Extracted from context-menu.ts for size.

import { apiFetch }                    from '../api'
import { PHASES }                      from '../phases'
import { store, featureLayers, syncCounts } from '../store'
import type { LayerEntry }             from '../types'
import { ctx, POLYLINE_WEIGHT }        from './state'
import { createEntranceIcon }          from './styles'
import { t }                           from '../i18n'

declare const L: typeof import('leaflet')

// ─── HIGHLIGHT ────────────────────────────────────────────────────────────────

function highlightLayer(dbId: number, phaseKey: string, active: boolean): void {
    const entry = (featureLayers[phaseKey] as LayerEntry[])
        ?.find(e => (e.layer as any)._dbId === dbId)
    if (!entry) return
    if (entry.layer instanceof L.Polyline && !(entry.layer instanceof L.Polygon)) {
        entry.layer.setStyle({ color: active ? '#f39c12' : '#3498db', weight: active ? 5 : POLYLINE_WEIGHT })
    } else if (entry.layer instanceof L.Marker) {
        const el = (entry.layer as any).getElement?.() as HTMLElement | undefined
        el?.classList.toggle('nars-reference', active)
    }
}

// ─── REFERENCE ROAD ───────────────────────────────────────────────────────────

export function setReferenceRoad(dbId: number): void {
    if (store.referenceRoadDbId != null)
        highlightLayer(store.referenceRoadDbId, 'roads', false)
    store.referenceRoadDbId = dbId
    highlightLayer(dbId, 'roads', true)
}

export function clearReferenceRoad(): void {
    if (store.referenceRoadDbId != null) {
        highlightLayer(store.referenceRoadDbId, 'roads', false)
        store.referenceRoadDbId = null
    }
}

// ─── REFERENCE ENTRANCE ───────────────────────────────────────────────────────

export function setReferenceEntrance(dbId: number): void {
    if (store.referenceEntranceDbId != null)
        highlightLayer(store.referenceEntranceDbId, 'houseEntrances', false)
    store.referenceEntranceDbId = dbId
    highlightLayer(dbId, 'houseEntrances', true)
}

// ─── SET HOUSE NUMBERS ────────────────────────────────────────────────────────
// Projects unassigned entrance markers onto the reference road polyline, sorts
// them by arc-length, then assigns odd numbers to the left side and even to
// the right — each counter continuing from the highest already-assigned number.

export async function setHouseNumbers(): Promise<void> {
    if (store.referenceRoadDbId == null) {
        alert(t('alert_no_ref_road')); return
    }

    const roadEntry = (featureLayers.roads as LayerEntry[])
        .find(r => (r.layer as any)._dbId === store.referenceRoadDbId)
    if (!roadEntry?.data.coordinates?.length) {
        alert(t('alert_ref_road_no_coords')); return
    }

    const unassigned = (featureLayers.houseEntrances as LayerEntry[]).filter(e =>
        e.data.entranceTypeKey === 'main_entrance' &&
        e.data.roadDbId        === store.referenceRoadDbId &&
        e.data.label           === '?'
    )
    if (!unassigned.length) {
        alert(t('alert_no_unassigned_entrances')); return
    }

    const turf = await import('@turf/turf')

    const roadLine = turf.lineString(
        roadEntry.data.coordinates.map(c => [c.lng, c.lat])
    )

    // Project each entrance and record its arc-length from road start.
    const withDist = unassigned.map(e => {
        const ll      = (e.layer as L.Marker).getLatLng()
        const pt      = turf.point([ll.lng, ll.lat])
        const snapped = turf.nearestPointOnLine(roadLine, pt, { units: 'meters' })
        return { entry: e, dist: snapped.properties.location ?? 0 }
    })
    withDist.sort((a, b) => a.dist - b.dist)

    // Find the current max odd/even already assigned on this road to continue.
    let oddNext = 1, evenNext = 2
    ;(featureLayers.houseEntrances as LayerEntry[])
        .filter(e =>
            e.data.entranceTypeKey === 'main_entrance' &&
            e.data.roadDbId        === store.referenceRoadDbId &&
            e.data.label           !== '?' &&
            e.data.entranceNumber  != null
        )
        .forEach(e => {
            const n = e.data.entranceNumber!
            if (n % 2 !== 0 && n >= oddNext)  oddNext  = n + 2
            if (n % 2 === 0 && n >= evenNext)  evenNext = n + 2
        })

    const phase   = PHASES.find(p => p.key === 'houseEntrances')!
    const updates: Promise<void>[] = []

    for (const { entry } of withDist) {
        const isLeft = entry.data.side === 'left'
        const number = isLeft ? oddNext : evenNext
        if (isLeft) oddNext += 2; else evenNext += 2

        entry.data.entranceNumber = number
        entry.data.label          = String(number)
        ;(entry.layer as L.Marker).setIcon(createEntranceIcon(String(number), phase.color))

        const dbId = (entry.layer as any)._dbId
        updates.push(
            apiFetch(`/api/update/${dbId}`, {
                method: 'PUT', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ data: entry.data }),
            }).then(() => {}).catch(err => console.error(`setHouseNumbers save error (id=${dbId}):`, err))
        )
    }

    await Promise.all(updates)
    syncCounts()
}
