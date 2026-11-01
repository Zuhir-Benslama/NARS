<template>
  <div v-click-outside="handleClickOutside" class="tile-control">
    <button
      class="tools-toggle"
      :title="t('map_tools')"
      :aria-expanded="open"
      aria-haspopup="true"
      @click="toggleTools"
    >
      ▼
    </button>
    <div v-if="open" class="tools-panel">
      <template v-if="!isEditMode">
        <button class="tool-btn" :title="t('tool_hand')" :aria-label="t('tool_hand')" @click="hand">
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
        <button class="tool-btn" :title="t('tool_draw')" :aria-label="t('tool_draw')" @click="draw">
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
          class="tool-btn"
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
          class="tool-btn tool-btn-danger"
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
      </template>
      <button
        class="tool-btn"
        :title="t('rotate_ccw')"
        :aria-label="t('rotate_ccw')"
        @click="rotateCcw"
      >
        ↺
      </button>
      <button
        class="tool-btn"
        :title="t('rotate_cw')"
        :aria-label="t('rotate_cw')"
        @click="rotateCw"
      >
        ↻
      </button>
      <button
        class="tool-btn"
        :title="snapOn ? t('map_disable_snapping') : t('map_enable_snapping')"
        :aria-label="snapOn ? t('map_disable_snapping') : t('map_enable_snapping')"
        @click="toggleSnap"
      >
        {{ snapOn ? "⊘" : "⊕" }}
      </button>
      <button
        class="tile-toggle"
        :title="t('tile_layers')"
        :aria-expanded="layersOpen"
        aria-haspopup="listbox"
        @click="toggleLayers"
      >
        <img src="/tiles.svg" class="tile-icon" alt="layers" />
      </button>
      <div
        v-if="layersOpen"
        ref="dropdownRef"
        class="tile-dropdown"
        role="listbox"
        :aria-label="t('tile_layers')"
        @keydown.escape="layersOpen = false"
        @keydown.up.prevent="moveFocus(-1)"
        @keydown.down.prevent="moveFocus(1)"
      >
        <div
          v-for="(layer, idx) in layers"
          :key="layer.key"
          :ref="
            (el) => {
              if (el) optionRefs[idx] = el as HTMLElement
            }
          "
          :class="['tile-item', { active: activeKey === layer.key }]"
          role="option"
          :tabindex="0"
          :aria-selected="activeKey === layer.key"
          @click="select(layer.key)"
          @keydown.enter="select(layer.key)"
        >
          <span class="tile-dot" :style="{ background: layer.color }" />
          {{ t(layer.labelKey) }}
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, nextTick, computed } from "vue"
import { useI18n } from "vue-i18n"
import { setBaseLayer } from "../map/index"
import { rotateCcw as rotateCcwFn, rotateCw as rotateCwFn } from "../map/rotation"
import { ensureGeoman } from "../map/map-init"
import { buildDrawControl } from "../map/draw/draw-control"
import { getCtx } from "../map/core/state"
import { enableCrosshair, disableCrosshair, toggleSnapping } from "../map/snapping/snapping"
import { isSnappingEnabled } from "../map/draw/draw-state"
import { PHASES } from "../phases"
import { enableEditGeometry, removeFeature } from "../map/context-menu/ctx-menu-actions"

import { useAppStore } from "../stores/appStore"
import { showToast } from "../lib/toast"
import { useEditStore } from "../stores/editStore"
import { useSelectionStore } from "../stores/selectionStore"

let appStore: ReturnType<typeof useAppStore> | null = null
let editStore: ReturnType<typeof useEditStore> | null = null
let selectionStore: ReturnType<typeof useSelectionStore> | null = null
try {
  appStore = useAppStore()
} catch {}
try {
  editStore = useEditStore()
} catch {}
try {
  selectionStore = useSelectionStore()
} catch {}

const { t } = useI18n()
const open = ref(false)
const layersOpen = ref(false)
const activeKey = ref("satellite")
const optionRefs = ref<HTMLElement[]>([])

