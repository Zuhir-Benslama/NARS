package com.nars.narstreet.data.model

import androidx.room.Entity
import androidx.room.PrimaryKey
import com.nars.narstreet.data.remote.dto.FeatureUpdateDto

@Entity(tableName = "panels")
data class PanelEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val remoteId: Long,
    val label: String,
    val lat: Double,
    val lng: Double,
    val dataJson: String,

    // Phase 08 field checklist
    val isPlaced: Boolean?           = null,  // panel physically exists on site
    val isCorrectLocation: Boolean?  = null,  // placed in the right spot
    val orderNeeded: Boolean         = false, // needs to be ordered
    val relocateNeeded: Boolean      = false, // needs to be moved

    val syncStatus: SyncStatus = SyncStatus.SYNCED,
) {
    fun toUpdateDto(): FeatureUpdateDto = FeatureUpdateDto(
        label = label,
        data  = mapOf(
            "isPlaced"          to isPlaced,
            "isCorrectLocation" to isCorrectLocation,
            "orderNeeded"       to orderNeeded,
            "relocateNeeded"    to relocateNeeded,
        ),
    )
}
