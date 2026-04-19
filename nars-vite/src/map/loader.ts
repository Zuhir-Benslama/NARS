import { apiFetch } from '../api'
import { PHASES, API_LAYER_TO_PHASE } from '../phases'
import { store, syncCounts } from '../store'
import { useLayerStore } from '../stores/layerStore'
import type { LayerState } from '../stores/layerStore'
import { getCtx, featuresStore, type MaplibreFeature } from './state'
import { renderScatteredAreas, displayCommuneBoundary, computeCircleRing } from './geometry'
import { refreshLayerVisibility } from './labels'
import { getFeatureStyle } from './draw-events'
import { getFeatureType } from './house-numbering'
import { sanitizeApiText } from '../utils/sanitize'
import { debugError, debugLog } from '../utils/debug'
import { updateEndpointMarkers } from './road-directions'
import { loadPhase } from './phase-storage'
import type { FeatureData, LayerEntry, DbFeature, ModalResult } from '../types'

interface GeoJsonFeatureWithStyle {
    type: 'Feature'
    geometry: GeoJSON.Point | GeoJSON.LineString | GeoJSON.Polygon
    properties: Record<string, unknown>
}

export async function loadFromDatabase(): Promise<void> {
    debugLog('[LOAD] Starting...')
    store.isLoading = true
    try {
        debugLog('[LOAD] Fetching /api/load...')
        const response = await apiFetch('/api/load')
        debugLog('[LOAD] Response status:', response.status)
        const json = (await response.json()) as { features?: DbFeature[]; count?: number } | DbFeature[]

        // Support both old array format and new paginated response { features, count, skip, take }
        const features: DbFeature[] = Array.isArray(json) ? json : (json.features ?? [])
        debugLog('[LOAD] API returned', features.length, 'features')
        if (features.length > 0) {
            debugLog('[LOAD] First feature raw keys:', Object.keys(features[0]))
            debugLog(
                '[LOAD] First feature layer values:',
                features.map((f) => f.layer).filter((v, i, a) => a.indexOf(v) === i),
            )
        }
        if (features.length > 0) {
            debugLog('[LOAD] First feature raw keys:', Object.keys(features[0]))
            debugLog('[LOAD] First feature:', JSON.stringify(features[0]).substring(0, 200))
        }
        if (!features.length) {
            debugLog('[LOAD] No saved features in database.')
            return
        }

        const layerStore = useLayerStore()
        const state = layerStore.$state as LayerState
        const phaseKeys = Object.keys(state) as (keyof LayerState)[]

        for (const key of phaseKeys) {
            state[key] = []
        }
        featuresStore.clear() // wipe stale in-memory features before re-populating

        // Accumulate all MaplibreFeatures and push to featuresStore in one batchAdd at
        // the end — this calls setData exactly once instead of once per feature.
        // Multiple setData calls cause Geoman to re-process the full feature list each
        // time, throwing "feature already exists" for every feature after the first.
        const maplibreFeatures: import('./state').MaplibreFeature[] = []

        for (const feature of features) {
            try {
                // Use plural phase key for consistent logging
                const data: FeatureData = typeof feature.data === 'string' ? JSON.parse(feature.data) : feature.data

                let phaseKey = API_LAYER_TO_PHASE[feature.layer]
                if (!phaseKey && feature.feature_type) {
                    phaseKey = API_LAYER_TO_PHASE[feature.feature_type]
                }
                if (!phaseKey) {
                    // Try mapping from type to layer mapping
                    const typeToLayerMap: Record<string, string> = {
                        area: 'central_urban',
                        road: 'street',
                        district: 'district',
                        house_entrance: 'main_entrance',
                        public_building: 'public_building',
                        public_space: 'garden',
                        city_center: 'city_center',
                        naming_panel: 'naming_panel',
                    }
                    const inferredLayer = typeToLayerMap[feature.feature_type || '']
                    if (inferredLayer) {
                        phaseKey = API_LAYER_TO_PHASE[inferredLayer]
                    }
                }

                debugLog(
                    `[LOAD] Feature: id=${feature.id}, layer='${feature.layer}', feature_type='${feature.feature_type}', phaseKey='${phaseKey}'`,
                )

                if (feature.layer === 'scattered') {
                    if (data.geometry) renderScatteredAreas(data.geometry)
                    debugLog('[LOAD] Scattered area rendered, continuing')
                    continue
                }

                debugLog('[LOAD] phaseKey:', phaseKey, 'from layer:', feature.layer, 'or data.type:', data?.type)
                // Use PHASES list instead of hasOwnProperty on the Proxy (which bypasses the trap)
                const validPhase = PHASES.find((p) => p.key === phaseKey)
                if (!phaseKey || !validPhase) {
                    debugError(
                        '[LOAD] Skipped feature',
                        feature.id,
                        '— unknown layer/type:',
                        feature.layer,
                        data?.type,
                        'phaseKey:',
                        phaseKey,
                    )
                    continue
                }

                const phase = validPhase
                debugLog('[LOAD] phase found:', phaseKey)

                const layerEntry: LayerEntry = {
                    id: `feat_${feature.id}`,
                    dbId: feature.id,
                    data,
                    type: getFeatureType(phase.drawType),
                }
                ;(state[phaseKey as keyof LayerState] as LayerEntry[]).push(layerEntry)

                const geojsonFeature = buildGeoJsonFeature(feature.id, data, phase)
                if (geojsonFeature) {
                    maplibreFeatures.push({
                        id: `feat_${feature.id}`,
                        geometry: geojsonFeature.geometry,
                        properties: geojsonFeature.properties as MaplibreFeature['properties'],
                    })
                } else {
                    debugLog(
                        '[LOAD] Skipped feature geometry',
                        feature.id,
                        '— no valid geometry. lat:',
                        data?.lat,
                        'lng:',
                        data?.lng,
                        'coords:',
                        data?.coordinates?.length ?? 'none',
                    )
                }

                if (phase.key === 'cityCenter' && data.lat != null && data.lng != null) {
                    store.cityCenterMode = 'city_center'
                    store.cityCenterLatLng = { lat: data.lat, lng: data.lng }
                }
            } catch (err) {
                debugError('[LOAD] Error loading feature:', err)
            }
        }

        // Single setData call for all loaded features
        featuresStore.batchAdd(maplibreFeatures)
        debugLog('[LOAD] batchAdd', maplibreFeatures.length, 'features into features source')
        const ctxSource = getCtx().featuresSource as { _data?: { features?: unknown[] } } | null
        if (ctxSource?._data) {
            debugLog('[LOAD] Source now has', ctxSource._data.features?.length ?? 0, 'features')
        }
        if (maplibreFeatures.length > 0) {
            debugLog('[LOAD] First feature:', JSON.stringify(maplibreFeatures[0]).substring(0, 300))
            const byPhase = new Map<string, number>()
            for (const f of maplibreFeatures) {
                const pk = f.properties.phaseKey
                byPhase.set(pk, (byPhase.get(pk) || 0) + 1)
            }
            debugLog('[LOAD] Features by phase:', Object.fromEntries(byPhase))
        }

        // Restore phase: prefer user’s explicitly chosen phase if available
        const communeId = (store.user as { commune?: { id?: number | string } } | null | undefined)?.commune?.id ?? null
        const persistedPhase = loadPhase(communeId)

        if (typeof persistedPhase === 'number' && persistedPhase >= 0 && persistedPhase < PHASES.length) {
            store.currentPhase = persistedPhase
        } else {
            // Fallback: start from phase 0 when there is no saved choice
            store.currentPhase = 0
        }
        debugLog('[LOAD] Restored phase:', store.currentPhase, PHASES[store.currentPhase]?.key)
        debugLog(
            '[LOAD] featureLayers:',
            Object.fromEntries(Object.entries(state).map(([k, v]) => [k, (v as LayerEntry[]).length])),
        )

        syncCounts()
        refreshLayerVisibility()

        // Update road endpoint markers for any existing roads
        updateEndpointMarkers()
        debugLog('[LOAD] Loading complete')
    } catch (err) {
        debugError('Load error:', err)
        store.loadError = true
    } finally {
        store.isLoading = false
    }
}

