<template>
  <div class="drafts-container">
    <!-- Toggle button -->
    <button
      v-if="open"
      :aria-label="t('draft_panel_title')"
      :title="t('draft_panel_title')"
      class="drafts-toggle is-open"
      @click="open = false"
    >
      {{ t("draft_panel_title") }}
      <span v-if="count > 0" class="drafts-badge">{{ count }}</span>
    </button>
    <button
      v-else
      :aria-label="t('draft_panel_title')"
      :title="t('draft_panel_title')"
      class="drafts-toggle"
      @click="open = true"
    >
      {{ t("draft_panel_title") }}
      <span v-if="draftsStore.loading" class="drafts-badge draft-spin">…</span>
      <span v-else-if="count > 0" class="drafts-badge">{{ count }}</span>
    </button>

    <Teleport to="body">
      <div v-if="open" class="drafts-panel">
        <div class="drafts-header">
          <h2 class="drafts-title">{{ t("draft_panel_title") }}</h2>
          <button class="drafts-refresh" :title="t('draft_panel_refresh')" @click="reload">
            <svg
              width="14"
              height="14"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2.5"
              stroke-linecap="round"
              stroke-linejoin="round"
            >
              <polyline points="23 4 23 10 17 10" />
              <polyline points="1 20 1 14 7 14" />
              <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" />
            </svg>
          </button>
        </div>

        <div v-if="draftsStore.loading" class="drafts-loading">
          {{ t("draft_panel_loading") }}
        </div>
        <div v-else-if="drafts.length === 0" class="drafts-empty">
          {{ t("draft_panel_empty") }}
        </div>
        <div v-else class="drafts-list">
          <div
            v-for="draft in drafts"
            :key="draft.id"
            :class="['drafts-item', { selected: draft.id === draftsStore.selectedDraftId }]"
            @click="centerOnDraft(draft)"
          >
            <div class="drafts-item-main">
              <span
                :class="[
                  'drafts-item-type',
                  draft.featureType === 'road' ? 'is-road' : 'is-building',
                ]"
              >
                {{
                  draft.featureType === "road" ? t("draft_panel_road") : t("draft_panel_building")
                }}
              </span>
              <span class="drafts-conf">{{ confidence(draft) }}%</span>
            </div>
            <div class="drafts-actions">
              <button
                class="drafts-action is-edit"
                :title="t('draft_edit_geometry')"
                @click.stop="startDraftEdit(draft.id)"
              >
                ✎
              </button>
              <button
                class="drafts-action is-accept"
                :title="t('draft_accept')"
                @click.stop="review(draft.id, 'accept')"
              >
                ✓
              </button>
              <button
                class="drafts-action is-reject"
                :title="t('draft_reject')"
                @click.stop="review(draft.id, 'reject')"
              >
                ✕
              </button>
              <button
                class="drafts-action is-delete"
                :title="t('draft_delete')"
                @click.stop="review(draft.id, 'delete')"
              >
                🗑
              </button>
            </div>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from "vue"
import { useI18n } from "vue-i18n"
import type { AiDraftFeatureDto } from "../api/drafts"
import { useDraftsStore } from "../stores/draftsStore"
import { useAppStore } from "../stores/appStore"
import { tryGetCtx } from "../map/core/state"
import { reviewDraft, type DraftReviewAction } from "../map/drafts/review-actions"
import { startDraftEdit } from "../map/drafts/draft-edit"
import { parseDraftGeometry } from "../map/drafts/draft-style"

const { t } = useI18n()
const draftsStore = useDraftsStore()
const appStore = useAppStore()
const open = ref(false)

const drafts = computed(() => draftsStore.pending)
const count = computed(() => drafts.value.length)

function confidence(draft: AiDraftFeatureDto): number {
  return Math.round(Number(draft.confidence) * 100)
}

async function reload(): Promise<void> {
  const communeId = appStore.user?.commune?.id ?? null
  if (communeId == null) return
  await draftsStore.load(communeId)
}

function review(draftId: string, action: DraftReviewAction): void {
  void reviewDraft(draftId, action)
}

