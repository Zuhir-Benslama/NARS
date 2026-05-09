package com.nars.maplibre.security

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import com.nars.maplibre.data.model.User

/**
 * Secure preferences manager using EncryptedSharedPreferences
 * 
 * This class provides secure storage for sensitive data like auth tokens and user information.
 * All data is encrypted using Android Keystore system.
 */
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

    /**
     * Save auth token securely
     */
    fun saveAuthToken(token: String) {
        encryptedPrefs.edit()
            .putString(KEY_AUTH_TOKEN, token)
            .apply()
    }

    /**
     * Get auth token
     */
    fun getAuthToken(): String? {
        return encryptedPrefs.getString(KEY_AUTH_TOKEN, null)
    }

    /**
     * Clear auth token
     */
    fun clearAuthToken() {
        encryptedPrefs.edit()
            .remove(KEY_AUTH_TOKEN)
            .apply()
    }

    /**
     * Save session cookie securely
     */
    fun saveCookie(cookie: String) {
        encryptedPrefs.edit()
            .putString(KEY_COOKIE, cookie)
            .apply()
    }

    /**
     * Get session cookie
     */
    fun getCookie(): String? {
        return encryptedPrefs.getString(KEY_COOKIE, null)
    }

    /**
     * Clear session cookie
     */
    fun clearCookie() {
        encryptedPrefs.edit()
            .remove(KEY_COOKIE)
            .apply()
    }

    /**
     * Save user data securely
     */
    fun saveUser(user: User) {
        val userJson = json.encodeToString(user)
        encryptedPrefs.edit()
            .putString(KEY_USER, userJson)
            .apply()
    }

    /**
     * Get user data
     */
    fun getUser(): User? {
        val userJson = encryptedPrefs.getString(KEY_USER, null) ?: return null
        return try {
            json.decodeFromString(User.serializer(), userJson)
        } catch (e: Exception) {
            null
        }
    }

    /**
     * Clear user data
     */
    fun clearUser() {
        encryptedPrefs.edit()
            .remove(KEY_USER)
            .apply()
    }

    /**
     * Save municipality name
     */
    fun saveMunicipalityName(name: String) {
        encryptedPrefs.edit()
            .putString(KEY_MUNICIPALITY, name)
            .apply()
    }

    /**
     * Get municipality name
     */
    fun getMunicipalityName(): String? {
        return encryptedPrefs.getString(KEY_MUNICIPALITY, null)
    }

    /**
     * Clear municipality name
     */
    fun clearMunicipalityName() {
        encryptedPrefs.edit()
            .remove(KEY_MUNICIPALITY)
            .apply()
    }

    /**
     * Clear all secure data
     */
    fun clearAll() {
        encryptedPrefs.edit()
            .remove(KEY_AUTH_TOKEN)
            .remove(KEY_COOKIE)
            .remove(KEY_USER)
            .remove(KEY_MUNICIPALITY)
            .apply()
    }

    /**
     * Save P12 client certificate password
     */
    fun saveP12Password(password: String) {
        encryptedPrefs.edit()
            .putString(KEY_P12_PASSWORD, password)
            .apply()
    }

    /**
     * Get P12 client certificate password
     */
    fun getP12Password(): String? {
        return encryptedPrefs.getString(KEY_P12_PASSWORD, null)
    }

    /**
     * Clear P12 password
     */
    fun clearP12Password() {
        encryptedPrefs.edit()
            .remove(KEY_P12_PASSWORD)
            .apply()
    }

    /**
     * Check if user is logged in
     */
    fun isLoggedIn(): Boolean {
        return getAuthToken() != null && getUser() != null
    }

    companion object {
        private const val PREFS_NAME = "nars_secure_prefs"

        private const val KEY_AUTH_TOKEN = "auth_token"
        private const val KEY_COOKIE = "session_cookie"
        private const val KEY_USER = "user"
        private const val KEY_MUNICIPALITY = "municipality"
        private const val KEY_P12_PASSWORD = "p12_password"
    }
}
