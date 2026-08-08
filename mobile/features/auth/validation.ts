import { z } from 'zod';

export const staffLoginSchema = z.object({
  email: z
    .string()
    .min(1, 'Введіть email')
    .pipe(z.string().email('Невірний email')),
  password: z.string().min(1, 'Введіть пароль'),
});

export const mobileLoginSchema = z.object({
  identifier: z.string().trim().min(1, 'Введіть email або номер телефону'),
  password: z.string().min(1, 'Введіть пароль'),
});

// Mirrors backend ShelfGuard.Application.Common.PasswordValidator (MinLength=12, needs a
// letter + a digit). The common-password list and email-substring check remain server-authoritative.
export const consumerRegisterSchema = z.object({
  fullName: z.string().trim().min(1, "Введіть ім'я та прізвище"),
  phone: z.string().min(1, 'Введіть номер телефону'),
  password: z
    .string()
    .min(1, 'Введіть пароль')
    .min(12, 'Щонайменше 12 символів')
    .refine((value) => /[a-zA-Z]/.test(value), 'Потрібна хоча б одна літера')
    .refine((value) => /[0-9]/.test(value), 'Потрібна хоча б одна цифра'),
  email: z.union([z.string().email('Невірний email'), z.literal('')]).optional(),
});

export type StaffLoginFormData = z.infer<typeof staffLoginSchema>;
export type MobileLoginFormData = z.infer<typeof mobileLoginSchema>;
export type ConsumerRegisterFormData = z.infer<typeof consumerRegisterSchema>;
