/* eslint-disable */
// Generated — DO NOT EDIT manually. Run `npm run codegen:types` to regenerate
// from the live NARS backend OpenAPI spec at /openapi/v1.json.

export interface paths {
  "/api/current_user": {
    get: {
      parameters: {
        query?: never
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["UserInfo"]
          }
        }
        401: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
      }
    }
  }

  "/api/refresh": {
    post: {
      parameters: {
        query?: never
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["RefreshResponse"]
          }
        }
        401: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
      }
    }
  }

  "/api/logout": {
    post: {
      parameters: {
        query?: never
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["ActionResponse"]
          }
        }
      }
    }
  }

  "/api/signin": {
    post: {
      parameters: {
        query?: never
      }
      requestBody: {
        content: {
          "application/json": components["schemas"]["SignInRequest"]
        }
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["SignInResponse"]
          }
        }
        401: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
      }
    }
  }

  "/api/load": {
    get: {
      parameters: {
        query?: {
          skip?: number
          take?: number
        }
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["LoadFeaturesResponse"]
          }
        }
      }
    }
  }

  "/api/save": {
    post: {
      parameters: {
        query?: never
      }
      requestBody: {
        content: {
          "application/json": components["schemas"]["FeatureSaveRequest"]
        }
      }
      responses: {
        201: {
          content: {
            "application/json": components["schemas"]["SaveFeatureResponse"]
          }
        }
        400: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
      }
    }
  }

  "/api/update/{featureId}": {
    put: {
      parameters: {
        path: {
          featureId: string
        }
      }
      requestBody: {
        content: {
          "application/json": components["schemas"]["FeatureUpdateRequest"]
        }
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["UpdateFeatureResponse"]
          }
        }
        400: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
        404: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
      }
    }
  }

  "/api/delete/{featureId}": {
    delete: {
      parameters: {
        path: {
          featureId: string
        }
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["ActionResponse"]
          }
        }
        400: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
        404: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
      }
    }
  }

  "/api/stats": {
    get: {
      parameters: {
        query?: never
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["FeatureStatsResponse"]
          }
        }
      }
    }
  }

  "/api/user/update": {
    put: {
      parameters: {
        query?: never
      }
      requestBody: {
        content: {
          "application/json": components["schemas"]["UpdateUserRequest"]
        }
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["UpdateCredentialsResponse"]
          }
        }
        400: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
        404: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
        409: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
      }
    }
  }

  "/api/admin/users": {
    post: {
      parameters: {
        query?: never
      }
      requestBody: {
        content: {
          "application/json": components["schemas"]["CreateAdminRequest"]
        }
      }
      responses: {
        201: {
          content: {
            "application/json": components["schemas"]["CreateAdminResponse"]
          }
        }
        400: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
        401: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
        403: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
        409: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
      }
    }
  }

  "/api/admin/overview": {
    get: {
      parameters: {
        query?: never
      }
      responses: {
        200: {
          content: {
            "application/json":
              | components["schemas"]["NationalOverview"]
              | components["schemas"]["WilayaReport"]
              | components["schemas"]["DairaReport"]
          }
        }
        401: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
        403: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
      }
    }
  }

  "/api/admin/wilaya/{wilayaId}": {
    get: {
      parameters: {
        path: {
          wilayaId: number
        }
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["WilayaReport"]
          }
        }
        401: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
        403: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
        404: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
      }
    }
  }

  "/api/wilayas": {
    get: {
      parameters: {
        query?: {
          search?: string
          skip?: number
          take?: number
        }
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["PagedWilayaResponse"]
          }
        }
        400: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
      }
    }
  }

  "/api/dairas": {
    get: {
      parameters: {
        query: {
          wilaya_id: number
          search?: string
          skip?: number
          take?: number
        }
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["PagedDairaResponse"]
          }
        }
        400: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
      }
    }
  }

  "/api/communes": {
    get: {
      parameters: {
        query: {
          daira_id: number
          search?: string
          skip?: number
          take?: number
        }
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["PagedCommuneResponse"]
          }
        }
        400: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
      }
    }
  }

  "/api/commune/{communeId}/boundary": {
    get: {
      parameters: {
        path: {
          communeId: number
        }
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["CommuneBoundaryResponse"]
          }
        }
        404: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
      }
    }
  }

  "/api/validate/road": {
    post: {
      parameters: {
        query?: never
      }
      requestBody: {
        content: {
          "application/json": components["schemas"]["ValidateRoadRequest"]
        }
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["ValidateRoadResponse"]
          }
        }
        400: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
      }
    }
  }

  "/api/validate/district": {
    post: {
      parameters: {
        query?: never
      }
      requestBody: {
        content: {
          "application/json": components["schemas"]["ValidateDistrictRequest"]
        }
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["ValidateDistrictResponse"]
          }
        }
        400: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
      }
    }
  }

  "/api/validate/districts/coverage": {
    get: {
      parameters: {
        query?: never
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["DistrictCoverageResponse"]
          }
        }
      }
    }
  }

  "/api/validate/area/main-urban-exists": {
    get: {
      parameters: {
        query?: never
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["MainUrbanExistsResponse"]
          }
        }
      }
    }
  }

  "/api/road-side": {
    post: {
      parameters: {
        query?: never
      }
      requestBody: {
        content: {
          "application/json": components["schemas"]["RoadSideRequest"]
        }
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["RoadSideResponse"]
          }
        }
        400: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
        404: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
      }
    }
  }

  "/api/field/features": {
    get: {
      parameters: {
        query?: {
          type?: "road" | "house_entrance" | "naming_panel"
          skip?: number
          take?: number
        }
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["FieldFeaturesResponse"]
          }
        }
        400: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
        401: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
        403: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
      }
    }
  }

  "/api/field/inspect": {
    post: {
      parameters: {
        query?: never
      }
      requestBody: {
        content: {
          "application/json": components["schemas"]["FieldInspectRequest"]
        }
      }
      responses: {
        201: {
          content: {
            "application/json": components["schemas"]["FieldInspectResponse"]
          }
        }
        400: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
        403: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
      }
    }
  }

  "/api/field/entrance/create": {
    post: {
      parameters: {
        query?: never
      }
      requestBody: {
        content: {
          "application/json": components["schemas"]["FieldEntranceCreateRequest"]
        }
      }
      responses: {
        201: {
          content: {
            "application/json": components["schemas"]["FieldEntranceCreateResponse"]
          }
        }
        400: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
        403: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
      }
    }
  }

  "/api/areas/refresh-scattered": {
    post: {
      parameters: {
        query?: never
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["ScatteredRefreshResponse"]
          }
        }
      }
    }
  }

  "/api/feature-types/custom": {
    post: {
      parameters: {
        query?: never
      }
      requestBody: {
        content: {
          "application/json": components["schemas"]["CustomFeatureTypeRequest"]
        }
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["ActionResponse"]
          }
        }
      }
    }
  }

  "/api/feature-types": {
    get: {
      parameters: {
        query?: never
      }
      responses: {
        200: {
          content: {
            "application/json": components["schemas"]["FeatureTypeDefinition"][]
          }
        }
      }
    }
  }

  "/api/logs": {
    post: {
      parameters: {
        query?: never
      }
      requestBody: {
        content: {
          "application/json": components["schemas"]["LogBatch"]
        }
      }
      responses: {
        204: {
          content: never
        }
        400: {
          content: {
            "application/json": components["schemas"]["ErrorResponse"]
          }
        }
      }
    }
  }
}

