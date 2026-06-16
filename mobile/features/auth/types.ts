export interface AuthUser {
  id: string;
  email: string;
  fullName: string;
  role: string;
  tenantId: string | null;
  locationId: string | null;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  user: AuthUser;
}
