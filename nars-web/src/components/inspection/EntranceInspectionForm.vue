<template>
  <div class="eif-form">
    <h3 class="eif-title">{{ t("eif_title") }}</h3>
    <p class="eif-feature">{{ feature?.label ?? t("eif_unknown") }}</p>

    <!-- Step 1: Has entrance? -->
    <div v-if="step === 1" class="eif-step">
      <p class="eif-question">{{ t("eif_q_entrance") }}</p>
      <div class="eif-actions">
        <button class="eif-btn eif-btn-yes" @click="hasEntrance(true)">{{ t("label_yes") }}</button>
        <button class="eif-btn eif-btn-no" @click="hasEntrance(false)">{{ t("label_no") }}</button>
      </div>
    </div>

    <!-- Result: No entrance -->
    <div v-if="step === 'no_entrance'" class="eif-result eif-issue">
      <p>{{ t("eif_missing_entrance") }}</p>
      <button class="eif-btn eif-btn-primary" :disabled="creating" @click="createEntrance">
        {{ creating ? t("label_adding") : t("btn_add_entrance") }}
      </button>
      <button class="eif-btn eif-btn-submit" @click="submitInspection('issue')">
        {{ t("btn_log_issue") }}
      </button>
    </div>

    <!-- Step 2: Has numbering panel? -->
    <div v-if="step === 2" class="eif-step">
      <p class="eif-question">{{ t("eif_q_panel") }}</p>
      <div class="eif-actions">
        <button class="eif-btn eif-btn-yes" @click="hasNumberingPanel(true)">
          {{ t("label_yes") }}
        </button>
        <button class="eif-btn eif-btn-no" @click="hasNumberingPanel(false)">
          {{ t("label_no") }}
        </button>
      </div>
    </div>

    <!-- Result: No numbering panel -->
    <div v-if="step === 'no_panel'" class="eif-result eif-issue">
      <p>{{ t("eif_missing_panel") }}</p>
      <button class="eif-btn eif-btn-submit" @click="submitInspection('issue')">
        {{ t("btn_log_issue") }}
      </button>
    </div>

    <!-- Step 3: Number correct? -->
    <div v-if="step === 3" class="eif-step">
      <p class="eif-question">{{ t("eif_q_number") }}</p>
      <div class="eif-actions">
        <button class="eif-btn eif-btn-yes" @click="numberCorrect(true)">
          {{ t("label_yes") }}
        </button>
        <button class="eif-btn eif-btn-no" @click="numberCorrect(false)">
          {{ t("label_no") }}
        </button>
      </div>
    </div>

    <!-- Result: Wrong number -->
    <div v-if="step === 'wrong_number'" class="eif-result eif-issue">
      <p>{{ t("eif_wrong_number") }}</p>
      <button class="eif-btn eif-btn-submit" @click="submitInspection('issue')">
        {{ t("btn_log_issue") }}
      </button>
    </div>

    <!-- Step 4: Position correct? -->
    <div v-if="step === 4" class="eif-step">
      <p class="eif-question">{{ t("eif_q_position") }}</p>
      <div class="eif-actions">
        <button class="eif-btn eif-btn-yes" @click="positionCorrect(true)">
          {{ t("label_yes") }}
        </button>
        <button class="eif-btn eif-btn-no" @click="positionCorrect(false)">
          {{ t("label_no") }}
        </button>
      </div>
    </div>

    <!-- Result: Wrong position -->
    <div v-if="step === 'wrong_position'" class="eif-result eif-issue">
      <p>{{ t("eif_wrong_position") }}</p>
      <button class="eif-btn eif-btn-submit" @click="submitInspection('issue')">
        {{ t("btn_log_issue") }}
      </button>
    </div>

    <!-- Result: All good -->
    <div v-if="step === 'good'" class="eif-result eif-good">
      <p>{{ t("eif_all_passed") }}</p>
      <button class="eif-btn eif-btn-submit" @click="submitInspection('good')">
        {{ t("btn_confirm") }}
      </button>
    </div>

    <div v-if="submitting" class="eif-loading">{{ t("label_saving") }}</div>
  </div>
</template>

<script setup lang="ts">
import { ref } from "vue"
import { useI18n } from "vue-i18n"
import { apiFetch } from "../../api"
import { getUserMessageKey } from "../../lib/errors"
import { showToast } from "../../lib/toast"
import type { EntranceStep, InspectionStatus } from "../../types/inspection"

