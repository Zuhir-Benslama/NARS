<template>
  <div class="eif-form">
    <h3 class="eif-title">Entrance Inspection</h3>
    <p class="eif-feature">{{ feature?.label ?? "Unknown entrance" }}</p>

    <!-- Step 1: Has entrance? -->
    <div v-if="step === 1" class="eif-step">
      <p class="eif-question">Does the entrance exist?</p>
      <div class="eif-actions">
        <button class="eif-btn eif-btn-yes" @click="hasEntrance(true)">Yes</button>
        <button class="eif-btn eif-btn-no" @click="hasEntrance(false)">No</button>
      </div>
    </div>

    <!-- Result: No entrance -->
    <div v-if="step === 'no_entrance'" class="eif-result eif-issue">
      <p>⚠ Entrance is missing.</p>
      <button class="eif-btn eif-btn-primary" :disabled="creating" @click="createEntrance">
        {{ creating ? "Adding..." : "Add Entrance" }}
      </button>
      <button class="eif-btn eif-btn-submit" @click="submitInspection('issue')">
        Log as Issue
      </button>
    </div>

    <!-- Step 2: Has numbering panel? -->
    <div v-if="step === 2" class="eif-step">
      <p class="eif-question">Does it have a numbering panel?</p>
      <div class="eif-actions">
        <button class="eif-btn eif-btn-yes" @click="hasNumberingPanel(true)">Yes</button>
        <button class="eif-btn eif-btn-no" @click="hasNumberingPanel(false)">No</button>
      </div>
    </div>

    <!-- Result: No numbering panel -->
    <div v-if="step === 'no_panel'" class="eif-result eif-issue">
      <p>⚠ Numbering panel is missing.</p>
      <button class="eif-btn eif-btn-submit" @click="submitInspection('issue')">
        Log as Issue
      </button>
    </div>

    <!-- Step 3: Number correct? -->
    <div v-if="step === 3" class="eif-step">
      <p class="eif-question">Is the number correct?</p>
      <div class="eif-actions">
        <button class="eif-btn eif-btn-yes" @click="numberCorrect(true)">Yes</button>
        <button class="eif-btn eif-btn-no" @click="numberCorrect(false)">No</button>
      </div>
    </div>

    <!-- Result: Wrong number -->
    <div v-if="step === 'wrong_number'" class="eif-result eif-issue">
      <p>⚠ Number is incorrect.</p>
      <button class="eif-btn eif-btn-submit" @click="submitInspection('issue')">
        Log as Issue
      </button>
    </div>

    <!-- Step 4: Position correct? -->
    <div v-if="step === 4" class="eif-step">
      <p class="eif-question">Is the numbering panel position correct?</p>
      <div class="eif-actions">
        <button class="eif-btn eif-btn-yes" @click="positionCorrect(true)">Yes</button>
        <button class="eif-btn eif-btn-no" @click="positionCorrect(false)">No</button>
      </div>
    </div>

    <!-- Result: Wrong position -->
    <div v-if="step === 'wrong_position'" class="eif-result eif-issue">
      <p>⚠ Numbering panel position is incorrect.</p>
      <button class="eif-btn eif-btn-submit" @click="submitInspection('issue')">
        Log as Issue
      </button>
    </div>

    <!-- Result: All good -->
    <div v-if="step === 'good'" class="eif-result eif-good">
      <p>✓ All checks passed.</p>
      <button class="eif-btn eif-btn-submit" @click="submitInspection('good')">Confirm</button>
    </div>

    <div v-if="submitting" class="eif-loading">Saving...</div>
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

async function submitInspection(status: "good" | "issue") {
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
        status === "good" ? "Entrance inspection complete." : "Issue reported.",
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

async function createEntrance() {
  if (!props.feature) return
  creating.value = true
  try {
    const res = await apiFetch("/api/field/entrance/create", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        road_id: props.feature.id,
        label: "Entrance (field worker)",
        data: { coordinates: [], note: "missing entrance added by field worker" },
      }),
    })
    if (res.ok) {
      showToast("Entrance added successfully.", "success")
      submitInspection("issue")
    } else {
      const body = await res.json()
      showToast(body.detail ?? "Failed to create entrance", "error")
    }
  } catch (e) {
    showToast("Network error: " + (e as Error).message, "error")
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
  border-color: #22c55e;
  color: #22c55e;
}
.eif-btn-yes:hover {
  background: rgba(34, 197, 94, 0.15);
}
.eif-btn-no {
  border-color: #ef4444;
  color: #ef4444;
}
.eif-btn-no:hover {
  background: rgba(239, 68, 68, 0.15);
}
.eif-btn-primary {
  background: var(--accent, #3b82f6);
  color: #fff;
  border-color: var(--accent, #3b82f6);
}
.eif-btn-submit {
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
  background: rgba(239, 68, 68, 0.1);
  border: 1px solid rgba(239, 68, 68, 0.25);
}
.eif-issue p {
  color: #fca5a5;
}
.eif-good {
  background: rgba(34, 197, 94, 0.1);
  border: 1px solid rgba(34, 197, 94, 0.25);
}
.eif-good p {
  color: #86efac;
}
.eif-loading {
  font-size: 12px;
  color: var(--text-secondary, #94a3b8);
  text-align: center;
}
</style>
