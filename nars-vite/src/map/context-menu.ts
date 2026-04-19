// ─── CONTEXT MENU ─────────────────────────────────────────────────────────────

import { apiFetch } from '../api'
import { store, selectedFeatureDbId, setSelectedFeature } from '../store'
import { PHASES } from '../phases'
import { t } from '../i18n'
import { featuresStore, ctx } from './state'
import { showToast, showConfirm } from '../toast'
import { recordDelete } from './undo'
import { enableEditMode } from './draw-events'
import { openEditModal } from '../store'
import { isSnappingEnabled } from './draw-complete'
import { toggleSnapping } from './snapping'
import type { LayerEntry } from '../types'
import type { LayerState } from '../stores/layerStore'
import { useLayerStore } from '../stores/layerStore'
import { setHouseNumbers } from './house-numbering'
import { setReferenceRoad, clearReferenceRoad, setReferenceEntrance } from './house-entrances'
import { computeCircleRing } from './geometry'
import { updateEndpointMarkers, computeAndApplyRoadDirections } from './road-directions'
import { generateNamingPanels } from './naming-panels'

// ─── DOM-BASED MENU (no innerHTML) ────────────────────────────────────────────

interface CtxMenuItem {
    label?: string
    danger?: boolean
    separator?: boolean
    onClick?: () => void
}

let contextMenuEl: HTMLElement | null = null

function createContextMenuEl(): HTMLElement {
    const el = document.createElement('div')
    el.className = 'nars-ctx-menu'
    el.style.position = 'fixed'
    el.style.left = '-9999px'
    el.style.top = '-9999px'
    el.style.zIndex = '100000'
    el.style.display = 'none'
    document.body.appendChild(el)

    const hide = () => {
        el.style.display = 'none'
    }
    document.addEventListener('click', (e) => {
        if (!el.contains(e.target as Node)) hide()
    })
    document.addEventListener('contextmenu', (e) => {
        if (!el.contains(e.target as Node)) hide()
    })
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') hide()
    })
    return el
}

function getCtxEl(): HTMLElement {
    if (!contextMenuEl) contextMenuEl = createContextMenuEl()
    return contextMenuEl
}

function setMenuItems(el: HTMLElement, items: CtxMenuItem[]): void {
    el.innerHTML = ''
    for (const item of items) {
        if (item.separator) {
            const sep = document.createElement('div')
            sep.style.borderTop = '1px solid var(--dropdown-border, #eee)'
            sep.style.margin = '2px 0'
            el.appendChild(sep)
            continue
        }
        const child = document.createElement('div')
        child.className = 'nars-ctx-item'
        if (item.danger) child.style.color = '#ef4444'
        child.textContent = item.label!
        child.addEventListener('click', (e) => {
            e.stopPropagation()
            el.style.display = 'none'
            item.onClick!()
        })
        el.appendChild(child)
    }
}

interface DrawContextEvent {
    originalEvent?: MouseEvent
    point: { x: number; y: number }
}

function placeMenu(el: HTMLElement, x: number, y: number): void {
    el.style.left = '0'
    el.style.top = '0'
    el.style.display = 'block'
    void el.offsetHeight
    const w = el.offsetWidth || 180,
        h = el.offsetHeight || 100
    el.style.left = (x + w > window.innerWidth ? x - w : x) + 'px'
    el.style.top = (y + h > window.innerHeight ? y - h : y) + 'px'
}

