<template>
    <div class="info-panel">
        <div class="info-title">Map Features</div>
        <div class="info-item">⬟ Areas: <span>{{ c.areas }}</span></div>
        <div class="info-item">📍 City Center: <span>{{ cityCenterStatus }}</span></div>
        <div class="info-item">🏘️ Districts: <span>{{ c.districts }}</span></div>
        <div class="info-item">🛣️ Roads: <span>{{ c.roads }}</span></div>
        <div class="info-item">🚪 Main Entrances: <span>{{ c.mainEntrances }}</span></div>
        <div class="info-item">🔢 Secondary Entrances: <span>{{ c.secondaryEntrances }}</span></div>
        <div class="info-item">🏛️ Public Buildings: <span>{{ c.publicBuildings }}</span></div>
        <div class="info-item">🌳 Public Spaces: <span>{{ c.publicSpaces }}</span></div>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { store }    from '../store'

const c = computed(() => store.counts)

const cityCenterStatus = computed(() => {
    if (c.value.cityCenter > 0)           return 'Placed'
    if (store.cityCenterMode === 'auto')  return 'Skipped'
    return '—'
})
</script>
