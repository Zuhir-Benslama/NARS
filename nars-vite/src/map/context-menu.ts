// ─── CONTEXT MENU ─────────────────────────────────────────────────────────────
// Builds and displays the right-click context menu for both features and the
// map background. All edit operations delegate to the extracted modules:
//   edit-actions.ts   — removeFeature, editGeometry, editFeatureInfo
//   house-entrances.ts — setReferenceRoad/Entrance/clearReferenceRoad, setHouseNumbers

import { ctx }                           from './state'
import { featureLayers, store }          from '../store'
import { PHASES }                        from '../phases'
import type { LayerEntry }               from '../types'
import { t }                             from '../i18n'

import { removeFeature, editGeometry, editFeatureInfo } from './edit-actions'
import {
    setReferenceRoad, clearReferenceRoad,
    setReferenceEntrance, setHouseNumbers,
} from './house-entrances'

declare const L: typeof import('leaflet')

// ─── SINGLETON DOM ELEMENT ────────────────────────────────────────────────────

function createContextMenuEl(): HTMLElement {
    const el = document.createElement('div')
    el.id        = 'nars-ctx-menu'
    el.className = 'nars-ctx-menu'
    el.style.display = 'none'
    document.body.appendChild(el)

    const hide = () => { el.style.display = 'none' }
    document.addEventListener('click',       (e) => { if (!el.contains(e.target as Node)) hide() })
    document.addEventListener('contextmenu', (e) => { if (!el.contains(e.target as Node)) hide() })
    document.addEventListener('keydown',     (e) => { if (e.key === 'Escape') hide() })
    return el
}

let _ctxEl: HTMLElement | null = null
function getCtxEl(): HTMLElement {
    if (!_ctxEl) _ctxEl = createContextMenuEl()
    return _ctxEl
}

// ─── POSITION HELPER ──────────────────────────────────────────────────────────

function placeMenu(el: HTMLElement, x: number, y: number): void {
    // Measure off-screen first to get natural dimensions
    el.style.left = '-9999px'
    el.style.top  = '-9999px'
    el.style.display = 'block'
    const w = el.offsetWidth, h = el.offsetHeight
    el.style.left = (x + w > window.innerWidth  ? x - w : x) + 'px'
    el.style.top  = (y + h > window.innerHeight ? y - h : y) + 'px'
}

// ─── FEATURE CONTEXT MENU ─────────────────────────────────────────────────────
// Right-click on a drawn feature.

