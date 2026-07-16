"use client";

import { useMemo, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { CheckCircle2, Loader2, Send } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { submitLead } from "../api/leads";

function buildLeadSchema(t: ReturnType<typeof useTranslations>) {
  return z.object({
    name: z.string().trim().min(2, t("validation.nameRequired")),
    phone: z
      .string()
      .trim()
      .min(7, t("validation.phoneRequired"))
      .regex(/^\+?[\d\s\-()]{7,20}$/, t("validation.phoneInvalid")),
    company: z.string().trim().max(200, t("validation.companyTooLong")).optional().or(z.literal("")),
    message: z
      .string()
      .trim()
      .max(2000, t("validation.messageTooLong"))
      .optional()
      .or(z.literal("")),
    website: z.string().optional(), // honeypot
  });
}

type LeadFormValues = z.infer<ReturnType<typeof buildLeadSchema>>;

const inputClass =
  "w-full rounded-md border border-white/10 bg-white/[0.03] px-3.5 py-2.5 text-[15px] text-white placeholder:text-slate-500 transition-colors focus:border-[#2D7DD2] focus:outline-none focus:ring-1 focus:ring-[#2D7DD2]";

export function LeadForm() {
  const t = useTranslations("Landing.leadForm");
  const [submitted, setSubmitted] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);

  const leadSchema = useMemo(() => buildLeadSchema(t), [t]);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LeadFormValues>({
    resolver: zodResolver(leadSchema),
    defaultValues: { name: "", phone: "", company: "", message: "", website: "" },
  });

  const onSubmit = async (values: LeadFormValues) => {
    setServerError(null);
    const result = await submitLead({
      name: values.name,
      phone: values.phone,
      company: values.company ?? "",
      message: values.message ?? "",
      website: values.website ?? "",
    });
    if (result.ok) {
      setSubmitted(true);
    } else {
      setServerError(result.error);
    }
  };

  if (submitted) {
    return (
      <div className="flex flex-col items-center rounded-xl border border-[#22c55e]/25 bg-[#22c55e]/[0.06] px-6 py-12 text-center">
        <CheckCircle2 className="h-12 w-12 text-[#22c55e]" aria-hidden="true" />
        <h3 className="mt-4 text-xl font-semibold text-white">{t("successTitle")}</h3>
        <p className="mt-2 text-slate-400">{t("successText")}</p>
      </div>
    );
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2">
        <div>
          <label htmlFor="lead-name" className="mb-1.5 block text-sm font-medium text-slate-300">
            {t("nameLabel")} <span className="text-[#ef4444]">*</span>
          </label>
          <input
            id="lead-name"
            type="text"
            autoComplete="name"
            placeholder={t("namePlaceholder")}
            className={inputClass}
            {...register("name")}
          />
          {errors.name && <p className="mt-1.5 text-sm text-[#ef4444]">{errors.name.message}</p>}
        </div>
        <div>
          <label htmlFor="lead-phone" className="mb-1.5 block text-sm font-medium text-slate-300">
            {t("phoneLabel")} <span className="text-[#ef4444]">*</span>
          </label>
          <input
            id="lead-phone"
            type="tel"
            autoComplete="tel"
            placeholder={t("phonePlaceholder")}
            className={inputClass}
            {...register("phone")}
          />
          {errors.phone && <p className="mt-1.5 text-sm text-[#ef4444]">{errors.phone.message}</p>}
        </div>
      </div>

      <div>
        <label htmlFor="lead-company" className="mb-1.5 block text-sm font-medium text-slate-300">
          {t("companyLabel")}
        </label>
        <input
          id="lead-company"
          type="text"
          autoComplete="organization"
          placeholder={t("companyPlaceholder")}
          className={inputClass}
          {...register("company")}
        />
        {errors.company && (
          <p className="mt-1.5 text-sm text-[#ef4444]">{errors.company.message}</p>
        )}
      </div>

      <div>
        <label htmlFor="lead-message" className="mb-1.5 block text-sm font-medium text-slate-300">
          {t("messageLabel")}
        </label>
        <textarea
          id="lead-message"
          rows={4}
          placeholder={t("messagePlaceholder")}
          className={`${inputClass} resize-none`}
          {...register("message")}
        />
        {errors.message && (
          <p className="mt-1.5 text-sm text-[#ef4444]">{errors.message.message}</p>
        )}
      </div>

      {/* Honeypot — invisible for humans, bots fill it in */}
      <div style={{ display: "none" }} aria-hidden="true">
        <label htmlFor="lead-website">Website</label>
        <input id="lead-website" type="text" tabIndex={-1} autoComplete="off" {...register("website")} />
      </div>

      {serverError && (
        <div
          role="alert"
          className="rounded-md border border-[#ef4444]/30 bg-[#ef4444]/[0.08] px-4 py-3 text-sm text-[#fca5a5]"
        >
          {serverError}
        </div>
      )}

      <Button type="submit" size="lg" disabled={isSubmitting} className="w-full sm:w-auto">
        {isSubmitting ? (
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
        ) : (
          <Send className="h-4 w-4" aria-hidden="true" />
        )}
        {isSubmitting ? t("submitting") : t("submit")}
      </Button>
    </form>
  );
}
