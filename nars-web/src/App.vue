<template>
  <ProfileMenu />
  <EditSaveButton />
  <ContextMenu />

  <template v-if="isAdminUser">
    <router-view />
  </template>

  <template v-else-if="isFieldWorker">
    <FieldPanel />
  </template>

  <template v-else>
    <PhaseBar />
    <InfoPanel />
  </template>

  <TileControl />
  <FeatureModal />
  <ToastContainer />

  <div v-if="appStore.loadError" class="load-error-banner">
    <span>⚠ Could not load saved features. Check your connection and refresh the page.</span>
    <button class="load-error-dismiss" @click="appStore.loadError = false">✕</button>
  </div>

  <Teleport to="body">
    <div v-if="appStore.isLoading" class="loading-overlay">
      <div class="loading-spinner">
        <div class="spinner" />
        <p>Loading map data…</p>
      </div>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { computed, onUnmounted } from "vue"
import PhaseBar from "./components/PhaseBar.vue"
import InfoPanel from "./components/InfoPanel.vue"
import ProfileMenu from "./components/ProfileMenu.vue"
import TileControl from "./components/TileControl.vue"
import FeatureModal from "./components/FeatureModal.vue"
import FieldPanel from "./components/FieldPanel.vue"
import EditSaveButton from "./components/EditSaveButton.vue"
import ContextMenu from "./components/ContextMenu.vue"
import ToastContainer from "./components/ToastContainer.vue"
import { useAppStore } from "./stores/appStore"
import { destroyMap } from "./map"

const appStore = useAppStore()

const isAdminUser = computed(() => appStore.isAdminUser)
const isFieldWorker = computed(() => appStore.user?.role === "field_worker")

onUnmounted(() => {
  destroyMap()
})
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
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.4);
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
.load-error-dismiss:hover {
  color: #fff;
}

.loading-overlay {
  position: fixed;
  inset: 0;
  z-index: 9999;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(0, 0, 0, 0.4);
  backdrop-filter: blur(2px);
}
.loading-spinner {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
  padding: 32px 48px;
  background: #fff;
  border-radius: 12px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.2);
}
.loading-spinner p {
  margin: 0;
  font-size: 14px;
  color: #374151;
  font-weight: 500;
}
.spinner {
  width: 40px;
  height: 40px;
  border: 3px solid #e5e7eb;
  border-top-color: #3b82f6;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}
@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
