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
        :aria-valuetext="step > 0 ? `${stepLabel} ${t(stageKey)}` : t(stageKey)"
      >
        <div class="generation-bar-fill" :style="{ width: `${store.progress}%` }" />
        <div class="generation-bar-label">
          <span class="generation-bar-title">
            <span v-if="step > 0" class="generation-bar-step">{{ stepLabel }}</span>
            <span class="generation-bar-stage">{{ t(stageKey) }}</span>
          </span>
          <span class="generation-bar-percent">{{ Math.round(store.progress) }}%</span>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<script setup lang="ts">
import { computed, onUnmounted } from "vue"
import { useGenerationStore } from "../stores/generationStore"
import { t } from "../i18n"

const store = useGenerationStore()

/** First stage key; used as the label while the bar is between stages. */
const GEN_STAGE_TILES = "gen_roads_stage_tiles"
/** Total numbered steps in the generate-roads flow. */
const TOTAL_STEPS = 4
/** Maps each stage key to its numbered step; chunk is still within the tiles step. */
const STAGE_TO_STEP: Readonly<Record<string, number>> = {
  [GEN_STAGE_TILES]: 1,
  gen_roads_stage_chunk: 1,
  gen_roads_stage_detect: 2,
  gen_roads_stage_save: 3,
  gen_roads_stage_done: 4,
}

const stageKey = computed<string>(() => store.stage || GEN_STAGE_TILES)
const step = computed<number>(() => STAGE_TO_STEP[store.stage] ?? 0)
const stepLabel = computed<string>(() =>
  t("gen_roads_step", { current: step.value, total: TOTAL_STEPS }),
)

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
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  border: 1px solid var(--glass-border);
  border-top: none;
  border-radius: 0 0 10px 10px;
  padding: 10px 14px;
  box-shadow: var(--glass-shadow);
}

.generation-bar-fill {
  height: 6px;
  border-radius: 3px;
  background: linear-gradient(90deg, var(--accent-color), var(--accent-hover));
  transition: width 0.35s ease;
}

.generation-bar-label {
  margin-top: 7px;
  font-size: 12px;
  color: var(--text-secondary);
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}

.generation-bar-title {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.generation-bar-step {
  flex: none;
  padding: 1px 8px;
  border-radius: 999px;
  font-size: 11px;
  font-weight: 600;
  white-space: nowrap;
  background: var(--accent-bg);
  color: var(--accent-color);
  border: 1px solid var(--accent-border);
}

.generation-bar-stage {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.generation-bar-percent {
  flex: none;
  font-variant-numeric: tabular-nums;
  color: var(--text-muted);
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
