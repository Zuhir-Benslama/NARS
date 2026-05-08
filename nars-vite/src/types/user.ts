export type UserRole = "commune_user" | "daira_admin" | "wilaya_admin" | "national_admin"

export interface CommuneInfo {
  id: number | null
  name_fr: string | null
  name_ar: string | null
  latitude: number | null
  longitude: number | null
}

export interface DairaInfo {
  id: number | null
  name_fr: string | null
  name_ar: string | null
  latitude: number | null
  longitude: number | null
}

export interface WilayaInfo {
  id: number | null
  name_fr: string | null
  name_ar: string | null
  latitude: number | null
  longitude: number | null
}

export interface UserInfo {
  id: number
  username: string
  name: string
  email: string
  role: UserRole
  commune: CommuneInfo
  daira?: DairaInfo
  wilaya?: WilayaInfo
}