function showContextMenu(x: number, y: number, dbId: number, phaseKey: string): void {
    const el           = getCtxEl()
    const currentPhase = PHASES[store.currentPhase]?.key

    // City center circles are visible in other phases but are read-only there.
    if (phaseKey === 'cityCenter' && currentPhase !== 'cityCenter') {
        el.innerHTML = `<div class="nars-ctx-item nars-ctx-disabled">${t('ctx_cc_lock')}</div>`
        placeMenu(el, x, y)
        el.querySelector('.nars-ctx-disabled')?.addEventListener('click', () => { el.style.display = 'none' })
        return
    }

    const isRoad                = phaseKey === 'roads'
    const isRoadsPhase          = currentPhase === 'roads'
    const isHouseEntrancesPhase = currentPhase === 'houseEntrances'
    const roadInHousePhase      = isRoad && isHouseEntrancesPhase

    const isMainEntrance = phaseKey === 'houseEntrances' &&
        (featureLayers.houseEntrances as LayerEntry[])
            .find(e => (e.layer as any)._dbId === dbId)
            ?.data?.entranceTypeKey === 'main_entrance'

    // Per-context items
    const roadDir      = isRoad && isRoadsPhase
        ? `<div class="nars-ctx-item" data-action="road-dir">${t('ctx_road_dir')}</div>` : ''

    const isCurrentRef = isRoad && dbId === store.referenceRoadDbId
    const setRoadRef   = isRoad && isHouseEntrancesPhase && !isCurrentRef
        ? `<div class="nars-ctx-item" data-action="set-road-ref">${t('ctx_road_ref')}</div>` : ''
    const rmRoadRef    = isRoad && isHouseEntrancesPhase && isCurrentRef
        ? `<div class="nars-ctx-item" data-action="remove-road-ref">${t('ctx_road_ref_remove')}</div>` : ''

    const setEntRef    = isMainEntrance && isHouseEntrancesPhase
        ? `<div class="nars-ctx-item" data-action="set-entrance-ref">${t('ctx_ent_ref')}</div>` : ''

    // Roads are fully read-only in the House Entrances phase.
    const editInfo     = !roadInHousePhase
        ? `<div class="nars-ctx-item" data-action="edit">${t('ctx_edit_info')}</div>`         : ''
    const editGeom     = !roadInHousePhase
        ? `<div class="nars-ctx-item" data-action="geometry">${t('ctx_edit_geom')}</div>` : ''
    const removeItem   = !roadInHousePhase
        ? `<div class="nars-ctx-item nars-ctx-danger" data-action="remove">${t('ctx_remove')}</div>` : ''

    el.innerHTML = `${editInfo}${editGeom}${roadDir}${setRoadRef}${rmRoadRef}${setEntRef}${removeItem}`
    placeMenu(el, x, y)

    el.querySelectorAll('.nars-ctx-item').forEach(item => {
        (item as HTMLElement).onclick = (e) => {
            e.stopPropagation()
            el.style.display = 'none'
            const action = (item as HTMLElement).dataset.action
            if      (action === 'edit')              editFeatureInfo(dbId)
            else if (action === 'geometry')           editGeometry(dbId)
            else if (action === 'road-dir')           import('./road-directions').then(m => m.computeAndApplyRoadDirections())
            else if (action === 'set-road-ref')       setReferenceRoad(dbId)
            else if (action === 'remove-road-ref')    clearReferenceRoad()
            else if (action === 'set-entrance-ref')   setReferenceEntrance(dbId)
            else if (action === 'remove')             removeFeature(dbId)
        }
    })
}

// ─── BIND TO LAYER ────────────────────────────────────────────────────────────

export function bindContextMenu(layer: L.Layer, dbId: number, phaseKey: string): void {
    layer.on('contextmenu', (e: any) => {
        try {
            e.originalEvent.preventDefault()
            e.originalEvent.stopPropagation()
            // Signal the map-level handler to skip this tick
            // (Leaflet re-fires contextmenu to the map after a layer handles it).
            ;(ctx.map as any)._narsFeatureCtxHandled = true

            if ((ctx.map as any).pm.globalDrawModeEnabled()) {
                ;(ctx.map as any).pm.disableDraw(); return
            }
            if ((ctx.map as any).pm.globalEditModeEnabled()) {
                ;(ctx.map as any).pm.disableGlobalEditMode(); return
            }
            showContextMenu(e.originalEvent.clientX, e.originalEvent.clientY, dbId, phaseKey)
        } catch (err) { console.error('Context menu error:', err) }
    })
}

// ─── MAP BACKGROUND CONTEXT MENU ──────────────────────────────────────────────
// Shown when the user right-clicks on the map canvas (not on a feature).

export function showMapContextMenu(
    x: number, y: number,
    phase: typeof import('../phases').PHASES[number],
): void {
    const el = getCtxEl()

    if      (phase.key === 'roads')
        el.innerHTML = `<div class="nars-ctx-item" data-action="road-dir">${t('ctx_road_dir')}</div>`
    else if (phase.key === 'houseEntrances')
        el.innerHTML = `<div class="nars-ctx-item" data-action="set-house-numbers">${t('ctx_house_nums')}</div>`
    else if (phase.key === 'namingPanels')
        el.innerHTML = `<div class="nars-ctx-item" data-action="set-naming-panels">${t('ctx_set_naming_panels')}</div>`
    else return

    placeMenu(el, x, y)

    el.querySelectorAll('.nars-ctx-item').forEach(item => {
        (item as HTMLElement).onclick = (e) => {
            e.stopPropagation()
            el.style.display = 'none'
            const action = (item as HTMLElement).dataset.action
            if      (action === 'road-dir')          import('./road-directions').then(m => m.computeAndApplyRoadDirections())
            else if (action === 'set-naming-panels')  (window as any).__narsSetNamingPanels?.()
            else if (action === 'set-house-numbers')  setHouseNumbers()
        }
    })
}