const isEditMode = computed(() => editStore?.isEditMode ?? false)
const selectedDbId = computed(() => selectionStore?.selectedFeatureDbId ?? null)
const snapOn = computed(() => isSnappingEnabled())

const layers = [
  { key: "satellite", labelKey: "layer_satellite", color: "#e67e22" },
  { key: "street", labelKey: "layer_street", color: "#3498db" },
  { key: "light", labelKey: "layer_light", color: "#ecf0f1" },
  { key: "dark", labelKey: "layer_dark", color: "#2c3e50" },
]

function handleClickOutside() {
  layersOpen.value = false
}

function toggleTools() {
  open.value = !open.value
}

function toggleLayers() {
  layersOpen.value = !layersOpen.value
  if (layersOpen.value) {
    nextTick(() => {
      const activeIdx = layers.findIndex((l) => l.key === activeKey.value)
      optionRefs.value[activeIdx]?.focus()
    })
  }
}

function moveFocus(delta: number) {
  const current = document.activeElement
  const idx = optionRefs.value.indexOf(current as HTMLElement)
  const next = (idx + delta + layers.length) % layers.length
  optionRefs.value[next]?.focus()
}

function select(key: string) {
  if (key === activeKey.value) {
    layersOpen.value = false
    return
  }
  activeKey.value = key
  layersOpen.value = false
  setBaseLayer(key)
}

function rotateCcw() {
  rotateCcwFn()
}

function rotateCw() {
  rotateCwFn()
}

function hand() {
  const gm = getCtx().geoman
  if (gm) {
    gm.disableDraw?.().catch(() => {})
  }
  disableCrosshair()
}

function draw() {
  const phase = PHASES[appStore?.currentPhase ?? 0]
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

function toggleSnap() {
  const on = toggleSnapping()
  showToast(t(on ? "map_snapping_enabled" : "map_snapping_disabled"), "info")
}
</script>

<style scoped>
.tile-control {
  position: fixed;
  top: 50%;
  right: 10px;
  transform: translateY(-50%);
  z-index: 1050;
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

.tools-toggle {
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
  font-size: 16px;
  line-height: 1;
  transition:
    background 0.15s,
    transform 0.1s;
  backdrop-filter: var(--glass-blur) !important;
  -webkit-backdrop-filter: var(--glass-blur) !important;
}
.tools-toggle:hover {
  background: var(--btn-glass-hover) !important;
  transform: translateY(-1px);
}

.tools-panel {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.tool-btn {
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
  font-size: 18px;
  line-height: 1;
}
.tool-btn:hover:not(:disabled) {
  background: var(--btn-glass-hover) !important;
  transform: translateY(-1px);
}
.tool-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}
.tool-btn-danger:hover:not(:disabled) {
  background: var(--danger-bg) !important;
  color: var(--danger-color) !important;
}

.tile-toggle {
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
.tile-toggle:hover {
  background: var(--btn-glass-hover) !important;
  transform: translateY(-1px);
}

.tile-icon {
  width: 22px;
  height: 22px;
  object-fit: contain;
  filter: invert(1);
}

:global([data-theme="light"]) .tile-icon {
  filter: none;
}

.tile-dropdown {
  position: absolute;
  bottom: calc(100% + 8px);
  right: 0;
  background: var(--dropdown-bg);
  border: 1px solid var(--dropdown-border);
  border-radius: 8px;
  overflow: hidden;
  min-width: 150px;
  backdrop-filter: var(--dropdown-bg-blur);
  -webkit-backdrop-filter: var(--dropdown-bg-blur);
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.3);
}

.tile-item {
  padding: 9px 14px;
  font-size: 13px;
  color: var(--dropdown-item);
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 8px;
  transition: background 0.15s;
  border-bottom: 1px solid var(--glass-border);
}
.tile-item:last-child {
  border-bottom: none;
}
.tile-item:hover {
  background: var(--dropdown-hover);
}
.tile-item.active {
  color: var(--text-primary);
  font-weight: 600;
}

.tile-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  flex-shrink: 0;
}
</style>
