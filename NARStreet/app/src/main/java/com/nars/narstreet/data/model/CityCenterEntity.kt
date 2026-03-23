package com.nars.narstreet.data.model

import androidx.room.Entity
import androidx.room.PrimaryKey
import com.nars.narstreet.data.remote.dto.FeatureUpdateDto

@Entity(tableName = "city_centers")
data class CityCenterEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val remoteId: Long,
    val label: String,
    val lat: Double,
    val lng: Double,
    val dataJson: String       = "{}",
    val syncStatus: SyncStatus = SyncStatus.SYNCED,
) {
    fun toUpdateDto(): FeatureUpdateDto = FeatureUpdateDto(
        label = label,
        data  = mapOf("lat" to lat, "lng" to lng),
    )
}
