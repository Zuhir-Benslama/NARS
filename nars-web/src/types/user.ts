export type UserRole =
  "commune_user" | "field_worker" | "daira_admin" | "wilaya_admin" | "national_admin"

export interface LocationInfo {
  id: number | null
  name_fr: string | null
  name_ar: string | null
  latitude: number | null
  longitude: number | null
}

/** @deprecated Use LocationInfo */
export type CommuneInfo = LocationInfo
/** @deprecated Use LocationInfo */
export type DairaInfo = LocationInfo
/** @deprecated Use LocationInfo */
export type WilayaInfo = LocationInfo

export interface UserInfo {
  id: number
  username: string
  name: string
  email: string
  role: UserRole
  commune: LocationInfo
  daira?: LocationInfo
  wilaya?: LocationInfo
}
