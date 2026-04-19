<template>
    <div class="info-panel">
        <div class="info-title">
            {{ t('info_title') }}
        </div>
        <div class="info-item">
            ⬟ {{ t('info_areas') }}:
            <span>{{ c.areas }}</span>
        </div>
        <div class="info-item">
            📍 {{ t('info_city_center') }}:
            <span>{{ cityCenterStatus }}</span>
        </div>
        <div class="info-item">
            🏘️ {{ t('info_districts') }}:
            <span>{{ c.districts }}</span>
        </div>
        <div class="info-item">
            🛣️ {{ t('info_roads') }}:
            <span>{{ c.roads }}</span>
        </div>
        <div class="info-item">
            🚪 {{ t('info_main_entrances') }}:
            <span>{{ c.mainEntrances }}</span>
        </div>
        <div class="info-item">
            🔢 {{ t('info_secondary_entrances') }}:
            <span>{{ c.secondaryEntrances }}</span>
        </div>
        <div class="info-item">
            🏛️ {{ t('info_public_buildings') }}:
            <span>{{ c.publicBuildings }}</span>
        </div>
        <div class="info-item">
            🌳 {{ t('info_public_spaces') }}:
            <span>{{ c.publicSpaces }}</span>
        </div>
    </div>
</template>

<script setup lang="ts">
    import { computed } from 'vue'
    import { useI18n } from 'vue-i18n'
    import { store } from '../store'

    const { t } = useI18n()

    const c = computed(() => store.counts)

    const cityCenterStatus = computed(() => {
        if (c.value.cityCenter > 0) return t('info_status_placed')
        if (store.cityCenterMode === 'auto') return t('info_status_skipped')
        return '—'
    })
</script>
