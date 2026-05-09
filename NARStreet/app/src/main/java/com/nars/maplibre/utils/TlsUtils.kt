package com.nars.maplibre.utils

import android.content.Context
import com.nars.maplibre.BuildConfig
import com.nars.maplibre.security.SecurePreferences
import java.security.KeyStore
import java.security.MessageDigest
import java.security.cert.CertificateFactory
import java.security.cert.X509Certificate
import javax.net.ssl.KeyManagerFactory
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLSocketFactory
import javax.net.ssl.TrustManagerFactory
import javax.net.ssl.X509TrustManager

object TlsUtils {

    private var cachedConfig: TlsConfig? = null

    /**
     * SHA-256 pins of accepted server public keys (base64-encoded).
     */
    private val CERTIFICATE_PINS: Set<String> = BuildConfig.CERTIFICATE_PINS
        .takeIf { it.isNotBlank() }
        ?.split(",")
        ?.map { it.trim() }
        ?.toSet()
        ?: emptySet()

    data class TlsConfig(
        val socketFactory: SSLSocketFactory,
        val trustManager: X509TrustManager
    )

    fun getTlsConfig(context: Context): TlsConfig? {
        if (cachedConfig != null) return cachedConfig
        if (!BuildConfig.MTLS_ENABLED) return null

        cachedConfig = try {
            val sslContext = SSLContext.getInstance("TLS")

            val trustManagers = run {
                val assetName = BuildConfig.CA_CERT_ASSET
                val caStream = context.assets.open(assetName)
                val cf = CertificateFactory.getInstance("X.509")
                val caCert = caStream.use { cf.generateCertificate(it) as X509Certificate }

                val trustStore = KeyStore.getInstance(KeyStore.getDefaultType()).apply {
                    load(null, null)
                    setCertificateEntry("ca", caCert)
                }
                TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm()).apply {
                    init(trustStore)
                }.trustManagers
            }

            val baseTrustManager = trustManagers.firstOrNull { it is X509TrustManager } as? X509TrustManager
                ?: throw IllegalStateException("No X509TrustManager available")

            val effectiveTrustManager = if (CERTIFICATE_PINS.isNotEmpty()) {
                PinningTrustManager(baseTrustManager, CERTIFICATE_PINS)
            } else {
                baseTrustManager
            }

            val keyManagers = run {
                val assetName = BuildConfig.CLIENT_P12_ASSET
                val securePrefs = SecurePreferences(context)
                val password = securePrefs.getP12Password() ?: ""

                if (password.isEmpty()) {
                    NarsLogger.w("TlsUtils", "P12 password not set in SecurePreferences. " +
                        "mTLS may fail. Set via SecurePreferences.saveP12Password() or provide during provisioning.")
                }

                val p12Stream = context.assets.open(assetName)
                val keyStore = KeyStore.getInstance("PKCS12").apply {
                    p12Stream.use { load(it, password.toCharArray()) }
                }
                KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm()).apply {
                    init(keyStore, password.toCharArray())
                }.keyManagers
            }

            sslContext.init(keyManagers, arrayOf(effectiveTrustManager), null)
            TlsConfig(sslContext.socketFactory, effectiveTrustManager)
        } catch (e: Exception) {
            NarsLogger.w("TlsUtils", "Failed to create TLS config: ${e.message}")
            null
        }
        return cachedConfig
    }

    fun getSocketFactory(context: Context): SSLSocketFactory? = getTlsConfig(context)?.socketFactory

    private class PinningTrustManager(
        private val delegate: X509TrustManager,
        private val pins: Set<String>
    ) : X509TrustManager {

        override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) {
            delegate.checkClientTrusted(chain, authType)
        }

        override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {
            delegate.checkServerTrusted(chain, authType)

            val pinMismatch = chain.all { cert ->
                val pin = sha256Base64(cert.publicKey.encoded)
                pin !in pins
            }
            if (pinMismatch) {
                throw java.security.cert.CertificateException(
                    "Certificate pinning failed: no matching pin found in chain"
                )
            }
        }

        override fun getAcceptedIssuers(): Array<X509Certificate> = delegate.acceptedIssuers

        private fun sha256Base64(input: ByteArray): String {
            val digest = MessageDigest.getInstance("SHA-256")
            val hash = digest.digest(input)
            return android.util.Base64.encodeToString(hash, android.util.Base64.NO_WRAP)
        }
    }
}
