// ─── STYLES, ICONS, POPUP, APPLY-STYLE ───────────────────────────────────────

import { AREA_TYPES, PHASES } from '../phases'
import { POLYLINE_WEIGHT }    from './state'
import type { FeatureData }   from '../types'

declare const L: typeof import('leaflet')

// ─── POLYGON STYLES ───────────────────────────────────────────────────────────

export function areaStyle(areaTypeKey: string): L.PathOptions {
    const at = AREA_TYPES.find(a => a.key === areaTypeKey) ?? AREA_TYPES[0]
    return { color: at.color, weight: 2.5, fillOpacity: 0, dashArray: '10, 6' }
}

export const polygonStyles: Record<string, L.PathOptions> = {
    districts:       { color: '#f39c12', weight: 3, fillOpacity: 0 },
    publicBuildings: { color: '#e67e22', weight: 3, fillOpacity: 0.25, fillColor: '#e67e22' },
    publicSpaces:    { color: '#2ecc71', weight: 3, fillOpacity: 0.20, fillColor: '#2ecc71' },
}

export const scatteredStyle: L.PathOptions = {
    color: '#7f8c8d', weight: 1.5, fillOpacity: 0.10, fillColor: '#7f8c8d', dashArray: '3, 6',
}

// ─── ICONS ────────────────────────────────────────────────────────────────────

export function createEntranceIcon(label: string | number, color = '#27ae60'): L.DivIcon {
    const text = String(label ?? '').trim().slice(0, 6) || '?'
    return L.divIcon({
        className: 'entrance-marker',
        html: `<div class="entrance-icon" style="background:${color}">${text}</div>`,
        iconSize:    [28, 28],
        iconAnchor:  [14, 14],
        popupAnchor: [0, -14],
    })
}


export function createEndpointIcon(char: string, angleDeg: number, color: string, large = false): L.DivIcon {
    const size = large ? 36 : 24, fs = large ? 28 : 20, half = size / 2
    return L.divIcon({
        className: 'line-endpoint-marker',
        html: `<div class="endpoint-icon" style="color:${color};width:${size}px;height:${size}px;font-size:${fs}px;transform:rotate(${angleDeg}deg)">${char}</div>`,
        iconSize:   [size, size],
        iconAnchor: [half, half],
    })
}

// ─── POPUP BUILDER ────────────────────────────────────────────────────────────

export function buildPopup(data: FeatureData, phase: typeof PHASES[number], dbId?: number): string {
    const lines = [`<b>${data.label}</b>`, `<small>${phase.label}</small>`]
    if (data.decisionNumber)    lines.push(`<small>Decision: ${data.decisionNumber}</small>`)
    if (data.decisionDate)      lines.push(`<small>Date: ${data.decisionDate}</small>`)
    if (data.roadLabel)         lines.push(`<small>Road: ${data.roadLabel}</small>`)
    if (data.side)              lines.push(`<small>Side: ${data.side} (${data.side === 'left' ? 'odd' : 'even'})</small>`)
    if (data.mainEntranceLabel) lines.push(`<small>Main entrance: ${data.mainEntranceLabel}</small>`)
    return lines.join('<br>')
}

// ─── STYLE APPLICATOR ────────────────────────────────────────────────────────

export function applyStyle(layer: L.Layer, phase: typeof PHASES[number], modalResult: FeatureData): void {
    if      (phase.key === 'areas')           (layer as L.Path).setStyle(areaStyle(modalResult.areaTypeKey ?? 'central_urban'))
    else if (phase.key === 'districts')       (layer as L.Path).setStyle(polygonStyles.districts)
    else if (phase.key === 'publicBuildings') (layer as L.Path).setStyle(polygonStyles.publicBuildings)
    else if (phase.key === 'publicSpaces')    (layer as L.Path).setStyle(polygonStyles.publicSpaces)
    else if (phase.drawType === 'polyline')   (layer as L.Path).setStyle({ color: phase.color, weight: POLYLINE_WEIGHT })
    else if (phase.key === 'houseEntrances') {
        if ((modalResult as any).entranceTypeKey === 'secondary_entrance') {
            const bisStr = 'BIS' + String(modalResult.bisNumber ?? 1).padStart(2, '0')
            ;(layer as L.Marker).setIcon(createEntranceIcon(bisStr, '#16a085'))
        } else {
            ;(layer as L.Marker).setIcon(createEntranceIcon(String(modalResult.entranceNumber ?? modalResult.label), phase.color))
        }
    }
}
