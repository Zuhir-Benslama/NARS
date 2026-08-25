<template>
  <span class="stat-pill" :class="colorClass">
    <span class="stat-value">{{ value }}</span>
    <span class="stat-label">{{ label }}</span>
  </span>
</template>

<script setup lang="ts">
import { computed } from "vue"
const props = defineProps<{
  label: string
  value: number
  color?: "blue" | "green" | "default"
}>()
const colorClass = computed(() =>
  props.color === "blue" ? "pill-blue" : props.color === "green" ? "pill-green" : "pill-default",
)
</script>

<style scoped>
.stat-pill {
  display: inline-flex;
  flex-direction: column;
  align-items: center;
  padding: 0.25rem 0.6rem;
  border-radius: 6px;
  font-size: 0.75rem;
  min-width: 52px;
}
.pill-default {
  background: var(--glass-bg);
  color: var(--text-primary);
}
.pill-blue {
  background: rgba(59, 130, 246, 0.15);
  color: #2563eb;
}
.pill-green {
  background: rgba(34, 197, 94, 0.15);
  color: #16a34a;
}
.stat-value {
  font-size: 1.1rem;
  font-weight: 700;
  line-height: 1.2;
}
.stat-label {
  font-size: 0.68rem;
  opacity: 0.75;
}
</style>
