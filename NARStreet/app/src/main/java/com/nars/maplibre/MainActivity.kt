package com.nars.maplibre

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.ui.Modifier
import com.nars.maplibre.ui.navigation.NarsNavHost
import com.nars.maplibre.ui.theme.NARSTheme
import com.nars.maplibre.ui.theme.ThemeMode

/**
 * Main Activity for NARS Android
 */
class MainActivity : ComponentActivity() {
    
    private lateinit var viewModel: NarsViewModel
    
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        
        // Initialize ViewModel
        val application = application as NarsApplication
        viewModel = NarsViewModel(
            featureStore = application.featureStore,
            appPreferences = application.appPreferences,
            application = application
        )
        
        setContent {
            NARSTheme(
                themeMode = ThemeMode.AUTO
            ) {
                Surface(
                    modifier = Modifier.fillMaxSize(),
                    color = MaterialTheme.colorScheme.background
                ) {
                    NarsNavHost(viewModel = viewModel)
                }
            }
        }
    }
}
