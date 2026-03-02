import { defineComponent, computed } from 'vue';
import { store }                     from '../store.js';
import { PHASES }                    from '../phases.js';
import { goToPhase, navigatePhase }  from '../map.js';

export default defineComponent({
    name: 'PhaseBar',

    setup() {
        const phases = PHASES;

        const steps = computed(() => phases.map((p, i) => ({
            ...p,
            done:   i < store.currentPhase,
            active: i === store.currentPhase,
            locked: i > store.currentPhase,
            badge:  i < store.currentPhase ? '✓' : String(i + 1),
            connectorDone: i < store.currentPhase,
        })));

        const showSkip = computed(() =>
            phases[store.currentPhase]?.key === 'cityCenter');

        function onPhaseClick(index) { goToPhase(index); }

        function onNext() { navigatePhase(1); }
        function onPrev() { navigatePhase(-1); }
        function onSkip() {
            import('../map.js').then(m => m.cityCenterSkip());
        }

        return { steps, showSkip, onPhaseClick, onNext, onPrev, onSkip, PHASES };
    },

    template: `
        <div id="phaseBar">
            <div id="phaseSteps">
                <template v-for="(step, i) in steps" :key="step.key">
                    <button
                        :class="['phase-step', step.done ? 'done' : step.active ? 'active' : 'locked']"
                        :title="step.label"
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

        <!-- Skip button shown only during City Center phase -->
        <button
            v-show="showSkip"
            id="skipCityCenterBtn"
            @click="onSkip"
        >
            Skip City Center Phase →
        </button>
    `,
});
