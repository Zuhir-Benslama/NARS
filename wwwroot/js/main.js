import { createApp } from 'vue';
import { store }      from './store.js';

// Components
import PhaseBar          from './components/PhaseBar.js';
import CityCenterDialog  from './components/CityCenterDialog.js';
import InfoPanel         from './components/InfoPanel.js';
import ProfileMenu       from './components/ProfileMenu.js';
import FeatureModal      from './components/FeatureModal.js';

// Map
import { initMap, loadFromDatabase, loadUserAndCommune } from './map.js';

// ── Vue application ────────────────────────────────────────────────────────────

const app = createApp({
    template: `
        <PhaseBar />
        <CityCenterDialog />
        <InfoPanel />
        <ProfileMenu />
        <FeatureModal />
    `,
});

// ── Global directive: v-click-outside ─────────────────────────────────────────
// Used by ProfileMenu to close the dropdown when clicking outside.
app.directive('click-outside', {
    mounted(el, binding) {
        el._clickOutsideHandler = (e) => {
            if (!el.contains(e.target)) binding.value(e);
        };
        document.addEventListener('click', el._clickOutsideHandler);
    },
    unmounted(el) {
        document.removeEventListener('click', el._clickOutsideHandler);
    },
});

// ── Register components ────────────────────────────────────────────────────────
app.component('PhaseBar',         PhaseBar);
app.component('CityCenterDialog', CityCenterDialog);
app.component('InfoPanel',        InfoPanel);
app.component('ProfileMenu',      ProfileMenu);
app.component('FeatureModal',     FeatureModal);

// ── Mount ──────────────────────────────────────────────────────────────────────
app.mount('#app');

// ── Initialize Leaflet map and load data ───────────────────────────────────────
// These run after Vue is mounted so all store watchers are active.
(async () => {
    initMap();
    await loadUserAndCommune();
    await loadFromDatabase();
    console.log('NARS Urban Addressing — initialized');
})();