const { t } = useI18n()
const props = defineProps<{ feature: { id: string; label: string } | null }>()
const emit = defineEmits<{ done: [] }>()

const step = ref<EntranceStep>(1)
const submitting = ref(false)
const creating = ref(false)

function hasEntrance(val: boolean) {
  if (!val) {
    step.value = "no_entrance"
    return
  }
  step.value = 2
}

function hasNumberingPanel(val: boolean) {
  if (!val) {
    step.value = "no_panel"
    return
  }
  step.value = 3
}

function numberCorrect(val: boolean) {
  if (!val) {
    step.value = "wrong_number"
    return
  }
  step.value = 4
}

function positionCorrect(val: boolean) {
  if (!val) {
    step.value = "wrong_position"
    return
  }
  step.value = "good"
}

function getInspectionData() {
  return {
    hasEntrance: step.value !== "no_entrance",
    hasNumberingPanel:
      step.value !== "no_panel" && step.value !== "no_entrance" && step.value !== 1,
    numberCorrect: step.value === 4 || step.value === "good" || step.value === "wrong_position",
    positionCorrect: step.value === "good",
  }
}

async function submitInspection(status: InspectionStatus) {
  if (!props.feature) return
  submitting.value = true
  try {
    const res = await apiFetch("/api/field/inspect", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        feature_id: props.feature.id,
        type: "house_entrance",
        data: getInspectionData(),
        status,
      }),
    })
    if (res.ok) {
      showToast(
        status === "good" ? t("alert_entrance_inspection_complete") : t("alert_issue_reported"),
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

async function createEntrance() {
  if (!props.feature) return
  creating.value = true
  try {
    const res = await apiFetch("/api/field/entrance/create", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        road_id: props.feature.id,
        label: t("label_entrance_field_worker"),
        data: { coordinates: [], note: t("label_missing_entrance_note") },
      }),
    })
    if (res.ok) {
      showToast(t("alert_entrance_added"), "success")
      await submitInspection("issue")
    }
  } catch (e) {
    showToast(t(getUserMessageKey(e)), "error")
  } finally {
    creating.value = false
  }
}
</script>

<style scoped>
.eif-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.eif-title {
  margin: 0;
  font-size: 15px;
  font-weight: 600;
  color: var(--text-primary, #fff);
}
.eif-feature {
  margin: 0;
  font-size: 13px;
  color: var(--text-secondary, #94a3b8);
}
.eif-step {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.eif-question {
  margin: 0;
  font-size: 14px;
  font-weight: 500;
  color: var(--text-primary, #fff);
}
.eif-actions {
  display: flex;
  gap: 8px;
}
.eif-btn {
  padding: 8px 16px;
  border: 1px solid var(--glass-border, rgba(255, 255, 255, 0.15));
  border-radius: 6px;
  background: transparent;
  color: var(--text-secondary, #94a3b8);
  font-size: 13px;
  cursor: pointer;
  transition: all 0.15s;
}
.eif-btn-yes {
  border-color: var(--success-color);
  color: var(--success-color);
}
.eif-btn-yes:hover {
  background: var(--success-bg);
}
.eif-btn-no {
  border-color: var(--danger-color);
  color: var(--danger-color);
}
.eif-btn-no:hover {
  background: var(--danger-bg);
}
.eif-btn-primary {
  background: var(--accent, #3b82f6);
  color: var(--text-primary);
  border-color: var(--accent, #3b82f6);
}
.eif-btn-submit {
  padding: 10px;
  background: var(--accent, #3b82f6);
  color: var(--text-primary);
  border: none;
  border-radius: 8px;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
  margin-top: 4px;
}
.eif-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.eif-result {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 12px;
  border-radius: 8px;
}
.eif-result p {
  margin: 0;
  font-size: 14px;
  font-weight: 500;
}
.eif-issue {
  background: var(--danger-bg);
  border: 1px solid var(--danger-border);
}
.eif-issue p {
  color: var(--danger-text-light);
}
.eif-good {
  background: var(--success-bg);
  border: 1px solid var(--success-border);
}
.eif-good p {
  color: var(--success-text-light);
}
.eif-loading {
  font-size: 12px;
  color: var(--text-secondary, #94a3b8);
  text-align: center;
}
</style>
