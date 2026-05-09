package com.nars.maplibre.utils

import android.content.Context
import com.nars.maplibre.BuildConfig
import java.security.KeyStore
import java.security.cert.CertificateFactory
import java.security.cert.X509Certificate
import javax.net.ssl.KeyManagerFactory
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLSocketFactory
import javax.net.ssl.TrustManagerFactory

object TlsUtils {

    private var cachedFactory: SSLSocketFactory? = null

    fun getSocketFactory(context: Context): SSLSocketFactory? {
        if (cachedFactory != null) return cachedFactory
        if (!BuildConfig.MTLS_ENABLED) return null

        cachedFactory = try {
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

            val keyManagers = run {
                val assetName = BuildConfig.CLIENT_P12_ASSET
                val password = BuildConfig.CLIENT_P12_PASSWORD
                val p12Stream = context.assets.open(assetName)
                val keyStore = KeyStore.getInstance("PKCS12").apply {
                    p12Stream.use { load(it, password.toCharArray()) }
                }
                KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm()).apply {
                    init(keyStore, password.toCharArray())
                }.keyManagers
            }

            sslContext.init(keyManagers, trustManagers, null)
            sslContext.socketFactory
        } catch (e: Exception) {
            NarsLogger.w("TlsUtils", "Failed to create TLS socket factory: ${e.message}")
            null
        }
        return cachedFactory
    }
}
