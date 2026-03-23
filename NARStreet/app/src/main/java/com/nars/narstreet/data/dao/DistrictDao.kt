package com.nars.narstreet.data.dao

import androidx.room.*
import com.nars.narstreet.data.model.DistrictEntity
import kotlinx.coroutines.flow.Flow

@Dao
interface DistrictDao {
    @Query("SELECT * FROM districts ORDER BY label ASC")
    fun getAll(): Flow<List<DistrictEntity>>

    @Query("SELECT * FROM districts WHERE syncStatus = 'PENDING'")
    suspend fun getPending(): List<DistrictEntity>

    @Query("SELECT * FROM districts WHERE remoteId = :remoteId LIMIT 1")
    suspend fun getByRemoteId(remoteId: Long): DistrictEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(districts: List<DistrictEntity>)

    @Update
    suspend fun update(district: DistrictEntity)

    @Query("UPDATE districts SET syncStatus = 'SYNCED' WHERE id = :id")
    suspend fun markSynced(id: Long)

    @Query("UPDATE districts SET syncStatus = 'ERROR' WHERE id = :id")
    suspend fun markError(id: Long)

    @Query("UPDATE districts SET syncStatus = 'PENDING' WHERE syncStatus = 'ERROR'")
    suspend fun resetErrors()

    @Query("SELECT COUNT(*) FROM districts WHERE syncStatus = 'PENDING'")
    fun pendingCount(): Flow<Int>
}
