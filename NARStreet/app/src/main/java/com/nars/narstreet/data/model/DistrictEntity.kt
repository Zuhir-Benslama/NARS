package com.nars.narstreet.data.model

import androidx.room.Entity
import androidx.room.PrimaryKey
import com.nars.narstreet.data.remote.dto.FeatureUpdateDto

@Entity(tableName = "districts")
data class DistrictEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val remoteId: Long,
    val label: String,
    val layer: String,             // housing_estate | urban_pole | district | trad_activities_zone | industry_zone
    val coordinatesJson: String = "[]",
    val dataJson: String        = "{}",
    val syncStatus: SyncStatus  = SyncStatus.SYNCED,
) {
    fun toUpdateDto(): FeatureUpdateDto = FeatureUpdateDto(
        label = label,
        // Reuse coordinatesJsonToList (defined in AreaEntity.kt) — sends a proper
        // JSON array, not a string-wrapped array.
        data  = mapOf("coordinates" to coordinatesJsonToList(coordinatesJson)),
    )
}
