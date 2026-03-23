package com.nars.narstreet.ui.navigation

import androidx.navigation.*
import androidx.navigation.compose.*
import androidx.compose.runtime.Composable
import androidx.hilt.navigation.compose.hiltViewModel
import com.nars.narstreet.ui.login.LoginScreen
import com.nars.narstreet.ui.login.LoginViewModel
import com.nars.narstreet.ui.phase01.Phase01Screen
import com.nars.narstreet.ui.phase02.Phase02Screen
import com.nars.narstreet.ui.phase03.Phase03Screen
import com.nars.narstreet.ui.phase04.Phase04Screen
import com.nars.narstreet.ui.phase05.Phase05Screen
import com.nars.narstreet.ui.phase06.Phase06Screen
import com.nars.narstreet.ui.phase07.Phase07Screen
import com.nars.narstreet.ui.phase08.Phase08Screen

object Route {
    const val LOGIN   = "login"
    const val PHASE01 = "phase01"
    const val PHASE02 = "phase02"
    const val PHASE03 = "phase03"
    const val PHASE04 = "phase04"
    const val PHASE05 = "phase05"

    // Detail routes (polygon/point editor per feature)
    const val PHASE01_DETAIL = "phase01/{areaId}"
    const val PHASE02_DETAIL = "phase02/{districtId}"
    const val PHASE06_DETAIL = "phase06/{buildingId}"
    const val PHASE07_DETAIL = "phase07/{spaceId}"
    const val PHASE08_DETAIL = "phase08/{panelId}"

    fun phase01Detail(id: Long) = "phase01/$id"
    fun phase02Detail(id: Long) = "phase02/$id"
    fun phase06Detail(id: Long) = "phase06/$id"
    fun phase07Detail(id: Long) = "phase07/$id"
    fun phase08Detail(id: Long) = "phase08/$id"
}

@Composable
fun NARStreetNavGraph(
    navController: NavHostController,
    isLoggedIn: Boolean,
) {
    val startDestination = if (isLoggedIn) Route.PHASE01 else Route.LOGIN

    // Navigate to a phase route — pop duplicates to keep the back stack clean
    val navigateTo: (String) -> Unit = { route ->
        val rootRoute = route.substringBefore("/")
        // If navigating to a list screen that's already in the back stack, pop back to it
        val popped = navController.popBackStack(rootRoute, inclusive = false)
        if (!popped) navController.navigate(route)
    }

    val logout: () -> Unit = {
        navController.navigate(Route.LOGIN) { popUpTo(0) { inclusive = true } }
    }

    NavHost(navController = navController, startDestination = startDestination) {

        // ── Login ─────────────────────────────────────────────────────────────
        composable(Route.LOGIN) {
            val vm: LoginViewModel = hiltViewModel()
            LoginScreen(
                viewModel  = vm,
                onLoggedIn = {
                    navController.navigate(Route.PHASE01) {
                        popUpTo(Route.LOGIN) { inclusive = true }
                    }
                },
            )
        }

        // ── Phase 01 — Areas list ─────────────────────────────────────────────
        composable(Route.PHASE01) {
            Phase01Screen(
                areaId       = 0L,
                onNavigateTo = navigateTo,
                onBack       = { navController.popBackStack() },
            )
        }

        // Phase 01 — Area polygon editor
        composable(
            route     = Route.PHASE01_DETAIL,
            arguments = listOf(navArgument("areaId") { type = NavType.LongType }),
        ) { back ->
            Phase01Screen(
                areaId       = back.arguments?.getLong("areaId") ?: 0L,
                onNavigateTo = navigateTo,
                onBack       = { navController.popBackStack() },
            )
        }

        // ── Phase 02 — Districts list ─────────────────────────────────────────
        composable(Route.PHASE02) {
            Phase02Screen(
                districtId   = 0L,
                onNavigateTo = navigateTo,
                onBack       = { navController.popBackStack() },
            )
        }

        // Phase 02 — District polygon editor
        composable(
            route     = Route.PHASE02_DETAIL,
            arguments = listOf(navArgument("districtId") { type = NavType.LongType }),
        ) { back ->
            Phase02Screen(
                districtId   = back.arguments?.getLong("districtId") ?: 0L,
                onNavigateTo = navigateTo,
                onBack       = { navController.popBackStack() },
            )
        }

        // ── Phase 03 — City Center ────────────────────────────────────────────
        composable("phase03") {
            Phase03Screen(
                onNavigateTo = navigateTo,
                onBack       = { navController.popBackStack() },
            )
        }

        // ── Phase 04 — Roads ──────────────────────────────────────────────────
        composable(Route.PHASE04) {
            Phase04Screen(
                onLogout     = logout,
                onNavigateTo = navigateTo,
            )
        }

        // ── Phase 05 — House Entrances ────────────────────────────────────────
        composable(Route.PHASE05) {
            Phase05Screen(
                onNavigateTo = navigateTo,
                onBack       = { navController.popBackStack() },
                onLogout     = logout,
            )
        }

        // ── Phase 06 — Public Buildings ───────────────────────────────────────
        composable(
            route     = Route.PHASE06_DETAIL,
            arguments = listOf(navArgument("buildingId") { type = NavType.LongType }),
        ) { back ->
            val buildingId = back.arguments?.getLong("buildingId") ?: return@composable
            Phase06Screen(
                buildingId   = buildingId,
                onNavigateTo = navigateTo,
                onBack       = { navController.popBackStack() },
            )
        }

        // Handle bare "phase06" route (navigates to first building or shows empty)
        composable("phase06") {
            Phase06Screen(
                buildingId   = 0L,
                onNavigateTo = navigateTo,
                onBack       = { navController.popBackStack() },
            )
        }

        // ── Phase 07 — Public Spaces ──────────────────────────────────────────
        composable(
            route     = Route.PHASE07_DETAIL,
            arguments = listOf(navArgument("spaceId") { type = NavType.LongType }),
        ) { back ->
            val spaceId = back.arguments?.getLong("spaceId") ?: return@composable
            Phase07Screen(
                spaceId      = spaceId,
                onNavigateTo = navigateTo,
                onBack       = { navController.popBackStack() },
            )
        }

        composable("phase07") {
            Phase07Screen(
                spaceId      = 0L,
                onNavigateTo = navigateTo,
                onBack       = { navController.popBackStack() },
            )
        }

        // ── Phase 08 — Naming Panels ──────────────────────────────────────────
        composable(
            route     = Route.PHASE08_DETAIL,
            arguments = listOf(navArgument("panelId") { type = NavType.LongType }),
        ) { back ->
            val panelId = back.arguments?.getLong("panelId") ?: return@composable
            Phase08Screen(
                panelId      = panelId,
                onNavigateTo = navigateTo,
                onBack       = { navController.popBackStack() },
            )
        }

        composable("phase08") {
            Phase08Screen(
                panelId      = 0L,
                onNavigateTo = navigateTo,
                onBack       = { navController.popBackStack() },
            )
        }
    }
}
