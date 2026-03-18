package com.nars.narstreet.data.dao

import androidx.room.*
import com.nars.narstreet.data.model.BuildingEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface BuildingDao {
    @Query("SELECT * FROM buildings ORDER BY label ASC")
    fun getAll(): Flow<List<BuildingEntity>>

    @Query("SELECT * FROM buildings WHERE syncStatus = 'PENDING'")
    suspend fun getPending(): List<BuildingEntity>

    @Query("SELECT * FROM buildings WHERE remoteId = :remoteId LIMIT 1")
    suspend fun getByRemoteId(remoteId: Long): BuildingEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(buildings: List<BuildingEntity>)

    @Update
    suspend fun update(building: BuildingEntity)

    @Query("UPDATE buildings SET syncStatus = 'SYNCED' WHERE id = :id")
    suspend fun markSynced(id: Long)

    @Query("UPDATE buildings SET syncStatus = 'ERROR' WHERE id = :id")
    suspend fun markError(id: Long)

    @Query("SELECT COUNT(*) FROM buildings WHERE syncStatus = 'PENDING'")
    fun pendingCount(): Flow<Int>
}
