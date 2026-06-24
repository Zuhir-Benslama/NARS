import { createRouter, createWebHistory } from "vue-router"

const routes = [
  {
    path: "/",
    redirect: "/admin",
  },
  {
    path: "/map",
    redirect: "/admin",
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

router.beforeEach((_to, _from, next) => {
  // Auth is handled in main.ts before Vue mounts — this guard catches
  // cases where the session expires while the user is on a page.
  next()
})

export default router
