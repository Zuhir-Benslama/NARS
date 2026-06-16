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

export default router
