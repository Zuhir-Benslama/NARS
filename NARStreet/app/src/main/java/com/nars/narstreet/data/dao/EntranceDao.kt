package com.nars.narstreet.data.dao

import androidx.room.*
import com.nars.narstreet.data.model.EntranceEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface EntranceDao {
    @Query("SELECT * FROM entrances ORDER BY label ASC")
    fun getAll(): Flow<List<EntranceEntity>>

    @Query("SELECT * FROM entrances WHERE syncStatus = 'PENDING'")
    suspend fun getPending(): List<EntranceEntity>

    @Query("SELECT * FROM entrances WHERE remoteId = :remoteId LIMIT 1")
    suspend fun getByRemoteId(remoteId: Long): EntranceEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(entrance: EntranceEntity): Long

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(entrances: List<EntranceEntity>)

    @Update
    suspend fun update(entrance: EntranceEntity)

    @Query("UPDATE entrances SET remoteId = :remoteId WHERE id = :id")
    suspend fun updateRemoteId(id: Long, remoteId: Long)

    @Query("UPDATE entrances SET syncStatus = 'SYNCED' WHERE id = :id")
    suspend fun markSynced(id: Long)

    @Query("UPDATE entrances SET syncStatus = 'ERROR' WHERE id = :id")
    suspend fun markError(id: Long)

    @Query("UPDATE entrances SET syncStatus = 'PENDING' WHERE syncStatus = 'ERROR'")
    suspend fun resetErrors()

    @Query("SELECT COUNT(*) FROM entrances WHERE syncStatus = 'PENDING'")
    fun pendingCount(): Flow<Int>
}
