package com.nars.narstreet.data.dao

import androidx.room.*
import com.nars.narstreet.data.model.PanelEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface PanelDao {
    @Query("SELECT * FROM panels ORDER BY label ASC")
    fun getAll(): Flow<List<PanelEntity>>

    @Query("SELECT * FROM panels WHERE syncStatus = 'PENDING'")
    suspend fun getPending(): List<PanelEntity>

    @Query("SELECT * FROM panels WHERE remoteId = :remoteId LIMIT 1")
    suspend fun getByRemoteId(remoteId: Long): PanelEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(panels: List<PanelEntity>)

    @Update
    suspend fun update(panel: PanelEntity)

    @Query("UPDATE panels SET syncStatus = 'SYNCED' WHERE id = :id")
    suspend fun markSynced(id: Long)

    @Query("UPDATE panels SET syncStatus = 'ERROR' WHERE id = :id")
    suspend fun markError(id: Long)

    @Query("UPDATE panels SET syncStatus = 'PENDING' WHERE syncStatus = 'ERROR'")
    suspend fun resetErrors()

    @Query("SELECT COUNT(*) FROM panels WHERE syncStatus = 'PENDING'")
    fun pendingCount(): Flow<Int>
}
