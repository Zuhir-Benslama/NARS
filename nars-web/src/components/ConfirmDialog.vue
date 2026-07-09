<template>
  <Teleport to="body">
    <Transition name="confirm">
      <div
        v-if="store.visible"
        ref="dialogRef"
        class="confirm-backdrop"
        role="dialog"
        aria-modal="true"
        aria-labelledby="confirmMsg"
        @click.self="store.cancel()"
      >
        <div class="confirm-dialog">
          <p id="confirmMsg" class="confirm-message">{{ store.message }}</p>
          <div class="confirm-actions">
            <button class="confirm-btn cancel" @click="store.cancel()">Cancel</button>
            <button ref="okRef" class="confirm-btn ok" @click="store.confirm()">
              {{ store.okText }}
            </button>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<script setup lang="ts">
import { watch, ref, nextTick, onMounted, onUnmounted } from "vue"
import { useConfirmStore } from "../stores/confirmStore"
import { useFocusTrap } from "../composables/useFocusTrap"

const store = useConfirmStore()
const dialogRef = ref<HTMLElement | null>(null)
const okRef = ref<HTMLElement | null>(null)

useFocusTrap(dialogRef, () => store.visible)

watch(
  () => store.visible,
  async (v) => {
    if (v) {
      await nextTick()
      okRef.value?.focus()
    }
  },
)

function onKey(e: KeyboardEvent) {
  if (e.key === "Escape" && store.visible) {
    store.cancel()
  }
}

onMounted(() => {
  window.addEventListener("keydown", onKey)
})

onUnmounted(() => {
  window.removeEventListener("keydown", onKey)
})
</script>

<style scoped>
.confirm-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.4);
  z-index: 10000;
  display: flex;
  align-items: center;
  justify-content: center;
}

.confirm-dialog {
  background: #fff;
  color: #1e293b;
  padding: 24px;
  border-radius: 12px;
  max-width: 380px;
  width: 90%;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.3);
  font-size: 15px;
  line-height: 1.5;
}

.confirm-message {
  margin: 0 0 20px;
}

.confirm-actions {
  display: flex;
  gap: 12px;
  justify-content: flex-end;
}

.confirm-btn {
  padding: 8px 20px;
  border-radius: 8px;
  font-size: 14px;
  cursor: pointer;
  border: none;
}

.confirm-btn.cancel {
  border: 1px solid #cbd5e1;
  background: #f8fafc;
  color: #475569;
}

.confirm-btn.cancel:hover {
  background: #e2e8f0;
}

.confirm-btn.ok {
  background: #ef4444;
  color: #fff;
  font-weight: 600;
}

.confirm-btn.ok:hover {
  background: #dc2626;
}

.confirm-enter-active,
.confirm-leave-active {
  transition: opacity 0.15s;
}

.confirm-enter-from,
.confirm-leave-to {
  opacity: 0;
}
</style>
