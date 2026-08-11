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
    // so redirect with a full page load. Returning the path here would do a
    // client-side navigation to an unregistered route and render a blank page.
    window.location.assign(getLoginPath())
    return false
  }
  if ((to.name === "admin" || to.name === "wilaya-detail") && !appStore.isAdminUser) {
    return false
  }
})

export default router
