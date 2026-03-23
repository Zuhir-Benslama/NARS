<template>
    <div class="about-panel">
        <h3>{{ t('about_nars') }}</h3>
        <p>{{ t('about_nars_desc') }}</p>
        <p class="version">{{ t('about_version') }}</p>
        <hr />
        <p>{{ t('about_body') }}</p>
        <p><small>{{ t('about_copyright') }}</small></p>
    </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
</script>
