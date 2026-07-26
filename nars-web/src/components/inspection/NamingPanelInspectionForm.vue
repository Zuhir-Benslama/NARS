<template>
  <div class="npf-form">
    <h3 class="npf-title">{{ t("npf_title") }}</h3>
    <p class="npf-feature">{{ feature?.label ?? t("npf_unknown") }}</p>

    <!-- Step 1: Has location? -->
    <div v-if="step === 1" class="npf-step">
      <p class="npf-question">{{ t("npf_question_location") }}</p>
      <div class="npf-actions">
        <button class="npf-btn npf-btn-yes" @click="hasLocation(true)">{{ t("label_yes") }}</button>
        <button class="npf-btn npf-btn-no" @click="hasLocation(false)">{{ t("label_no") }}</button>
      </div>
    </div>

    <!-- Result: No location -->
    <div v-if="step === 'no_location'" class="npf-result npf-issue">
      <p>{{ t("npf_missing_location") }}</p>
      <button class="npf-btn npf-btn-submit" @click="submitInspection('issue')">
        {{ t("npf_log_issue") }}
      </button>
    </div>

    <!-- Step 2: Has panel? -->
    <div v-if="step === 2" class="npf-step">
      <p class="npf-question">{{ t("npf_question_panel") }}</p>
      <div class="npf-actions">
        <button class="npf-btn npf-btn-yes" @click="hasPanel(true)">{{ t("label_yes") }}</button>
        <button class="npf-btn npf-btn-no" @click="hasPanel(false)">{{ t("label_no") }}</button>
      </div>
    </div>

    <!-- Result: No panel -->
    <div v-if="step === 'no_panel'" class="npf-result npf-issue">
      <p>{{ t("npf_missing_panel") }}</p>
      <button class="npf-btn npf-btn-submit" @click="submitInspection('issue')">
        {{ t("npf_log_issue") }}
      </button>
    </div>

    <!-- Step 3: Naming correct? -->
    <div v-if="step === 3" class="npf-step">
      <p class="npf-question">{{ t("npf_question_naming") }}</p>
      <div class="npf-actions">
        <button class="npf-btn npf-btn-yes" @click="namingCorrect(true)">
          {{ t("label_yes") }}
        </button>
        <button class="npf-btn npf-btn-no" @click="namingCorrect(false)">
          {{ t("label_no") }}
        </button>
      </div>
    </div>

    <!-- Result: Wrong naming -->
    <div v-if="step === 'wrong_naming'" class="npf-result npf-issue">
      <p>{{ t("npf_wrong_naming") }}</p>
      <button class="npf-btn npf-btn-submit" @click="submitInspection('issue')">
        {{ t("npf_log_issue") }}
      </button>
    </div>

    <!-- Step 4: Position correct? -->
    <div v-if="step === 4" class="npf-step">
      <p class="npf-question">{{ t("npf_question_position") }}</p>
      <div class="npf-actions">
        <button class="npf-btn npf-btn-yes" @click="positionCorrect(true)">
          {{ t("label_yes") }}
        </button>
        <button class="npf-btn npf-btn-no" @click="positionCorrect(false)">
          {{ t("label_no") }}
        </button>
      </div>
    </div>

    <!-- Result: Wrong position -->
    <div v-if="step === 'wrong_position'" class="npf-result npf-issue">
      <p>{{ t("npf_wrong_position") }}</p>
      <button class="npf-btn npf-btn-submit" @click="submitInspection('issue')">
        {{ t("npf_log_issue") }}
      </button>
    </div>

    <!-- Result: All good -->
    <div v-if="step === 'good'" class="npf-result npf-good">
      <p>{{ t("npf_all_good") }}</p>
      <button class="npf-btn npf-btn-submit" @click="submitInspection('good')">
        {{ t("npf_confirm") }}
      </button>
    </div>

    <div v-if="submitting" class="npf-loading">{{ t("npf_saving") }}</div>
  </div>
</template>

<script setup lang="ts">
import { ref } from "vue"
import { useI18n } from "vue-i18n"
import { apiFetch } from "../../api"
import { getErrorMessage } from "../../lib/errors"
import { showToast } from "../../lib/toast"
import type { NamingPanelStep, InspectionStatus } from "../../types/inspection"

const { t } = useI18n()

const props = defineProps<{ feature: { id: string; label: string } | null }>()
const emit = defineEmits<{ done: [] }>()

const step = ref<NamingPanelStep>(1)
const submitting = ref(false)

function hasLocation(val: boolean) {
  if (!val) {
    step.value = "no_location"
    return
  }
  step.value = 2
}

function hasPanel(val: boolean) {
  if (!val) {
    step.value = "no_panel"
    return
  }
  step.value = 3
}

function namingCorrect(val: boolean) {
  if (!val) {
    step.value = "wrong_naming"
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
    hasLocation: step.value !== "no_location",
    hasPanel: step.value !== "no_panel" && step.value !== "no_location" && step.value !== 1,
    namingCorrect: step.value === 4 || step.value === "good" || step.value === "wrong_position",
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
        type: "naming_panel",
        data: getInspectionData(),
        status,
      }),
    })
    if (res.ok) {
      showToast(
        status === "good" ? t("npf_complete") : t("npf_issue_reported"),
        status === "good" ? "success" : "error",
      )
      emit("done")
    } else {
      const body = await res.json()
      showToast(body.detail ?? t("error_save_failed"), "error")
    }
  } catch (e) {
    showToast(t("error_network_with_msg", { message: getErrorMessage(e) }), "error")
  } finally {
    submitting.value = false
  }
}
</script>

<style scoped>
.npf-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.npf-title {
  margin: 0;
  font-size: 15px;
  font-weight: 600;
  color: var(--text-primary, #fff);
}
.npf-feature {
  margin: 0;
  font-size: 13px;
  color: var(--text-secondary, #94a3b8);
}
.npf-step {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.npf-question {
  margin: 0;
  font-size: 14px;
  font-weight: 500;
  color: var(--text-primary, #fff);
}
.npf-actions {
  display: flex;
  gap: 8px;
}
.npf-btn {
  padding: 8px 16px;
  border: 1px solid var(--glass-border, rgba(255, 255, 255, 0.15));
  border-radius: 6px;
  background: transparent;
  color: var(--text-secondary, #94a3b8);
  font-size: 13px;
  cursor: pointer;
  transition: all 0.15s;
}
.npf-btn-yes {
  border-color: var(--success-color);
  color: var(--success-color);
}
.npf-btn-yes:hover {
  background: var(--success-bg);
}
.npf-btn-no {
  border-color: var(--danger-color);
  color: var(--danger-color);
}
.npf-btn-no:hover {
  background: var(--danger-bg);
}
.npf-btn-submit {
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
.npf-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.npf-result {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 12px;
  border-radius: 8px;
}
.npf-result p {
  margin: 0;
  font-size: 14px;
  font-weight: 500;
}
.npf-issue {
  background: var(--danger-bg);
  border: 1px solid var(--danger-border);
}
.npf-issue p {
  color: var(--danger-text-light);
}
.npf-good {
  background: var(--success-bg);
  border: 1px solid var(--success-border);
}
.npf-good p {
  color: var(--success-text-light);
}
.npf-loading {
  font-size: 12px;
  color: var(--text-secondary, #94a3b8);
  text-align: center;
}
</style>