export function showContextMenu(x: number, y: number, dbId: string, phaseKey: string): void {
    const el = getCtxEl()
    const layerStore = useLayerStore()
    const state = layerStore.$state as LayerState
    const currentPhase = PHASES[store.currentPhase]
    const currentPhaseKey = currentPhase?.key ?? ''
    const isRoad = phaseKey === 'roads'
    const isRoadsPhase = currentPhaseKey === 'roads'
    const isHouseEntrancesPhase = currentPhaseKey === 'houseEntrances'
    const roadInHousePhase = isRoad && isHouseEntrancesPhase
    const isCurrentPhase = phaseKey === currentPhaseKey
    const isArea = phaseKey === 'areas'
    const canEdit = (isCurrentPhase || isArea) && !roadInHousePhase && phaseKey !== 'houseEntrances'
    const isCityCenter = phaseKey === 'cityCenter'
    const isMainEntrance =
        phaseKey === 'houseEntrances' &&
        (state.houseEntrances?.some((e) => e.dbId === dbId && e.data.entranceTypeKey === 'main_entrance') ?? false)

    // City center is read-only outside its phase
    if (isCityCenter && currentPhaseKey !== 'cityCenter') {
        const items: CtxMenuItem[] = [{ label: t('ctx_cc_lock'), onClick: () => {} }]
        setMenuItems(el, items)
        placeMenu(el, x, y)
        return
    }

    const items: CtxMenuItem[] = []

    // Edit options for editable features
    if (canEdit && !isCityCenter) {
        items.push({ label: t('ctx_edit_geom'), onClick: () => enableEditGeometry(dbId) })
    }
    if (canEdit) {
        items.push({ label: t('ctx_edit_info'), onClick: () => editFeatureInfo(dbId) })
    }
    if (canEdit) {
        items.push({ label: t('ctx_remove'), danger: true, onClick: () => removeFeature(dbId) })
    }

    // Road directions option in roads phase
    if (isRoad && isRoadsPhase) {
        items.push({ label: t('ctx_road_dir'), onClick: () => computeRoadDirections() })
    }

    // Reference road options in houseEntrances phase
    const isCurrentRef = isRoad && dbId === store.referenceRoadDbId
    if (isRoad && isHouseEntrancesPhase && !isCurrentRef) {
        items.push({ label: t('ctx_road_ref'), onClick: () => setReferenceRoad(dbId) })
    }
    if (isRoad && isHouseEntrancesPhase && isCurrentRef) {
        items.push({ label: t('ctx_road_ref_remove'), onClick: () => clearReferenceRoad() })
    }
    if (isMainEntrance && isHouseEntrancesPhase) {
        items.push({ label: t('ctx_ent_ref'), onClick: () => setReferenceEntrance(dbId) })
    }

    // Snapping toggle - always shown
    const snapOn = isSnappingEnabled()
    items.push({
        label: snapOn ? '\u2298 Disable Snapping' : '\u229E Enable Snapping',
        onClick: () => {
            const e = toggleSnapping()
            showToast(`Snapping ${e ? 'enabled' : 'disabled'}`, 'info')
        },
    })

    setMenuItems(el, items)
    placeMenu(el, x, y)
}

export function bindContextMenu(e: DrawContextEvent, dbId: string, phaseKey: string): void {
    showContextMenu(e.originalEvent?.clientX || e.point.x, e.originalEvent?.clientY || e.point.y, dbId, phaseKey)
}

export async function showMapContextMenu(x: number, y: number, phase: (typeof PHASES)[number]): Promise<void> {
    const el = getCtxEl()

    const items: CtxMenuItem[] = []

    if (phase.key === 'roads') {
        items.push({ label: t('ctx_road_dir'), onClick: () => computeRoadDirections() })
    } else if (phase.key === 'houseEntrances') {
        items.push({ label: t('ctx_house_nums'), onClick: () => setHouseNumbers() })
    } else if (phase.key === 'namingPanels') {
        items.push({ label: t('ctx_set_naming_panels'), onClick: () => generateNamingPanels() })
    }
    if (items.length > 0) {
        items.push({ separator: true })
    }
    const snapOn = isSnappingEnabled()
    items.push({
        label: snapOn ? '\u2298 Disable Snapping' : '\u229E Enable Snapping',
        onClick: () => {
            const e = toggleSnapping()
            showToast(`Snapping ${e ? 'enabled' : 'disabled'}`, 'info')
        },
    })

    setMenuItems(el, items)
    placeMenu(el, x, y)
}

// ─── ACTIONS ──────────────────────────────────────────────────────────────────

function enableEditGeometry(dbId: string): void {
    // Guard: only the selected feature can be edited.
    // If no feature is selected, allow editing (first right-click auto-selects).
    if (selectedFeatureDbId !== null && dbId !== selectedFeatureDbId) {
        showToast('Click the feature to select it first, then right-click to edit.', 'info')
        return
    }

    // Auto-select the feature if none is selected yet
    if (selectedFeatureDbId === null) {
        setSelectedFeature(dbId)
    }

    const entry = findLayerEntryByDbId(dbId)
    if (!entry) {
        showToast('Feature not found', 'error')
        return
    }

    // City center: editing is just changing the radius via the edit modal, not dragging vertices
    if (entry.type === 'circle') {
        void editFeatureInfo(dbId)
        return
    }

    if (!ctx.geoman) {
        showToast('Edit mode not available', 'error')
        return
    }

    // Pass the feature's in-memory ID so snapping excludes it
    enableEditMode(entry.id)
    showToast('Edit mode: drag vertices to reshape. Right-click to cancel.', 'info')
}

