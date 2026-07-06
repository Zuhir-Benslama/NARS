<template>
  <Teleport to="body">
    <div id="nars-toast-container">
      <TransitionGroup name="toast">
        <div
          v-for="t in store.toasts"
          :key="t.id"
          class="nars-toast"
          :style="{ background: toastBg(t.type) }"
          @click="store.removeToast(t.id)"
        >
          {{ t.message }}
        </div>
      </TransitionGroup>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { useToastStore } from "../stores/toastStore"
import { UI_CONFIG } from "../config"

const store = useToastStore()

function toastBg(type: string): string {
  return UI_CONFIG.toastColors[type as keyof typeof UI_CONFIG.toastColors] ?? "#3b82f6"
}
</script>

<style scoped>
#nars-toast-container {
  position: fixed;
  bottom: 24px;
  right: 24px;
  z-index: 9999;
  display: flex;
  flex-direction: column;
  gap: 8px;
  pointer-events: none;
}

.nars-toast {
  color: #fff;
  padding: 10px 18px;
  border-radius: 8px;
  font-size: 14px;
  line-height: 1.4;
  max-width: 340px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.25);
  pointer-events: auto;
  cursor: default;
}

.toast-enter-active {
  transition:
    opacity 0.2s,
    transform 0.2s;
}
.toast-leave-active {
  transition:
    opacity 0.2s,
    transform 0.2s;
}
.toast-enter-from {
  opacity: 0;
  transform: translateY(8px);
}
.toast-leave-to {
  opacity: 0;
  transform: translateY(8px);
}
</style>
