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

router.beforeEach(() => {
  const appStore = useAppStore()
  if (!appStore.isAuthenticated) {
    return getLoginPath()
  }
})

export default router
