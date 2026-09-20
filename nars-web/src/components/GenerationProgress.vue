<template>
  <Teleport to="body">
    <Transition name="generation">
      <div
        v-if="store.active"
        id="nars-generation-progress"
        class="generation-bar"
        role="progressbar"
        aria-label="Road generation progress"
        :aria-valuemin="0"
        :aria-valuemax="100"
        :aria-valuenow="Math.round(store.progress)"
      >
        <div class="generation-bar-fill" :style="{ width: `${store.progress}%` }" />
        <div class="generation-bar-label">
          {{ t(store.stage || "gen_roads_stage_tiles") }}
          <span class="generation-bar-percent">{{ Math.round(store.progress) }}%</span>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<script setup lang="ts">
import { onUnmounted } from "vue"
import { useGenerationStore } from "../stores/generationStore"
import { t } from "../i18n"

const store = useGenerationStore()

onUnmounted(() => store.reset())
</script>

<style scoped>
#nars-generation-progress {
  position: fixed;
  top: 0;
  left: 50%;
  transform: translateX(-50%);
  width: min(420px, 90vw);
  z-index: 9998;
  background: var(--bg-elevated, #ffffff);
  border: 1px solid var(--border, rgba(0, 0, 0, 0.15));
  border-top: none;
  border-radius: 0 0 8px 8px;
  padding: 10px 14px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.18);
}

.generation-bar-fill {
  height: 6px;
  border-radius: 3px;
  background: linear-gradient(90deg, #3498db, #3b82f6);
  transition: width 0.35s ease;
}

.generation-bar-label {
  margin-top: 7px;
  font-size: 12px;
  color: var(--text-primary);
  display: flex;
  justify-content: space-between;
  gap: 8px;
}

.generation-bar-percent {
  font-variant-numeric: tabular-nums;
  opacity: 0.75;
}

.generation-enter-active {
  transition:
    opacity 0.2s ease,
    transform 0.2s ease;
}

.generation-leave-active {
  transition: opacity 0.25s ease;
}

.generation-enter-from {
  opacity: 0;
  transform: translateX(-50%) translateY(-6px);
}

.generation-leave-to,
.generation-leave-from {
  opacity: 1;
}

.generation-leave-from {
  transform: translateX(-50%);
}

.generation-leave-to {
  opacity: 0;
  transform: translateX(-50%);
}
</style>
