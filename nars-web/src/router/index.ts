import { createRouter, createWebHistory } from "vue-router"
import { useAppStore } from "../stores/appStore"
import { getLoginPath } from "../config"

// Routes are used only for admin views. Commune_user and field_worker
// UIs are rendered by App.vue based on role, outside <router-view>.
const routes = [
  {
    path: "/",
    redirect: "/admin",
  },
  {
    path: "/map",
    redirect: "/",
  },
  {
    path: "/admin",
    name: "admin",
    component: () => import("../components/AdminDashboard.vue"),
  },
  {
    path: "/nars/:wilayaName",
    name: "wilaya-detail",
    component: () => import("../components/WilayaDetailPage.vue"),
  },
]

const router = createRouter({
  history: createWebHistory(),
  routes,
})

router.beforeEach((to) => {
  const appStore = useAppStore()
  if (!appStore.isAuthenticated) {
    // The login page is not a SPA route (served statically / by the backend),
    // so do not re-trigger this guard for it — that would cause a redirect loop.
    if (to.fullPath !== getLoginPath()) return getLoginPath()
    return
  }
  if ((to.name === "admin" || to.name === "wilaya-detail") && !appStore.isAdminUser) {
    return false
  }
})

export default router
