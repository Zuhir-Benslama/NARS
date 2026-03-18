<template>
    <div class="tile-control" v-click-outside="close">
        <button class="tile-toggle" @click="toggle" :title="t('tile_layers')">
            <img src="/tiles.svg" class="tile-icon" alt="layers" />
        </button>
        <div v-if="open" class="tile-dropdown">
            <div
                v-for="layer in layers"
                :key="layer.key"
                :class="['tile-item', { active: activeKey === layer.key }]"
                @click="select(layer.key)"
            >
                <span class="tile-dot" :style="{ background: layer.color }"></span>
                {{ t(layer.labelKey) }}
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ctx } from '../map/state'

declare const L: typeof import('leaflet')

const { t } = useI18n()
const open      = ref(false)
const activeKey = ref('satellite')

const layers = [
    { key: 'satellite', labelKey: 'layer_satellite', color: '#e67e22' },
    { key: 'street',    labelKey: 'layer_street',    color: '#3498db' },
    { key: 'light',     labelKey: 'layer_light',     color: '#ecf0f1' },
    { key: 'dark',      labelKey: 'layer_dark',      color: '#2c3e50' },
]

function toggle() { open.value = !open.value }
function close()  { open.value = false }

function select(key: string) {
    if (key === activeKey.value) { close(); return }
    activeKey.value = key
    close()
    // Signal index.ts to swap the active base layer
    ;(window as any).__narsSetBaseLayer?.(key)
}
</script>

<style scoped>
.tile-control {
    position: fixed;
    bottom: 10px;
    right: 10px;
    z-index: 1050;
}

.tile-toggle {
    width: 36px;
    height: 36px;
    border-radius: 8px;
    border: 1px solid var(--glass-border);
    background: var(--glass-bg);
    color: var(--text-primary);
    font-size: 16px;
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    backdrop-filter: var(--glass-blur);
    -webkit-backdrop-filter: var(--glass-blur);
    transition: background 0.15s;
}
.tile-toggle:hover { background: var(--glass-bg-hover); }

.tile-icon {
    width: 22px;
    height: 22px;
    object-fit: contain;
    filter: invert(1);
}

:global([data-theme="light"]) .tile-icon { filter: none; }

.tile-dropdown {
    position: absolute;
    bottom: 42px;
    right: 0;
    background: var(--dropdown-bg);
    border: 1px solid var(--dropdown-border);
    border-radius: 8px;
    overflow: hidden;
    min-width: 150px;
    backdrop-filter: var(--dropdown-bg-blur);
    -webkit-backdrop-filter: var(--dropdown-bg-blur);
    box-shadow: 0 4px 20px rgba(0,0,0,0.3);
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
.tile-item:last-child { border-bottom: none; }
.tile-item:hover      { background: var(--dropdown-hover); }
.tile-item.active     { color: var(--text-primary); font-weight: 600; }

.tile-dot {
    width: 10px;
    height: 10px;
    border-radius: 50%;
    flex-shrink: 0;
}
</style>
