<template>
    <div v-show="visible" id="cityCenterDialog">
        <div class="dialog-box">
            <div class="dialog-title">📍 City Center</div>
            <div class="dialog-body">
                Does this municipality have an identifiable city center?<br>
                If yes, place a marker on the map. Entrance numbering will radiate outward from that point.<br><br>
                If no, numbering will be determined automatically by road direction (East→West or North→South).
            </div>
            <div class="dialog-buttons">
                <button class="dialog-btn dialog-btn-yes"  @click="cityCenterYes">Yes — Place Marker</button>
                <button class="dialog-btn dialog-btn-skip" @click="cityCenterSkip">No — Skip</button>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed }                      from 'vue'
import { store }                         from '../store'
import { cityCenterYes, cityCenterSkip } from '../map'

const visible = computed(() => store.cityCenterDialogVisible)
</script>
