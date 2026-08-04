<template>
  <div class="rif-form">
    <h3 class="rif-title">{{ t("rif_title") }}</h3>
    <p class="rif-feature">{{ feature?.label ?? t("rif_unknown") }}</p>

    <div class="rif-field">
      <label>{{ t("label_road_traffic") }}</label>
      <div class="rif-options">
        <button
          v-for="opt in trafficOptions"
          :key="opt.value"
          :class="['rif-btn', { active: data.roadTraffic === opt.value }]"
          :aria-pressed="data.roadTraffic === opt.value"
          @click="data.roadTraffic = opt.value as RoadInspectionData['roadTraffic']"
        >
          {{ opt.label }}
        </button>
      </div>
    </div>

    <div class="rif-field">
      <label>{{ t("label_trad_activity") }}</label>
      <div class="rif-options">
        <button
          v-for="opt in activityOptions"
          :key="opt.value"
          :class="['rif-btn', { active: data.tradActivity === opt.value }]"
          @click="data.tradActivity = opt.value as RoadInspectionData['tradActivity']"
        >
          {{ opt.label }}
        </button>
      </div>
    </div>

    <div class="rif-field">
      <label>{{ t("label_num_lanes") }}</label>
      <input v-model.number="data.numLanes" type="number" min="0" class="rif-input" />
    </div>

    <div class="rif-field rif-toggle">
      <span>{{ t("label_median_presence") }}</span>
      <label class="rif-switch">
        <input v-model="data.hasMedian" type="checkbox" />
        <span class="rif-slider" />
      </label>
    </div>

    <div class="rif-field rif-toggle">
      <span>{{ t("label_vegetation_presence") }}</span>
      <label class="rif-switch">
        <input v-model="data.hasVegetation" type="checkbox" />
        <span class="rif-slider" />
      </label>
    </div>

    <div class="rif-field rif-toggle">
      <span>{{ t("label_dead_end") }}</span>
      <label class="rif-switch">
        <input v-model="data.isDeadEnd" type="checkbox" />
        <span class="rif-slider" />
      </label>
    </div>

    <div class="rif-field rif-toggle">
      <span>{{ t("label_sidewalk_presence") }}</span>
      <label class="rif-switch">
        <input v-model="data.hasSidewalk" type="checkbox" />
        <span class="rif-slider" />
      </label>
    </div>

    <button class="rif-submit" :disabled="submitting" @click="submit">
      {{ submitting ? t("label_saving") : t("btn_submit_inspection") }}
    </button>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref, computed } from "vue"
import { useI18n } from "vue-i18n"
import { apiFetch } from "../../api"
import { getUserMessageKey } from "../../lib/errors"
import { showToast } from "../../lib/toast"
import type { RoadInspectionData } from "../../types/inspection"

const { t } = useI18n()
const props = defineProps<{ feature: { id: string; label: string } | null }>()
const emit = defineEmits<{ done: [] }>()

const data = reactive<RoadInspectionData>({
  roadTraffic: "medium",
  tradActivity: "medium",
  numLanes: 2,
  hasMedian: false,
  hasVegetation: false,
  isDeadEnd: false,
  hasSidewalk: false,
})

const trafficOptions = computed(() => [
  { value: "high", label: t("label_high") },
  { value: "medium", label: t("label_medium") },
  { value: "low", label: t("label_low") },
])
const activityOptions = computed(() => [
  { value: "high", label: t("label_high") },
  { value: "medium", label: t("label_medium") },
  { value: "low", label: t("label_low") },
])

const submitting = ref(false)

async function submit() {
  if (!props.feature) return
  submitting.value = true
  try {
    const status = detectIssues() ? "issue" : "good"
    const res = await apiFetch("/api/field/inspect", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        feature_id: props.feature.id,
        type: "road",
        data,
        status,
      }),
    })
    if (res.ok) {
      showToast(
        status === "good" ? t("alert_road_inspection_saved") : t("alert_issues_reported"),
        status === "good" ? "success" : "error",
      )
      emit("done")
    }
  } catch (e) {
    showToast(t(getUserMessageKey(e)), "error")
  } finally {
    submitting.value = false
  }
}

function detectIssues(): boolean {
  return !data.roadTraffic || !data.tradActivity || data.numLanes < 1
}
</script>

<style scoped>
.rif-form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.rif-title {
  margin: 0;
  font-size: 15px;
  font-weight: 600;
  color: var(--text-primary, #fff);
}
.rif-feature {
  margin: 0;
  font-size: 13px;
  color: var(--text-secondary, #94a3b8);
}
.rif-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.rif-field label {
  font-size: 12px;
  font-weight: 500;
  color: var(--text-secondary, #94a3b8);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}
.rif-options {
  display: flex;
  gap: 6px;
}
.rif-btn {
  flex: 1;
  padding: 6px 10px;
  border: 1px solid var(--glass-border, rgba(255, 255, 255, 0.15));
  border-radius: 6px;
  background: transparent;
  color: var(--text-secondary, #94a3b8);
  font-size: 12px;
  cursor: pointer;
  transition: all 0.15s;
}
.rif-btn.active {
  background: var(--accent, #3b82f6);
  color: var(--text-primary);
  border-color: var(--accent, #3b82f6);
}
.rif-input {
  padding: 8px 10px;
  border: 1px solid var(--glass-border, rgba(255, 255, 255, 0.15));
  border-radius: 6px;
  background: var(--glass-bg, rgba(255, 255, 255, 0.05));
  color: var(--text-primary, #fff);
  font-size: 13px;
  width: 80px;
}
.rif-toggle {
  flex-direction: row;
  align-items: center;
  justify-content: space-between;
}
.rif-toggle span {
  font-size: 13px;
  color: var(--text-primary, #fff);
}
.rif-switch {
  position: relative;
  display: inline-block;
  width: 36px;
  height: 20px;
}
.rif-switch input {
  opacity: 0;
  width: 0;
  height: 0;
}
.rif-slider {
  position: absolute;
  cursor: pointer;
  inset: 0;
  background: var(--glass-border, rgba(255, 255, 255, 0.15));
  border-radius: 20px;
  transition: 0.2s;
}
.rif-slider::before {
  content: "";
  position: absolute;
  height: 14px;
  width: 14px;
  left: 3px;
  bottom: 3px;
  background: var(--text-primary);
  border-radius: 50%;
  transition: 0.2s;
}
.rif-switch input:checked + .rif-slider {
  background: var(--accent, #3b82f6);
}
.rif-switch input:checked + .rif-slider::before {
  transform: translateX(16px);
}
.rif-submit {
  padding: 10px;
  border: none;
  border-radius: 8px;
  background: var(--accent, #3b82f6);
  color: var(--text-primary);
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
  margin-top: 4px;
}
.rif-submit:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>
