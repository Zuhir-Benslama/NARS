<template>
    <div id="phaseBar">
        <div id="phaseSteps">
            <template v-for="(step, i) in steps" :key="step.key">
                <button
                    :class="['phase-step', step.done ? 'done' : step.active ? 'active' : 'locked']"
                    :title="t(step.label)"
                    :aria-label="t(step.label)"
                    @click="onPhaseClick(i)"
                >
                    <span class="phase-badge">{{ step.badge }}</span>
                </button>
                <span
                    v-if="i < steps.length - 1"
                    :class="['phase-connector', step.done ? 'done' : 'locked']"
                ></span>
            </template>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed }  from 'vue'
import { useI18n }   from 'vue-i18n'
import { store }     from '../store'
import { PHASES }    from '../phases'
import { goToPhase } from '../map'

const { t } = useI18n()

const steps = computed(() => PHASES.map((p, i) => ({
    ...p,
    done:          i < store.currentPhase,
    active:        i === store.currentPhase,
    locked:        i > store.currentPhase,
    badge:         i < store.currentPhase ? '✓' : String(i + 1),
    connectorDone: i < store.currentPhase,
})))

const onPhaseClick = (index: number) => goToPhase(index)
</script>
