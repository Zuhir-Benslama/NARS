<template>
  <div class="npf-form">
    <h3 class="npf-title">Naming Panel Inspection</h3>
    <p class="npf-feature">{{ feature?.label ?? "Unknown naming panel" }}</p>

    <!-- Step 1: Has location? -->
    <div v-if="step === 1" class="npf-step">
      <p class="npf-question">Is the naming panel location present?</p>
      <div class="npf-actions">
        <button class="npf-btn npf-btn-yes" @click="hasLocation(true)">Yes</button>
        <button class="npf-btn npf-btn-no" @click="hasLocation(false)">No</button>
      </div>
    </div>

    <!-- Result: No location -->
    <div v-if="step === 'no_location'" class="npf-result npf-issue">
      <p>⚠ Naming panel location is missing.</p>
      <button class="npf-btn npf-btn-submit" @click="submitInspection('issue')">
        Log as Issue
      </button>
    </div>

    <!-- Step 2: Has panel? -->
    <div v-if="step === 2" class="npf-step">
      <p class="npf-question">Is the naming panel present?</p>
      <div class="npf-actions">
        <button class="npf-btn npf-btn-yes" @click="hasPanel(true)">Yes</button>
        <button class="npf-btn npf-btn-no" @click="hasPanel(false)">No</button>
      </div>
    </div>

    <!-- Result: No panel -->
    <div v-if="step === 'no_panel'" class="npf-result npf-issue">
      <p>⚠ Naming panel is missing.</p>
      <button class="npf-btn npf-btn-submit" @click="submitInspection('issue')">
        Log as Issue
      </button>
    </div>

    <!-- Step 3: Naming correct? -->
    <div v-if="step === 3" class="npf-step">
      <p class="npf-question">Is the naming correct?</p>
      <div class="npf-actions">
        <button class="npf-btn npf-btn-yes" @click="namingCorrect(true)">Yes</button>
        <button class="npf-btn npf-btn-no" @click="namingCorrect(false)">No</button>
      </div>
    </div>

    <!-- Result: Wrong naming -->
    <div v-if="step === 'wrong_naming'" class="npf-result npf-issue">
      <p>⚠ Naming on the panel is incorrect.</p>
      <button class="npf-btn npf-btn-submit" @click="submitInspection('issue')">
        Log as Issue
      </button>
    </div>

    <!-- Step 4: Position correct? -->
    <div v-if="step === 4" class="npf-step">
      <p class="npf-question">Is the panel position correct?</p>
      <div class="npf-actions">
        <button class="npf-btn npf-btn-yes" @click="positionCorrect(true)">Yes</button>
        <button class="npf-btn npf-btn-no" @click="positionCorrect(false)">No</button>
      </div>
    </div>

    <!-- Result: Wrong position -->
    <div v-if="step === 'wrong_position'" class="npf-result npf-issue">
      <p>⚠ Naming panel position is incorrect.</p>
      <button class="npf-btn npf-btn-submit" @click="submitInspection('issue')">
        Log as Issue
      </button>
    </div>

    <!-- Result: All good -->
    <div v-if="step === 'good'" class="npf-result npf-good">
      <p>✓ All checks passed.</p>
      <button class="npf-btn npf-btn-submit" @click="submitInspection('good')">Confirm</button>
    </div>

    <div v-if="submitting" class="npf-loading">Saving...</div>
  </div>
</template>

<script setup lang="ts">
import { ref } from "vue"
import { apiFetch } from "../../api"
import { showToast } from "../../lib/toast"

const props = defineProps<{ feature: { id: string; label: string } | null }>()
const emit = defineEmits<{ done: [] }>()

const step = ref<number | string>(1)
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

async function submitInspection(status: "good" | "issue") {
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
        status === "good" ? "Naming panel inspection complete." : "Issue reported.",
        status === "good" ? "success" : "error",
      )
      emit("done")
    } else {
      const body = await res.json()
      showToast(body.detail ?? "Failed to save", "error")
    }
  } catch (e) {
    showToast("Network error: " + (e as Error).message, "error")
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
  border-color: #22c55e;
  color: #22c55e;
}
.npf-btn-yes:hover {
  background: rgba(34, 197, 94, 0.15);
}
.npf-btn-no {
  border-color: #ef4444;
  color: #ef4444;
}
.npf-btn-no:hover {
  background: rgba(239, 68, 68, 0.15);
}
.npf-btn-submit {
  padding: 10px;
  background: var(--accent, #3b82f6);
  color: #fff;
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
  background: rgba(239, 68, 68, 0.1);
  border: 1px solid rgba(239, 68, 68, 0.25);
}
.npf-issue p {
  color: #fca5a5;
}
.npf-good {
  background: rgba(34, 197, 94, 0.1);
  border: 1px solid rgba(34, 197, 94, 0.25);
}
.npf-good p {
  color: #86efac;
}
.npf-loading {
  font-size: 12px;
  color: var(--text-secondary, #94a3b8);
  text-align: center;
}
</style>
