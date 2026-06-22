export interface AuthUserDto {
  id: string;
  email: string;
  fullName: string;
  role: string;
  tenantId: string | null;
  storeId: string | null;
  phone?: string | null;
  telegramChatId?: string | null;
  permissions?: Record<string, boolean> | null;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  user: AuthUserDto;
}