export type webhooks = Record<string, never>

export interface components {
  schemas: {
    ErrorResponse: {
      detail?: string
      title?: string
      status?: number
    }

    ActionResponse: {
      success: boolean
      message: string
    }

    SignInRequest: {
      username: string
      password: string
    }

    SignInResponse: {
      success: boolean
      token: string
      token_type: string
      user: components["schemas"]["UserInfo"]
    }

    RefreshResponse: {
      success: boolean
      token_type: string
    }

    CommuneInfo: {
      id: number | null
      name_fr: string | null
      name_ar: string | null
      latitude: number | null
      longitude: number | null
    }

    DairaInfo: {
      id: number | null
      name_fr: string | null
      name_ar: string | null
      latitude: number | null
      longitude: number | null
    }

    WilayaInfo: {
      id: number | null
      name_fr: string | null
      name_ar: string | null
      latitude: number | null
      longitude: number | null
    }

    UserInfo: {
      id: number
      username: string
      name: string
      email: string
      role: string
      commune: components["schemas"]["CommuneInfo"]
      daira?: components["schemas"]["DairaInfo"]
      wilaya?: components["schemas"]["WilayaInfo"]
    }

    FeatureSaveRequest: {
      type: string
      layer: string
      label: string
      data: Record<string, unknown>
    }

    SaveFeatureResponse: {
      success: boolean
      id: string
      message: string
    }

    FeatureUpdateRequest: {
      label?: string
      data?: Record<string, unknown>
    }

    UpdateFeatureResponse: {
      success: boolean
      id: string
      updated_at: string
    }

    DbFeature: {
      id: string
      layer: string
      feature_type: string
      label: string
      data: string | Record<string, unknown>
      created_at: string
    }

    LoadFeaturesResponse: {
      features: components["schemas"]["DbFeature"][]
      count: number
      skip: number
      take: number
    }

    FeatureStatsResponse: {
      area: number
      district: number
      city_center: number
      road: number
      house_entrance: number
      public_building: number
      public_space: number
      naming_panel: number
      total: number
    }

    UpdateUserRequest: {
      username?: string
      email?: string
      password?: string
    }

    UpdateCredentialsResponse: {
      success: boolean
      message: string
      user: {
        username?: string
        email?: string
      }
    }

    CreateAdminRequest: {
      name: string
      email: string
      phone: string
      username: string
      password: string
      role: string
      commune_id?: number
      daira_id?: number
      wilaya_id?: number
    }

    CreateAdminResponse: {
      success: boolean
      user_id: string
      message: string
    }

    AdminInfo: {
      user_id: string
      username: string
      name: string
      email: string
      role: string
    }

    UserFeatureStats: {
      user_id: string
      username: string
      name: string
      email: string
      role: string
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

    CommuneReport: {
      commune_id: number
      commune_name_fr: string
      commune_name_ar: string
      users: components["schemas"]["UserFeatureStats"][]
    }

    DairaReport: {
      daira_id: number
      daira_name_fr: string
      daira_name_ar: string
      daira_admin: components["schemas"]["AdminInfo"] | null
      communes: components["schemas"]["CommuneReport"][]
    }

    WilayaReport: {
      wilaya_id: number
      wilaya_name_fr: string
      wilaya_name_ar: string
      wilaya_admin: components["schemas"]["AdminInfo"] | null
      dairas: components["schemas"]["DairaReport"][]
    }

    WilayaSummary: {
      wilaya_id: number
      wilaya_name_fr: string
      wilaya_name_ar: string
      wilaya_admin: components["schemas"]["AdminInfo"] | null
      daira_count: number
      commune_count: number
      commune_user_count: number
    }

    NationalOverview: {
      wilayas: components["schemas"]["WilayaSummary"][]
    }

    WilayaItem: {
      id: number
      name_fr: string
      name_ar: string
      latitude: number | null
      longitude: number | null
    }

    DairaItem: {
      id: number
      name_fr: string
      name_ar: string
      latitude: number | null
      longitude: number | null
      full_name: string
    }

    CommuneItem: {
      id: number
      name_fr: string
      name_ar: string
      code: string | null
      latitude: number | null
      longitude: number | null
      full_name: string
    }

    PagedWilayaResponse: {
      items: components["schemas"]["WilayaItem"][]
      total: number
      skip: number
      take: number
      success: boolean
    }

    PagedDairaResponse: {
      items: components["schemas"]["DairaItem"][]
      total: number
      skip: number
      take: number
      success: boolean
    }

    PagedCommuneResponse: {
      items: components["schemas"]["CommuneItem"][]
      total: number
      skip: number
      take: number
      success: boolean
    }

    CommuneBoundaryResponse: {
      communeId: number
      communeName?: string
      geometry: string
    }

    ValidateRoadRequest: {
      coordinates: { lat: number; lng: number }[]
    }

    ValidateRoadResponse: {
      valid: boolean
      error: string | null
    }

    ValidateDistrictRequest: {
      coordinates: { lat: number; lng: number }[]
      districtTypeKey?: string
    }

    ValidateDistrictResponse: {
      valid: boolean
      error: string | null
    }

    DistrictCoverageResponse: {
      covered: boolean
      message: string
    }

    MainUrbanExistsResponse: {
      exists: boolean
    }

    RoadSideRequest: {
      roadId: string
      lat: number
      lng: number
    }

    RoadSideResponse: {
      side: "left" | "right"
      suggestedNumber: number
    }

    FieldFeature: {
      id: string
      label: string
      type: string
      data: Record<string, unknown>
      coordinates: Record<string, unknown> | null
    }

    FieldFeaturesResponse: {
      features: components["schemas"]["FieldFeature"][]
      count: number
    }

    FieldInspectRequest: {
      feature_id: string
      type: string
      data: Record<string, unknown>
      status: string
    }

    FieldInspectResponse: {
      success: boolean
      id: string
      message: string
    }

    FieldEntranceCreateRequest: {
      road_id: string
      data: Record<string, unknown>
      label?: string
    }

    FieldEntranceCreateResponse: {
      success: boolean
      id: string
      message: string
    }

    ScatteredRefreshResponse: {
      success: boolean
      geojson: string | null
      message: string
    }

    CustomFeatureTypeRequest: {
      type: string
      label: string
      layers: { key: string; label: string }[]
    }

    FeatureTypeLayer: {
      key: string
      label: string
    }

    FeatureTypeDefinition: {
      key: string
      label: string
      icon: string
      layers: components["schemas"]["FeatureTypeLayer"][]
    }

    LogEntry: {
      level?: string
      code?: string
      message: string
      context?: string
      url?: string
      method?: string
    }

    LogBatch: {
      logs: components["schemas"]["LogEntry"][]
    }
  }
}

export type $defs = Record<string, never>

export type external = Record<string, never>

export type operations = Record<string, never>
