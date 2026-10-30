<template>
  <div v-if="visible" class="edit-panel" role="toolbar" aria-label="Edit geometry">
    <div class="edit-panel-title">{{ t("edit_panel_title") }}</div>
    <p class="edit-panel-hint">{{ t("map_edit_mode_hint") }}</p>

    <div class="edit-panel-actions">
      <button class="edit-panel-btn edit-panel-btn-save" @click="save">
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
          <polyline points="20 6 9 17 4 12" />
        </svg>
        {{ t("btn_save_geometry") }}
      </button>
      <button class="edit-panel-btn edit-panel-btn-cancel" @click="cancel">
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
          <line x1="18" y1="6" x2="6" y2="18" />
          <line x1="6" y1="6" x2="18" y2="18" />
        </svg>
        {{ t("btn_cancel") }}
      </button>
    </div>

    <button class="edit-panel-snap" @click="toggleSnap">
      {{ snapOn ? t("map_disable_snapping") : t("map_enable_snapping") }}
    </button>
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue"
import { useI18n } from "vue-i18n"
import { useEditStore } from "../stores/editStore"
import { commitEditMode, cancelEditMode } from "../map/edit/edit-mode"
import { showToast } from "../lib/toast"
import { getUserMessageKey } from "../lib/errors"
import { toggleSnapping } from "../map/snapping/snapping"
import { isSnappingEnabled } from "../map/draw/draw-state"

const { t } = useI18n()
const store = useEditStore()
const visible = computed(() => store.isEditMode)
const snapOn = computed(() => isSnappingEnabled())

function save() {
  commitEditMode().catch((err) => {
    showToast(t("map_save_failed", { error: t(getUserMessageKey(err)) }), "error")
  })
}

function cancel() {
  cancelEditMode().catch((err) => {
    showToast(t("map_save_failed", { error: t(getUserMessageKey(err)) }), "error")
  })
}

function toggleSnap() {
  const on = toggleSnapping()
  showToast(t(on ? "map_snapping_enabled" : "map_snapping_disabled"), "info")
}
</script>

<style scoped>
.edit-panel {
  position: absolute;
  top: 60px;
  right: 10px;
  z-index: 1500;
  background: var(--glass-bg) !important;
  backdrop-filter: var(--glass-blur) !important;
  -webkit-backdrop-filter: var(--glass-blur) !important;
  border: 1px solid var(--glass-border) !important;
  box-shadow: var(--glass-shadow) !important;
  color: var(--text-secondary) !important;
  padding: 14px 16px;
  border-radius: 10px;
  min-width: 220px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.edit-panel-title {
  font-weight: 700;
  color: var(--text-primary) !important;
  font-size: 13px;
}
.edit-panel-hint {
  font-size: 11px;
  color: var(--text-muted) !important;
  margin: 0;
  line-height: 1.4;
}
.edit-panel-actions {
  display: flex;
  gap: 8px;
}
.edit-panel-btn {
  flex: 1;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 8px 10px;
  border: 1px solid var(--glass-border) !important;
  border-radius: 8px;
  font-size: 12px;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.15s;
  background: var(--btn-glass-bg) !important;
  color: var(--text-primary) !important;
  backdrop-filter: var(--glass-blur) !important;
  -webkit-backdrop-filter: var(--glass-blur) !important;
}
.edit-panel-btn:hover {
  background: var(--btn-glass-hover) !important;
}
.edit-panel-btn-save {
  background: rgba(39, 174, 96, 0.75) !important;
  border-color: var(--success-color) !important;
}
.edit-panel-btn-save:hover {
  background: var(--success-color) !important;
}
.edit-panel-btn-cancel {
  color: var(--danger-color) !important;
}
.edit-panel-snap {
  padding: 6px 10px;
  border: 1px solid var(--glass-border) !important;
  border-radius: 8px;
  font-size: 12px;
  cursor: pointer;
  background: var(--btn-glass-bg) !important;
  color: var(--text-primary) !important;
  backdrop-filter: var(--glass-blur) !important;
  -webkit-backdrop-filter: var(--glass-blur) !important;
  transition: background 0.15s;
}
.edit-panel-snap:hover {
  background: var(--btn-glass-hover) !important;
}
</style>