function centerOnDraft(draft: AiDraftFeatureDto): void {
  draftsStore.setSelectedDraftId(draft.id)
  const ctx = tryGetCtx()
  if (!ctx?.map) return
  const feature = parseDraftGeometry(
    draft.id,
    draft.featureType as "road" | "building",
    draft.geometryGeoJson,
    Number(draft.confidence),
  )
  const coords =
    feature?.geometry.type === "Polygon" && feature.geometry.coordinates[0]?.length
      ? feature.geometry.coordinates[0]
      : feature?.geometry.type === "LineString"
        ? feature.geometry.coordinates
        : undefined
  if (!coords?.length) return
  let minLng = Infinity,
    minLat = Infinity,
    maxLng = -Infinity,
    maxLat = -Infinity
  for (const [lng, lat] of coords) {
    if (lng < minLng) minLng = lng
    if (lng > maxLng) maxLng = lng
    if (lat < minLat) minLat = lat
    if (lat > maxLat) maxLat = lat
  }
  ctx.map.fitBounds(
    [
      [minLng, minLat],
      [maxLng, maxLat],
    ],
    { padding: 60, maxZoom: 18 },
  )
}

onMounted(() => {
  void reload()
})
</script>

<style scoped>
.drafts-container {
  position: fixed;
  bottom: 24px;
  right: 16px;
  z-index: 1200;
}
.drafts-toggle {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 8px 14px;
  border: none;
  border-radius: 8px;
  background: var(--accent-color, #3498db);
  color: #fff;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.3);
}
.drafts-toggle.is-open {
  background: var(--danger-bg-strong, #c0392b);
}
.drafts-badge {
  min-width: 18px;
  height: 18px;
  padding: 0 5px;
  border-radius: 9px;
  background: #fff;
  color: var(--accent-color, #3498db);
  font-size: 12px;
  font-weight: 700;
  display: inline-flex;
  align-items: center;
  justify-content: center;
}
.draft-spin {
  animation: draftPulse 1s ease-in-out infinite;
}
@keyframes draftPulse {
  50% {
    opacity: 0.4;
  }
}
.drafts-panel {
  position: fixed;
  right: 16px;
  bottom: 76px;
  width: 300px;
  max-height: 55vh;
  display: flex;
  flex-direction: column;
  background: var(--modal-bg, #fff);
  border: 1px solid var(--border-color, #d1d5db);
  border-radius: 12px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.25);
  overflow: hidden;
}
.drafts-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 14px;
  border-bottom: 1px solid var(--border-color, #d1d5db);
}
.drafts-title {
  margin: 0;
  font-size: 14px;
  font-weight: 700;
}
.drafts-refresh {
  background: none;
  border: none;
  cursor: pointer;
  color: var(--text-primary, #374151);
  padding: 2px;
  line-height: 0;
}
.drafts-refresh:hover {
  color: var(--accent-color, #3498db);
}
.drafts-loading,
.drafts-empty {
  padding: 18px 14px;
  font-size: 13px;
  color: var(--text-secondary, #6b7280);
}
.drafts-list {
  overflow-y: auto;
  padding: 6px;
}
.drafts-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 8px 10px;
  border-radius: 8px;
  cursor: pointer;
}
.drafts-item:hover,
.drafts-item.selected {
  background: var(--list-hover-bg, rgba(52, 152, 219, 0.12));
}
.drafts-item-main {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.drafts-item-type {
  font-size: 12px;
  font-weight: 700;
}
.drafts-item-type.is-road {
  color: #48c9f0;
}
.drafts-item-type.is-building {
  color: #c44569;
}
.drafts-conf {
  font-size: 11px;
  color: var(--text-secondary, #6b7280);
}
.drafts-actions {
  display: flex;
  gap: 4px;
}
.drafts-action {
  width: 26px;
  height: 26px;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  font-size: 13px;
  line-height: 1;
  background: var(--input-bg, #f3f4f6);
  color: var(--text-primary, #374151);
}
.drafts-action:hover {
  filter: brightness(0.92);
}
.drafts-action.is-accept {
  background: var(--success-color, #27ae60);
  color: #fff;
}
.drafts-action.is-reject,
.drafts-action.is-delete {
  background: var(--danger-bg-strong, #c0392b);
  color: #fff;
}
</style>
