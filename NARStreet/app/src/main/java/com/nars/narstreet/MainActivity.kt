package com.nars.narstreet

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.runtime.*
import androidx.compose.runtime.getValue
import androidx.core.splashscreen.SplashScreen.Companion.installSplashScreen
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.navigation.compose.rememberNavController
import com.nars.narstreet.core.session.SessionManager
import com.nars.narstreet.core.sync.ConnectivityObserver
import com.nars.narstreet.ui.navigation.NARStreetNavGraph
import com.nars.narstreet.ui.theme.NARStreetTheme
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject

@AndroidEntryPoint
class MainActivity : ComponentActivity() {

    @Inject lateinit var session: SessionManager
    @Inject lateinit var connectivity: ConnectivityObserver

    override fun onCreate(savedInstanceState: Bundle?) {
        val splashScreen = installSplashScreen()
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()

        var keepSplash = true
        splashScreen.setKeepOnScreenCondition { keepSplash }

        setContent {
            NARStreetTheme {
                val token by session.token.collectAsStateWithLifecycle(initialValue = null)
                val isOnline by connectivity.isOnline.collectAsStateWithLifecycle(initialValue = true)

                // Dismiss splash once we know auth state
                LaunchedEffect(token) { keepSplash = false }

                val navController = rememberNavController()

                NARStreetNavGraph(
                    navController = navController,
                    isLoggedIn    = token != null,
                )
            }
        }
    }
}
