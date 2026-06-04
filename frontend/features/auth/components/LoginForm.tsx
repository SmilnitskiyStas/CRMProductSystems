"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { useLogin } from "../hooks/useAuth";

const schema = z.object({
  email:    z.string().email("Введіть коректний email"),
  password: z.string().min(1, "Пароль обов'язковий"),
});

type FormValues = z.infer<typeof schema>;

export function LoginForm() {
  const login = useLogin();

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  const onSubmit = (values: FormValues) => {
    login.mutate({ email: values.email, password: values.password });
  };

  return (
    <form onSubmit={handleSubmit(onSubmit)} noValidate style={{ display: "flex", flexDirection: "column", gap: 20 }}>

      {/* Email */}
      <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
        <label style={{ fontSize: 12, fontWeight: 500, color: "#8A94A8", fontFamily: '"Inter", sans-serif', letterSpacing: "0.03em" }}>
          EMAIL
        </label>
        <input
          {...register("email")}
          type="email"
          autoComplete="email"
          placeholder="you@company.com"
          style={{
            background: "#0F1117",
            border: `1px solid ${errors.email ? "#EF4444" : "#2A3347"}`,
            borderRadius: 4,
            padding: "10px 14px",
            color: "#E8EDF5",
            fontSize: 14,
            fontFamily: '"Inter", sans-serif',
            outline: "none",
            transition: "border-color 0.15s",
          }}
          onFocus={(e) => { e.currentTarget.style.borderColor = errors.email ? "#EF4444" : "#2D7DD2"; }}
          onBlur={(e)  => { e.currentTarget.style.borderColor = errors.email ? "#EF4444" : "#2A3347"; }}
        />
        {errors.email && (
          <span style={{ fontSize: 11, color: "#EF4444", fontFamily: '"Inter", sans-serif' }}>
            {errors.email.message}
          </span>
        )}
      </div>

      {/* Password */}
      <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
        <label style={{ fontSize: 12, fontWeight: 500, color: "#8A94A8", fontFamily: '"Inter", sans-serif', letterSpacing: "0.03em" }}>
          ПАРОЛЬ
        </label>
        <input
          {...register("password")}
          type="password"
          autoComplete="current-password"
          placeholder="••••••••"
          style={{
            background: "#0F1117",
            border: `1px solid ${errors.password ? "#EF4444" : "#2A3347"}`,
            borderRadius: 4,
            padding: "10px 14px",
            color: "#E8EDF5",
            fontSize: 14,
            fontFamily: '"Inter", sans-serif',
            outline: "none",
            transition: "border-color 0.15s",
          }}
          onFocus={(e) => { e.currentTarget.style.borderColor = errors.password ? "#EF4444" : "#2D7DD2"; }}
          onBlur={(e)  => { e.currentTarget.style.borderColor = errors.password ? "#EF4444" : "#2A3347"; }}
        />
        {errors.password && (
          <span style={{ fontSize: 11, color: "#EF4444", fontFamily: '"Inter", sans-serif' }}>
            {errors.password.message}
          </span>
        )}
      </div>

      {/* API error */}
      {login.error && (
        <div style={{
          background: "#EF44441A",
          border: "1px solid #EF444440",
          borderRadius: 4,
          padding: "10px 14px",
          color: "#EF4444",
          fontSize: 13,
          fontFamily: '"Inter", sans-serif',
        }}>
          {login.error.message}
        </div>
      )}

      {/* Submit */}
      <button
        type="submit"
        disabled={login.isPending}
        style={{
          background: login.isPending ? "#2D7DD280" : "#2D7DD2",
          color: "#fff",
          border: "none",
          borderRadius: 4,
          padding: "11px 0",
          fontSize: 14,
          fontWeight: 600,
          fontFamily: '"Inter", sans-serif',
          cursor: login.isPending ? "not-allowed" : "pointer",
          transition: "background 0.15s",
          letterSpacing: "0.01em",
        }}
        onMouseEnter={(e) => { if (!login.isPending) e.currentTarget.style.background = "#3A8FE8"; }}
        onMouseLeave={(e) => { if (!login.isPending) e.currentTarget.style.background = "#2D7DD2"; }}
      >
        {login.isPending ? "Вхід…" : "Увійти"}
      </button>
    </form>
  );
}
