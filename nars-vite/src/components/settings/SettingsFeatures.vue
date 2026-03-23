<template>
    <div>
        <p class="settings-hint">{{ t('hint_features') }}</p>
        <div class="modal-field">
            <label>{{ t('label_category') }}</label>
            <select v-model="form.category" class="modal-input">
                <option value="districts">{{ t('cat_districts') }}</option>
                <option value="roads">{{ t('cat_roads') }}</option>
                <option value="publicBuildings">{{ t('cat_publicBuildings') }}</option>
                <option value="publicSpaces">{{ t('cat_publicSpaces') }}</option>
            </select>
        </div>
        <div class="modal-field">
            <label>{{ t('label_feature_label') }}</label>
            <input type="text" v-model="form.label" class="modal-input" :placeholder="t('placeholder_feature_label')" />
        </div>
        <button class="modal-btn modal-btn-save" @click="add">{{ t('btn_add_feature') }}</button>
    </div>
</template>

<script setup lang="ts">
import { reactive }   from 'vue'
import { useI18n }    from 'vue-i18n'
import { apiFetch }   from '../../api'
import { t as tFn }   from '../../i18n'

const { t } = useI18n()

const form = reactive({ category: 'districts', label: '' })

async function add() {
    if (!form.label.trim()) return
    const res = await apiFetch('/api/feature-types/custom', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(form),
    })
    if (res.ok) {
        alert(tFn('alert_feature_added', { label: form.label, category: form.category }))
        form.label = ''
    }
}
</script>
