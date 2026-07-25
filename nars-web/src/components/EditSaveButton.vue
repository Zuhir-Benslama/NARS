<template>
  <button
    v-if="visible"
    id="nars-edit-save"
    class="nars-edit-save-btn"
    :aria-label="t('btn_save_geometry')"
    :title="t('btn_save_geometry')"
    @click="save"
  >
    <svg
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="2.5"
      stroke-linecap="round"
      stroke-linejoin="round"
    >
      <polyline points="20 6 9 17 4 12" />
    </svg>
    {{ t('btn_save_geometry') }}
  </button>
</template>

<script setup lang="ts">
import { computed } from "vue"
import { useI18n } from "vue-i18n"
import { useEditStore } from "../stores/editStore"
import { commitEditMode } from "../map/edit/edit-mode"
import { showToast } from "../lib/toast"

const { t } = useI18n()
const store = useEditStore()
const visible = computed(() => store.isEditMode)

function save() {
  commitEditMode().catch((err) => {
    const message = err instanceof Error ? err.message : "Please try again."
    showToast("Save failed: " + message, "error")
  })
}
</script>

<style scoped>
.nars-edit-save-btn {
  position: fixed;
  bottom: 50px;
  left: 50%;
  transform: translateX(-50%);
  z-index: 10000;
  padding: 10px 24px;
  background: var(--success-color);
  color: var(--text-primary);
  border: none;
  border-radius: 8px;
  font-size: 14px;
  font-weight: 600;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 8px;
  box-shadow: 0 4px 16px rgba(39, 174, 96, 0.4);
  transition: background 0.15s;
}
.nars-edit-save-btn:hover {
  background: var(--success-hover);
}
.nars-edit-save-btn svg {
  flex-shrink: 0;
}
</style>