async function editFeatureInfo(dbId: string): Promise<void> {
    const entry = findLayerEntryByDbId(dbId)
    if (!entry) {
        showToast('Feature not found', 'error')
        return
    }

    // House entrances are fully reference-driven (no manual info modal).
    if (entry.data.type === 'houseEntrances') return

    const phaseIndex = PHASES.findIndex((p) => p.key === entry.data.type)
    if (phaseIndex === -1) {
        showToast('Unknown feature type', 'error')
        return
    }

    const result = await openEditModal(phaseIndex, dbId, entry.data)
    if (!result) return // user cancelled

    try {
        await apiFetch(`/api/update/${dbId}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ data: { ...entry.data, ...result } }),
        })

        // Update in-memory data
        Object.assign(entry.data, result)

        // City center: re-render as LineString ring with new radius
        if (entry.type === 'circle' && entry.data.radius && entry.data.lat && entry.data.lng) {
            const ring = computeCircleRing(entry.data.lat, entry.data.lng, entry.data.radius)
            ring.push([ring[0][0], ring[0][1]]) // close the ring
            featuresStore.update(entry.id, {
                geometry: { type: 'LineString', coordinates: ring },
                properties: {
                    phaseKey: 'cityCenter',
                    label: entry.data.label,
                    geomType: 'LineString',
                    lineColor: '#e74c3c',
                    lineWidth: 6,
                    ...(entry.data.radius != null ? { radius: entry.data.radius } : {}),
                    /* eslint-disable-next-line @typescript-eslint/no-explicit-any */
                } as any,
            })
        } else {
            // Update rendered label for other feature types
            featuresStore.update(entry.id, { properties: { phaseKey: entry.data.type, label: result.label as string } })
        }

        showToast('Feature updated.', 'success')
    } catch (err) {
        showToast('Save failed: ' + (err as Error).message, 'error')
    }
}

async function removeFeature(dbId: string): Promise<void> {
    const entry = findLayerEntryByDbId(dbId)
    if (!entry) {
        showToast('Feature not found', 'error')
        return
    }

    const confirmed = await showConfirm(`Delete "${entry.data.label}"?`)
    if (!confirmed) return

    // Find the phase key before removing
    const layerStore = useLayerStore()
    const state = layerStore.$state as LayerState
    let phaseKey = ''
    for (const key of Object.keys(state)) {
        const entries = state[key as keyof LayerState]
        if (entries?.some((f) => f.dbId === dbId)) {
            phaseKey = key
            break
        }
    }

    // Record for undo BEFORE the actual delete
    if (phaseKey) recordDelete(entry, phaseKey)

    try {
        await apiFetch(`/api/delete/${dbId}`, { method: 'DELETE' })

        featuresStore.remove(entry.id)
        const currentState = layerStore.$state as LayerState
        for (const key of Object.keys(currentState)) {
            const entries = currentState[key as keyof LayerState]
            if (entries) {
                currentState[key as keyof LayerState] = entries.filter((f) => f.dbId !== dbId) as never
            }
        }

        // Clear city center state from store when it's deleted
        if (phaseKey === 'cityCenter') {
            store.cityCenterMode = null
            store.cityCenterLatLng = null
        }

        // Update road endpoint markers after deletion
        if (phaseKey === 'roads') {
            updateEndpointMarkers()
        }

        showToast('Feature deleted.', 'success')
    } catch (err) {
        showToast('Delete failed: ' + (err as Error).message, 'error')
    }
}

// ─── HELPERS ──────────────────────────────────────────────────────────────────

// Uses LayerEntry.dbId — the actual database PK
function findLayerEntryByDbId(dbId: string): LayerEntry | null {
    const layerStore = useLayerStore()
    const state = layerStore.$state as LayerState
    for (const key of Object.keys(state)) {
        const entries = state[key as keyof LayerState]
        const entry = entries?.find((e) => e.dbId === dbId)
        if (entry) return entry
    }
    return null
}

async function computeRoadDirections(): Promise<void> {
    await computeAndApplyRoadDirections()
}