function buildGeoJsonFeature(
    dbId: string,
    data: FeatureData,
    phase: (typeof PHASES)[number],
): GeoJsonFeatureWithStyle | null {
    const style = getFeatureStyle(phase, data as unknown as ModalResult)
    const sanitizedLabel = sanitizeApiText(data.label)

    debugLog(
        '[buildGeoJson]',
        phase.key,
        'lat:',
        data.lat,
        'lng:',
        data.lng,
        'coords:',
        data.coordinates?.length ?? 'none',
    )

    // Point features (city center, house entrances, public buildings, naming panels)
    if (data.lat && data.lng) {
        // City center: render as a geographic circle (LineString ring) for simple outline.
        if (phase.key === 'cityCenter') {
            const radius = data.radius
            if (radius && radius > 0) {
                const ring = computeCircleRing(data.lat, data.lng, radius)
                // Close the ring
                ring.push([ring[0][0], ring[0][1]])
                return {
                    type: 'Feature' as const,
                    geometry: { type: 'LineString', coordinates: ring },
                    properties: {
                        dbId,
                        phaseKey: phase.key,
                        label: sanitizedLabel,
                        geomType: 'LineString',
                        lineColor: '#e74c3c',
                        lineWidth: 6,
                        radius,
                    },
                }
            }
            // Fallback: if radius is missing, render as a visible center marker
            return {
                type: 'Feature' as const,
                geometry: { type: 'Point', coordinates: [data.lng, data.lat] },
                properties: {
                    dbId,
                    phaseKey: phase.key,
                    label: sanitizedLabel,
                    geomType: 'Point',
                    ...style,
                    circleColor: '#e74c3c',
                    circleRadius: 12,
                    textColor: '#000000',
                },
            }
        }

        return {
            type: 'Feature' as const,
            geometry: {
                type: 'Point',
                coordinates: [data.lng, data.lat],
            },
            properties: {
                dbId,
                phaseKey: phase.key,
                label: sanitizedLabel,
                geomType: 'Point',
                ...style,
            },
        }
    } else if (data.coordinates && data.coordinates.length > 0) {
        // Line features (roads)
        if (phase.drawType === 'polyline') {
            return {
                type: 'Feature' as const,
                geometry: {
                    type: 'LineString',
                    coordinates: data.coordinates.map((c) => [c.lng, c.lat]),
                },
                properties: {
                    dbId,
                    phaseKey: phase.key,
                    label: sanitizedLabel,
                    geomType: 'LineString',
                    ...style,
                },
            }
        } else {
            // Polygon (or MultiPolygon flattened to single ring)
            const ring = data.coordinates.map((c) => [c.lng, c.lat])
            // Ensure the ring is closed (first == last)
            const first = ring[0],
                last = ring[ring.length - 1]
            if (first[0] !== last[0] || first[1] !== last[1]) {
                ring.push([...first])
            }
            return {
                type: 'Feature' as const,
                geometry: {
                    type: 'Polygon',
                    coordinates: [ring],
                },
                properties: {
                    dbId,
                    phaseKey: phase.key,
                    label: sanitizedLabel,
                    geomType: 'Polygon',
                    ...style,
                },
            }
        }
    }

    debugLog(
        '[LOAD] Skipping feature geometry:',
        data.type,
        'lat:',
        data.lat,
        'lng:',
        data.lng,
        'coords:',
        data.coordinates?.length ?? 'none',
    )
    return null
}

export async function loadUserAndCommune(): Promise<void> {
    try {
        const user = await apiFetch('/api/current_user').then((r) => r.json())
        store.user = user
        store.municipalityName = user.commune?.name_fr ?? ''
        if (user.commune?.id) await displayCommuneBoundary(user.commune.id as number, user.commune.name_fr as string)
    } catch (err) {
        debugError('Commune nav error:', err)
    }
}
