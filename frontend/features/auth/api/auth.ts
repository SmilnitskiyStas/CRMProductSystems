import { api, setToken, clearToken } from "@/lib/api";
import { setStoredUser, clearStoredUser } from "../store";
import type { AuthUserDto, LoginRequest, LoginResponse } from "../types";

export const authApi = {
  login: async (payload: LoginRequest): Promise<LoginResponse> => {
    const res = await api.post<LoginResponse>("/api/auth/login", payload);
    setToken(res.accessToken);
    setStoredUser(res.user);
    return res;
  },

  refresh: async (): Promise<LoginResponse> => {
    const res = await api.post<LoginResponse>("/api/auth/refresh");
    setToken(res.accessToken);
    setStoredUser(res.user);
    return res;
  },

  logout: async (): Promise<void> => {
    try {
      await api.post<void>("/api/auth/logout");
    } finally {
      clearToken();
      clearStoredUser();
    }
  },

  getMe: (): Promise<AuthUserDto> => api.get<AuthUserDto>("/api/auth/me"),
};
