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
import { t }   from '../i18n'
import { ctx } from '../map/state'

declare const L: typeof import('leaflet')

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
    z-index: 1100;
}

.tile-toggle {
    width: 36px;
    height: 36px;
    border-radius: 8px;
    border: 1px solid rgba(255, 255, 255, 0.2);
    background: rgba(15, 25, 50, 0.92);
    color: #fff;
    font-size: 16px;
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    backdrop-filter: blur(8px);
    -webkit-backdrop-filter: blur(8px);
    transition: background 0.15s;
}

.tile-icon {
    width: 22px;
    height: 22px;
    object-fit: contain;
    /* Invert to white for dark mode */
    filter: invert(1);
}
.tile-toggle:hover {
    background: rgba(255, 255, 255, 0.15);
}

.tile-dropdown {
    position: absolute;
    bottom: 42px;
    right: 0;
    background: rgba(15, 25, 50, 0.95);
    border: 1px solid rgba(255, 255, 255, 0.2);
    border-radius: 8px;
    overflow: hidden;
    min-width: 150px;
    backdrop-filter: blur(12px);
    -webkit-backdrop-filter: blur(12px);
    box-shadow: 0 4px 20px rgba(0,0,0,0.4);
}

.tile-item {
    padding: 9px 14px;
    font-size: 13px;
    color: rgba(255,255,255,0.8);
    cursor: pointer;
    display: flex;
    align-items: center;
    gap: 8px;
    transition: background 0.15s;
    border-bottom: 1px solid rgba(255,255,255,0.06);
}
.tile-item:last-child { border-bottom: none; }
.tile-item:hover      { background: rgba(255,255,255,0.08); }
.tile-item.active     { color: #fff; font-weight: 600; background: rgba(255,255,255,0.06); }

.tile-dot {
    width: 10px;
    height: 10px;
    border-radius: 50%;
    flex-shrink: 0;
}

/* Light mode */
:global([data-theme="light"]) .tile-toggle {
    background: rgba(240, 244, 255, 0.97);
    border-color: rgba(0,0,0,0.15);
    color: #1a1a2e;
}
:global([data-theme="light"]) .tile-icon {
    filter: none;  /* already black, no invert needed */
}
:global([data-theme="light"]) .tile-toggle:hover {
    background: rgba(210, 220, 245, 0.99);
}
:global([data-theme="light"]) .tile-dropdown {
    background: rgba(240, 244, 255, 0.98);
    border-color: rgba(0,0,0,0.12);
    box-shadow: 0 4px 20px rgba(0,0,0,0.12);
}
:global([data-theme="light"]) .tile-item {
    color: #1a1a2e;
    border-bottom-color: rgba(0,0,0,0.06);
}
:global([data-theme="light"]) .tile-item:hover     { background: rgba(0,0,0,0.04); }
:global([data-theme="light"]) .tile-item.active    { background: rgba(0,0,0,0.06); }
</style>
