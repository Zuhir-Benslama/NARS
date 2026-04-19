// ─── STORE TESTS ──────────────────────────────────────────────────────────────
// Tests for store.ts functions.

import { describe, it, expect, beforeEach, vi } from 'vitest'

// Mock Vue's reactive
vi.mock('vue', async () => {
    const actual = await vi.importActual('vue')
    return {
        ...(actual as any),
        reactive: vi.fn((obj) => obj),
    }
})

// Mock PHASES
vi.mock('./phases', () => ({
    PHASES: [
        { index: 0, key: 'areas', label: 'Areas', drawType: 'polygon', color: '#8e44ad', hint: 'hint' },
        { index: 1, key: 'districts', label: 'Districts', drawType: 'polygon', color: '#f39c12', hint: 'hint' },
        { index: 2, key: 'cityCenter', label: 'City Center', drawType: 'circle', color: '#e74c3c', hint: 'hint' },
        { index: 3, key: 'roads', label: 'Roads', drawType: 'polyline', color: '#3498db', hint: 'hint' },
        { index: 4, key: 'houseEntrances', label: 'Entrances', drawType: 'marker', color: '#27ae60', hint: 'hint' },
    ],
}))

import { store, featureLayers, syncCounts, openModal, openEditModal, resolveModal } from './store'
import type { FeatureData } from './types'

describe('store', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        // Reset store state
        store.currentPhase = 0
        store.loadError = false
        store.isLoading = false
        store.modal.visible = false
    })

    describe('syncCounts', () => {
        it('counts features correctly from featureLayers', () => {
            // Setup test data
            featureLayers.areas = [
                { id: '1', dbId: 'uuid-1', data: { type: 'areas' } as FeatureData, type: 'polygon' },
                { id: '2', dbId: 'uuid-2', data: { type: 'areas' } as FeatureData, type: 'polygon' },
            ]
            featureLayers.roads = [{ id: '3', dbId: 'uuid-3', data: { type: 'roads' } as FeatureData, type: 'line' }]
            featureLayers.houseEntrances = [
                {
                    id: '4',
                    dbId: 'uuid-4',
                    data: { type: 'houseEntrances', entranceTypeKey: 'main_entrance' } as FeatureData,
                    type: 'marker',
                },
                {
                    id: '5',
                    dbId: 'uuid-5',
                    data: { type: 'houseEntrances', entranceTypeKey: 'secondary_entrance' } as FeatureData,
                    type: 'marker',
                },
            ]

            syncCounts()

            expect(store.counts.areas).toBe(2)
            expect(store.counts.roads).toBe(1)
            expect(store.counts.mainEntrances).toBe(1)
            expect(store.counts.secondaryEntrances).toBe(1)
        })

        it('handles empty featureLayers', () => {
            featureLayers.areas = []
            featureLayers.roads = []
            featureLayers.houseEntrances = []

            syncCounts()

            expect(store.counts.areas).toBe(0)
            expect(store.counts.roads).toBe(0)
        })
    })

    describe('openModal', () => {
        it('opens modal with correct phase data', async () => {
            const promise = openModal(0, 'test-feature')

            expect(store.modal.visible).toBe(true)
            expect(store.modal.phaseIndex).toBe(0)
            expect(store.modal.isEdit).toBe(false)

            // Resolve the promise
            resolveModal({ label: 'Test', decisionNumber: '123', decisionDate: '2024-01-01' })

            const result = await promise
            expect(result).toEqual({ label: 'Test', decisionNumber: '123', decisionDate: '2024-01-01' })
        })

        it('handles cancel (null result)', async () => {
            const promise = openModal(0, 'test-feature')

            resolveModal(null)

            const result = await promise
            expect(result).toBeNull()
        })

        it('sets city center label for cityCenter phase', async () => {
            openModal(2, 'test-feature')

            // Mock i18n t() returns the key itself, not the translated value
            expect(store.modal.label).toBe('phase_cityCenter_label')
        })
    })

    describe('openEditModal', () => {
        it('opens modal in edit mode with existing data', async () => {
            const existingData: FeatureData = {
                type: 'areas',
                label: 'Existing Area',
                decisionNumber: '2023/001',
                decisionDate: '2023-06-15',
                areaTypeKey: 'central_urban',
            }

            const promise = openEditModal(0, 'test-uuid-123', existingData)

            expect(store.modal.visible).toBe(true)
            expect(store.modal.isEdit).toBe(true)
            expect(store.modal.editDbId).toBe('test-uuid-123')
            expect(store.modal.label).toBe('Existing Area')
            expect(store.modal.decisionNumber).toBe('2023/001')

            // Resolve
            resolveModal({ label: 'Updated', decisionNumber: '2024/002', decisionDate: '2024-01-01' })

            const result = await promise
            expect(result?.label).toBe('Updated')
        })
    })

    describe('resolveModal', () => {
        it('closes modal and clears visible state', async () => {
            // Open a modal first so resolveModal has something to close
            const promise = openModal(0, 'test-feature')
            resolveModal(null)
            const result = await promise

            expect(store.modal.visible).toBe(false)
            expect(result).toBeNull()
        })
    })
})

describe('featureLayers', () => {
    it('initializes with empty arrays for all feature types', () => {
        expect(featureLayers.areas).toEqual([])
        expect(featureLayers.districts).toEqual([])
        expect(featureLayers.roads).toEqual([])
        expect(featureLayers.houseEntrances).toEqual([])
        expect(featureLayers.publicBuildings).toEqual([])
        expect(featureLayers.publicSpaces).toEqual([])
    })
})
