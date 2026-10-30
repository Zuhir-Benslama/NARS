<template>
  <div v-if="visible" class="edit-toolbar" role="toolbar" aria-label="Map tools">
    <button
      class="edit-toolbar-btn"
      :title="t('tool_hand')"
      :aria-label="t('tool_hand')"
      @click="hand"
    >
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
      >
        <path d="M18 11V6a2 2 0 0 0-2-2 2 2 0 0 0-2 2v0" />
        <path d="M14 10V4a2 2 0 0 0-2-2 2 2 0 0 0-2 2v2" />
        <path d="M10 10.5V6a2 2 0 0 0-2-2 2 2 0 0 0-2 2v8" />
        <path
          d="M18 8a2 2 0 1 1 4 0v6a8 8 0 0 1-8 8h-2c-2.8 0-4.5-.86-5.99-2.34l-3.6-3.6a2 2 0 0 1 2.83-2.82L7 15"
        />
      </svg>
    </button>
    <button
      class="edit-toolbar-btn"
      :title="t('tool_draw')"
      :aria-label="t('tool_draw')"
      @click="draw"
    >
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
      >
        <path d="M12 19l7-7 3 3-7 7-3-3z" />
        <path d="M18 13l-1.5-7.5L2 2l3.5 14.5L13 18l5-5z" />
        <path d="M2 2l7.586 7.586" />
        <circle cx="11" cy="11" r="2" />
      </svg>
    </button>
    <button
      class="edit-toolbar-btn"
      :title="t('tool_edit')"
      :aria-label="t('tool_edit')"
      :disabled="!selectedDbId"
      @click="edit"
    >
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
      >
        <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
        <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1-1-4 9.5-9.5z" />
      </svg>
    </button>
    <button
      class="edit-toolbar-btn edit-toolbar-btn-danger"
      :title="t('tool_remove')"
      :aria-label="t('tool_remove')"
      :disabled="!selectedDbId"
      @click="remove"
    >
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
      >
        <polyline points="3 6 5 6 21 6" />
        <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" />
        <path d="M10 11v6" />
        <path d="M14 11v6" />
        <path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" />
      </svg>
    </button>
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue"
import { useI18n } from "vue-i18n"
import { useAppStore } from "../stores/appStore"
import { useEditStore } from "../stores/editStore"
import { useSelectionStore } from "../stores/selectionStore"
import { ensureGeoman } from "../map/map-init"
import { buildDrawControl } from "../map/draw/draw-control"
import { getCtx } from "../map/core/state"
import { enableCrosshair, disableCrosshair } from "../map/snapping/snapping"
import { PHASES } from "../phases"
import { enableEditGeometry } from "../map/context-menu/ctx-menu-actions"
import { removeFeature } from "../map/context-menu/ctx-menu-actions"

const { t } = useI18n()
const appStore = useAppStore()
const editStore = useEditStore()
const selectionStore = useSelectionStore()

const visible = computed(() => !editStore.isEditMode)
const selectedDbId = computed(() => selectionStore.selectedFeatureDbId)

function hand() {
  const gm = getCtx().geoman
  if (gm) {
    gm.disableDraw?.().catch(() => {})
  }
  disableCrosshair()
}

function draw() {
  const phase = PHASES[appStore.currentPhase]
  if (!phase) return
  ensureGeoman()
    .then(() => {
      buildDrawControl({ key: phase.key, drawType: phase.drawType, color: phase.color })
      enableCrosshair()
    })
    .catch((err) => console.error("[TOOLBAR] draw:", err))
}

function edit() {
  const dbId = selectedDbId.value
  if (!dbId) return
  enableEditGeometry(dbId)
}

function remove() {
  const dbId = selectedDbId.value
  if (!dbId) return
  removeFeature(dbId)
}
</script>

<style scoped>
.edit-toolbar {
  position: fixed;
  top: 50%;
  right: 10px;
  transform: translateY(-50%);
  z-index: 800;
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 8px 6px;
  background: var(--glass-bg) !important;
  backdrop-filter: var(--glass-blur) !important;
  -webkit-backdrop-filter: var(--glass-blur) !important;
  border: 1px solid var(--glass-border) !important;
  box-shadow: var(--glass-shadow) !important;
  border-radius: 10px;
}
.edit-toolbar-btn {
  width: 36px;
  height: 36px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--glass-border) !important;
  border-radius: 8px;
  background: var(--btn-glass-bg) !important;
  color: var(--text-primary) !important;
  cursor: pointer;
  transition:
    background 0.15s,
    transform 0.1s;
  backdrop-filter: var(--glass-blur) !important;
  -webkit-backdrop-filter: var(--glass-blur) !important;
}
.edit-toolbar-btn:hover:not(:disabled) {
  background: var(--btn-glass-hover) !important;
  transform: translateY(-1px);
}
.edit-toolbar-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}
.edit-toolbar-btn-danger:hover:not(:disabled) {
  background: var(--danger-bg) !important;
  color: var(--danger-color) !important;
}
</style>
