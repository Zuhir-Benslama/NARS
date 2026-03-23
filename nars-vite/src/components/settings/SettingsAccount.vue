<template>
    <div>
        <div class="modal-field">
            <label>{{ t('label_username') }}</label>
            <input type="text"     v-model="form.username" class="modal-input" autocomplete="off" />
        </div>
        <div class="modal-field">
            <label>{{ t('label_email') }}</label>
            <input type="email"    v-model="form.email"    class="modal-input" autocomplete="off" />
        </div>
        <div class="modal-field">
            <label>{{ t('label_password') }}</label>
            <input type="password" v-model="form.password" class="modal-input" :placeholder="t('placeholder_password')" />
        </div>
        <button class="modal-btn modal-btn-save" @click="save">{{ t('btn_update_creds') }}</button>
    </div>
</template>

<script setup lang="ts">
import { reactive, watch }  from 'vue'
import { useI18n }          from 'vue-i18n'
import { store }            from '../../store'
import { apiFetch }         from '../../api'
import { t as tFn }         from '../../i18n'

const { t } = useI18n()

const props = defineProps<{ visible: boolean }>()

const form = reactive({ username: '', email: '', password: '' })

function syncFromStore() {
    if (store.user) {
        form.username = store.user.username || ''
        form.email    = store.user.email    || ''
        form.password = ''
    }
}

syncFromStore()
watch(() => props.visible, v => { if (v) syncFromStore() })

async function save() {
    const res = await apiFetch('/api/user/update', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(form),
    })
    if (res.ok) { alert(tFn('alert_account_updated')); form.password = '' }
}
</script>
