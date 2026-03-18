package com.nars.narstreet.data.dao

import androidx.room.*
import com.nars.narstreet.data.model.RoadEntity
import com.nars.narstreet.data.model.SyncStatus
import kotlinx.coroutines.flow.Flow

@Dao
interface RoadDao {
    @Query("SELECT * FROM roads ORDER BY label ASC")
    fun getAll(): Flow<List<RoadEntity>>

    @Query("SELECT * FROM roads WHERE syncStatus = 'PENDING'")
    suspend fun getPending(): List<RoadEntity>

    @Query("SELECT * FROM roads WHERE remoteId = :remoteId LIMIT 1")
    suspend fun getByRemoteId(remoteId: Long): RoadEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(roads: List<RoadEntity>)

    @Update
    suspend fun update(road: RoadEntity)

    @Query("UPDATE roads SET syncStatus = 'SYNCED' WHERE id = :id")
    suspend fun markSynced(id: Long)

    @Query("UPDATE roads SET syncStatus = 'ERROR' WHERE id = :id")
    suspend fun markError(id: Long)

    @Query("SELECT COUNT(*) FROM roads WHERE syncStatus = 'PENDING'")
    fun pendingCount(): Flow<Int>
}
