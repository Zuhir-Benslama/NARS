<template>
    <PhaseBar />
    <InfoPanel />
    <ProfileMenu />
    <TileControl />
    <FeatureModal />

    <!-- Load-error banner — shown when /api/load fails at startup -->
    <div v-if="store.loadError" class="load-error-banner">
        <span>⚠ Could not load saved features. Check your connection and refresh the page.</span>
        <button @click="store.loadError = false" class="load-error-dismiss">✕</button>
    </div>
</template>

<script setup lang="ts">
import PhaseBar         from './components/PhaseBar.vue'
import InfoPanel        from './components/InfoPanel.vue'
import ProfileMenu      from './components/ProfileMenu.vue'
import TileControl      from './components/TileControl.vue'
import FeatureModal     from './components/FeatureModal.vue'
import { store }        from './store'
</script>

<style scoped>
.load-error-banner {
    position: fixed;
    bottom: 60px;
    left: 50%;
    transform: translateX(-50%);
    z-index: 2000;
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 10px 16px;
    background: #7f1d1d;
    color: #fecaca;
    border: 1px solid #991b1b;
    border-radius: 8px;
    font-size: 13px;
    box-shadow: 0 4px 20px rgba(0,0,0,0.4);
    max-width: 480px;
}
.load-error-dismiss {
    background: none;
    border: none;
    color: #fecaca;
    cursor: pointer;
    font-size: 14px;
    padding: 0 2px;
    line-height: 1;
    flex-shrink: 0;
}
.load-error-dismiss:hover { color: #fff; }
</style>
