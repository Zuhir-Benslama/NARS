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
    // public_building is a POINT feature — data = {lat, lng}
    val lat: Double = 0.0,
    val lng: Double = 0.0,
    val dataJson: String,
    val syncStatus: SyncStatus = SyncStatus.SYNCED,
) {
    fun toUpdateDto(): FeatureUpdateDto = FeatureUpdateDto(
        label = label,
        data  = mapOf("lat" to lat, "lng" to lng),
    )
}
