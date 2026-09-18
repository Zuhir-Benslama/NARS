import { createRouter, createWebHistory } from "vue-router"
import { defineComponent } from "vue"
import { useAppStore } from "../stores/appStore"
import { getLoginPath } from "../config"

// Routes are used only for admin views. Commune_user and field_worker
// UIs are rendered by App.vue based on role, outside <router-view>.
const routes = [
  {
    path: "/",
    // Role-aware entry: admins land on the dashboard, everyone else on the
    // map route. A blind "/admin" redirect would bounce non-admins through
    // the guard's /map redirect and loop forever.
    redirect: () => (useAppStore().isAdminUser ? "/admin" : "/map"),
  },
  {
    path: "/map",
    name: "map",
    // Non-admin UIs render outside <router-view>, so this matching shell only
    // needs to exist as a stable (non-looping) destination for role redirects.
    component: defineComponent({ name: "MapRoute", render: () => null }),
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
    // Aborting silently was the previous behavior: the URL stayed on the
    // blocked path while App.vue rendered the non-admin UI underneath. Send
    // them to the map route instead so the address bar reflects reality.
    return { path: "/map" }
  }
})

export default router
