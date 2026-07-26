import type { UserRole } from "./user"

export interface UserFeatureStats {
  user_id: string
  username: string
  name: string
  email: string
  role: UserRole
  areas: number
  districts: number
  city_centers: number
  roads: number
  house_entrances: number
  public_buildings: number
  public_spaces: number
  naming_panels: number
  total: number
}

export interface AdminInfo {
  user_id: string
  username: string
  name: string
  email: string
  role: UserRole
}

export interface CommuneReport {
  commune_id: number
  commune_name_fr: string
  commune_name_ar: string
  users: UserFeatureStats[]
}

export interface DairaReport {
  daira_id: number
  daira_name_fr: string
  daira_name_ar: string
  daira_admin: AdminInfo | null
  communes: CommuneReport[]
}

export interface WilayaReport {
  wilaya_id: number
  wilaya_name_fr: string
  wilaya_name_ar: string
  wilaya_admin: AdminInfo | null
  dairas: DairaReport[]
}

export interface WilayaSummary {
  wilaya_id: number
  wilaya_name_fr: string
  wilaya_name_ar: string
  wilaya_admin: AdminInfo | null
  daira_count: number
  commune_count: number
  commune_user_count: number
}

export interface NationalOverview {
  wilayas: WilayaSummary[]
}
