// ─── NAMING PANELS GENERATOR ────────────────────────────────────────────────
// Automatically places naming panel markers derived from existing features.
// Rules:
//  - Districts: every vertex
//  - Roads: start, end, and every 100 meters along the polyline
//  - Public Buildings: first drawn vertex of the polygon
//  - Public Spaces: first drawn vertex of the polygon
//  - Dedupe if a naming panel already exists within 3 meters
//  - Labels are copied from the source feature (no suffixes)

import { PHASES } from '../phases'
import { featureLayers } from '../store'
import type { FeatureData, LayerEntry } from '../types'
import { createEntranceIcon, buildPopup } from './styles'
import { bindContextMenu } from './context-menu'
import { ctx } from './state'

// Leaflet type import
declare const L: typeof import('leaflet')

const DEDUPE_METERS = 3
const ROAD_STEP_METERS = 100

function getNamingPanelsPhase() {
  const phase = PHASES.find(p => p.key === 'namingPanels')
  if (!phase) throw new Error('Naming Panels phase not found in PHASES')
  return phase
}

function nearExisting(ll: L.LatLng): boolean {
  const existing = featureLayers.namingPanels as LayerEntry[]
  for (const e of existing) {
    if (!(e.layer instanceof L.Marker)) continue
    const pll = (e.layer as L.Marker).getLatLng()
    if (pll.distanceTo(ll) < DEDUPE_METERS) return true
  }
  return false
}

function polygonVertices(poly: L.Polygon): L.LatLng[] {
  const ring = (poly.getLatLngs()[0] as L.LatLng[])
  if (!ring?.length) return []
  const res: L.LatLng[] = []
  for (let i = 0; i < ring.length; i++) {
    // Ignore duplicate closing vertex if it equals the first
    if (i === ring.length - 1) {
      const f = ring[0], l = ring[ring.length - 1]
      if (f && l && f.lat === l.lat && f.lng === l.lng) break
    }
    res.push(ring[i])
  }
  return res
}

function firstVertex(poly: L.Polygon): L.LatLng | null {
  const ring = (poly.getLatLngs()[0] as L.LatLng[])
  return ring?.length ? ring[0] : null
}

function roadStations(line: L.Polyline): L.LatLng[] {
  const pts = (line.getLatLngs() as L.LatLng[])
  if (!pts || pts.length < 2) return []
  const out: L.LatLng[] = []

  // Always include start and end
  out.push(pts[0])

  let nextAt = ROAD_STEP_METERS
  let acc = 0
  for (let i = 0; i < pts.length - 1; i++) {
    const a = pts[i]
    const b = pts[i + 1]
    const segLen = a.distanceTo(b)
    if (segLen <= 0) continue

    while (acc + segLen >= nextAt) {
      const remain = nextAt - acc
      const t = remain / segLen
      const lat = a.lat + (b.lat - a.lat) * t
      const lng = a.lng + (b.lng - a.lng) * t
      out.push(L.latLng(lat, lng))
      nextAt += ROAD_STEP_METERS
    }
    acc += segLen
  }

  out.push(pts[pts.length - 1])
  return out
}

async function addPanelIfMissing(label: string, ll: L.LatLng, color: string): Promise<void> {
  if (nearExisting(ll)) return

  const phase = getNamingPanelsPhase()
  const data: FeatureData = {
    type: 'namingPanels',
    label,
    decisionNumber: '',
    decisionDate:   '',
    lat:            ll.lat,
    lng:            ll.lng,
  }

  // Use the source feature's phase color so panels are visually linked to their origin.
  const icon = createEntranceIcon(label, color)
  // Ensure naming panels render on top by using a dedicated high z-index pane
  if (!ctx.map.getPane('namingPanelsPane')) {
    ctx.map.createPane('namingPanelsPane')
    ctx.map.getPane('namingPanelsPane')!.style.zIndex = '10050'
  }
  const marker = L.marker([ll.lat, ll.lng], { icon, pane: 'namingPanelsPane' } as any)
  ;(marker as any).pm?.setOptions?.({ pmIgnore: true })

  // Add to map and featureLayers (no context menu, no DB id)
  ctx.drawnItems.addLayer(marker)
  marker.bindPopup(buildPopup(data, phase))
  ;(featureLayers.namingPanels as LayerEntry[]).push({ layer: marker, data })
}

export async function generateNamingPanels(): Promise<void> {
  const tasks: Promise<void>[] = []

  // Districts: every vertex
  for (const e of featureLayers.districts as LayerEntry[]) {
    if (!(e.layer instanceof L.Polygon)) continue
    const verts = polygonVertices(e.layer)
    for (const v of verts) tasks.push(addPanelIfMissing(e.data.label, v, '#f39c12'))
  }

  // Roads: start, end, every 100m
  for (const e of featureLayers.roads as LayerEntry[]) {
    if (!(e.layer instanceof L.Polyline) || (e.layer instanceof L.Polygon)) continue
    const stations = roadStations(e.layer)
    for (const v of stations) tasks.push(addPanelIfMissing(e.data.label, v, '#3498db'))
  }

  // Public Buildings: first vertex
  for (const e of featureLayers.publicBuildings as LayerEntry[]) {
    if (!(e.layer instanceof L.Polygon)) continue
    const v = firstVertex(e.layer)
    if (v) tasks.push(addPanelIfMissing(e.data.label, v, '#e67e22'))
  }

  // Public Spaces: first vertex
  for (const e of featureLayers.publicSpaces as LayerEntry[]) {
    if (!(e.layer instanceof L.Polygon)) continue
    const v = firstVertex(e.layer)
    if (v) tasks.push(addPanelIfMissing(e.data.label, v, '#2ecc71'))
  }

  await Promise.all(tasks)
}

// Expose for context-menu action
;(window as any).__narsSetNamingPanels = async () => {
  try { await generateNamingPanels() } catch (err) { console.error('Generate naming panels error:', err) }
}
