package com.nars.narstreet.data.model

import androidx.room.Entity
import androidx.room.PrimaryKey
import com.nars.narstreet.data.remote.dto.FeatureUpdateDto

@Entity(tableName = "buildings")
data class BuildingEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val remoteId: Long,
    val label: String,
    val layer: String,
    val coordinatesJson: String,   // JSON array of {lat, lng} — edited on device
    val dataJson: String,          // full original blob from NARS API
    val syncStatus: SyncStatus = SyncStatus.SYNCED,
) {
    fun toUpdateDto(): FeatureUpdateDto = FeatureUpdateDto(
        label = label,
        data  = mapOf("coordinates" to coordinatesJson),
    )
}
