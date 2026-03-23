package com.nars.narstreet.data.dao

import androidx.room.*
import com.nars.narstreet.data.model.SpaceEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface SpaceDao {
    @Query("SELECT * FROM spaces ORDER BY label ASC")
    fun getAll(): Flow<List<SpaceEntity>>

    @Query("SELECT * FROM spaces WHERE syncStatus = 'PENDING'")
    suspend fun getPending(): List<SpaceEntity>

    @Query("SELECT * FROM spaces WHERE remoteId = :remoteId LIMIT 1")
    suspend fun getByRemoteId(remoteId: Long): SpaceEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(spaces: List<SpaceEntity>)

    @Update
    suspend fun update(space: SpaceEntity)

    @Query("UPDATE spaces SET syncStatus = 'SYNCED' WHERE id = :id")
    suspend fun markSynced(id: Long)

    @Query("UPDATE spaces SET syncStatus = 'ERROR' WHERE id = :id")
    suspend fun markError(id: Long)

    @Query("UPDATE spaces SET syncStatus = 'PENDING' WHERE syncStatus = 'ERROR'")
    suspend fun resetErrors()

    @Query("SELECT COUNT(*) FROM spaces WHERE syncStatus = 'PENDING'")
    fun pendingCount(): Flow<Int>
}
