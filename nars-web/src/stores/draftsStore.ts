// ─── DRAFTS STORE ──────────────────────────────────────────────────────────────
// Pinia store for the AI draft review queue. Loads pending drafts from
// /api/draft-features, renders them into the "drafts" GeoJSON source using the
// dashed draft style, and tracks which draft is being geometry-edited so the
// shared edit-commit path can PUT the change to the draft endpoint instead of
// /api/features.

import { defineStore } from "pinia"
import { tryGetCtx } from "../map/core/state"
import { debugWarn, debugLog } from "../utils/debug"
import { listDrafts, type AiDraftFeatureDto, type DraftFeatureType } from "../api/drafts"
import { parseDraftGeometry } from "../map/drafts/draft-style"

export interface DraftMapFeature {
  id: string
  geometry: GeoJSON.LineString | GeoJSON.Polygon
  properties: {
    draftId: string
    featureType: DraftFeatureType
    confidence: number
    geomType: "LineString" | "Polygon"
    lineColor: string
    lineWidth: number
    fillColor?: string
    fillOpacity?: number
  }
}

export const useDraftsStore = defineStore("drafts", {
  state: () => ({
    drafts: [] as AiDraftFeatureDto[],
    features: [] as DraftMapFeature[],
    loading: false,
    selectedDraftId: null as string | null,
    activeDraftEditId: null as string | null,
  }),

  getters: {
    pending(): AiDraftFeatureDto[] {
      return this.drafts
    },
    selectedDraft(): AiDraftFeatureDto | null {
      return this.drafts.find((d) => d.id === this.selectedDraftId) ?? null
    },
  },

  actions: {
    async load(communeId: number | null): Promise<void> {
      this.loading = true
      try {
        const drafts = await listDrafts({ communeId, status: "pending" })
        this.drafts = drafts
        this.features = drafts
          .map((d) => this.toMapFeature(d))
          .filter((f): f is DraftMapFeature => f !== null)
        this.updateSource()
        debugLog("[DRAFTS] loaded", drafts.length, "pending drafts")
      } catch (err) {
        debugWarn("[DRAFTS] load failed:", err)
      } finally {
        this.loading = false
      }
    },

    toMapFeature(draft: AiDraftFeatureDto): DraftMapFeature | null {
      const feature = parseDraftGeometry(
        draft.id,
        draft.featureType as DraftFeatureType,
        draft.geometryGeoJson,
        Number(draft.confidence),
      )
      if (!feature) return null
      return {
        id: `draft_${draft.id}`,
        geometry: feature.geometry as GeoJSON.LineString | GeoJSON.Polygon,
        properties: {
          draftId: draft.id,
          featureType: draft.featureType as DraftFeatureType,
          confidence: Number(draft.confidence),
          geomType: feature.geometry.type,
          lineColor: feature.properties.lineColor as string,
          lineWidth: feature.properties.lineWidth as number,
          ...(feature.properties.fillColor
            ? {
                fillColor: feature.properties.fillColor as string,
                fillOpacity: feature.properties.fillOpacity as number,
              }
            : {}),
        },
      }
    },

    replaceDraftGeometry(draftId: string, geometryGeoJson: string): void {
      const draft = this.drafts.find((d) => d.id === draftId)
      if (!draft) return
      draft.geometryGeoJson = geometryGeoJson
      const idx = this.features.findIndex((f) => f.properties.draftId === draftId)
      if (idx !== -1) {
        const mapped = this.toMapFeature(draft)
        if (mapped) this.features[idx] = mapped
        else this.features.splice(idx, 1)
      }
      this.updateSource()
    },

    removeDraft(draftId: string): void {
      this.drafts = this.drafts.filter((d) => d.id !== draftId)
      this.features = this.features.filter((f) => f.properties.draftId !== draftId)
      if (this.selectedDraftId === draftId) this.selectedDraftId = null
      if (this.activeDraftEditId === draftId) this.activeDraftEditId = null
      this.updateSource()
    },

    setSelectedDraftId(id: string | null): void {
      this.selectedDraftId = id
    },

    setActiveDraftEditId(id: string | null): void {
      this.activeDraftEditId = id
    },

    updateSource(): void {
      const ctx = tryGetCtx()
      if (!ctx?.draftsSource) {
        debugWarn("draftsStore.updateSource called but ctx.draftsSource is NOT set!")
        return
      }
      const data: GeoJSON.FeatureCollection = {
        type: "FeatureCollection",
        features: this.features.map((f) => ({
          type: "Feature" as const,
          geometry: f.geometry,
          properties: f.properties,
        })),
      }
      try {
        ctx.draftsSource.setData(data)
      } catch (err) {
        debugWarn("draftsStore.updateSource failed:", err)
      }
    },
  },
})
