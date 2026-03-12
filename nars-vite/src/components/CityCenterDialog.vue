<template>
    <div v-show="visible" id="cityCenterDialog">
        <div class="dialog-box">
            <div class="dialog-title">📍 City Center</div>
            <div class="dialog-body">
                Place a city center marker on the map for each urban area.<br>
                Entrance numbering will radiate outward from each city center point.<br><br>
                Each urban area can have at most one city center marker.
            </div>
            <div class="dialog-buttons">
                <button class="dialog-btn dialog-btn-yes" @click="cityCenterYes">OK — Place Marker</button>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed }       from 'vue'
import { store }          from '../store'
import { cityCenterYes }  from '../map'

const visible = computed(() => store.cityCenterDialogVisible)
</script>
