package com.nars.maplibre.security

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import com.nars.maplibre.data.model.User

class SecurePreferences(context: Context) {

    private val masterKey: MasterKey by lazy {
        MasterKey.Builder(context)
            .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
            .build()
    }

    private val encryptedPrefs: SharedPreferences by lazy {
        EncryptedSharedPreferences.create(
            context,
            PREFS_NAME,
            masterKey,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
        )
    }

    private val json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
    }

    fun saveAuthToken(token: String) {
        encryptedPrefs.edit()
            .putString(KEY_AUTH_TOKEN, token)
            .apply()
    }

    fun getAuthToken(): String? {
        return encryptedPrefs.getString(KEY_AUTH_TOKEN, null)
    }

    fun clearAuthToken() {
        encryptedPrefs.edit()
            .remove(KEY_AUTH_TOKEN)
            .apply()
    }

    fun saveCookie(cookie: String) {
        encryptedPrefs.edit()
            .putString(KEY_COOKIE, cookie)
            .apply()
    }

    fun getCookie(): String? {
        return encryptedPrefs.getString(KEY_COOKIE, null)
    }

    fun clearCookie() {
        encryptedPrefs.edit()
            .remove(KEY_COOKIE)
            .apply()
    }

    fun saveUser(user: User) {
        val userJson = json.encodeToString(user)
        encryptedPrefs.edit()
            .putString(KEY_USER, userJson)
            .apply()
    }

    fun getUser(): User? {
        val userJson = encryptedPrefs.getString(KEY_USER, null) ?: return null
        return try {
            json.decodeFromString(User.serializer(), userJson)
        } catch (e: Exception) {
            null
        }
    }

    fun clearUser() {
        encryptedPrefs.edit()
            .remove(KEY_USER)
            .apply()
    }

    fun saveMunicipalityName(name: String) {
        encryptedPrefs.edit()
            .putString(KEY_MUNICIPALITY, name)
            .apply()
    }

    fun getMunicipalityName(): String? {
        return encryptedPrefs.getString(KEY_MUNICIPALITY, null)
    }

    fun clearMunicipalityName() {
        encryptedPrefs.edit()
            .remove(KEY_MUNICIPALITY)
            .apply()
    }

    fun clearAll() {
        encryptedPrefs.edit()
            .remove(KEY_AUTH_TOKEN)
            .remove(KEY_COOKIE)
            .remove(KEY_USER)
            .remove(KEY_MUNICIPALITY)
            .apply()
    }

    companion object {
        private const val PREFS_NAME = "nars_secure_prefs"

        private const val KEY_AUTH_TOKEN = "auth_token"
        private const val KEY_COOKIE = "session_cookie"
        private const val KEY_USER = "user"
        private const val KEY_MUNICIPALITY = "municipality"
    }
}
