package com.nars.narstreet.data.dao

import androidx.room.*
import com.nars.narstreet.data.model.AreaEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface AreaDao {
    @Query("SELECT * FROM areas ORDER BY label ASC")
    fun getAll(): Flow<List<AreaEntity>>

    @Query("SELECT * FROM areas WHERE syncStatus = 'PENDING'")
    suspend fun getPending(): List<AreaEntity>

    @Query("SELECT * FROM areas WHERE remoteId = :remoteId LIMIT 1")
    suspend fun getByRemoteId(remoteId: Long): AreaEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(areas: List<AreaEntity>)

    @Update
    suspend fun update(area: AreaEntity)

    @Query("UPDATE areas SET syncStatus = 'SYNCED' WHERE id = :id")
    suspend fun markSynced(id: Long)

    @Query("UPDATE areas SET syncStatus = 'ERROR' WHERE id = :id")
    suspend fun markError(id: Long)

    @Query("UPDATE areas SET syncStatus = 'PENDING' WHERE syncStatus = 'ERROR'")
    suspend fun resetErrors()

    @Query("SELECT COUNT(*) FROM areas WHERE syncStatus = 'PENDING'")
    fun pendingCount(): Flow<Int>
}
