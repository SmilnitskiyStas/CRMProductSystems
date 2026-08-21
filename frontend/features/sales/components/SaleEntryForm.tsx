"use client";

import { useMemo } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useTranslations } from "next-intl";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import type { Product } from "@/features/inventory/types";
import type { UpsertDailySalePayload } from "../types";

// Zod .min(1, message) needs a translated message, so the schema is built once per
// render inside the component (see `useMemo(() => buildSaleSchema(t), [t])` below) —
// mirrors `buildProductSchema(t)` in features/inventory/components/ProductForm.tsx.
function buildSaleSchema(t: ReturnType<typeof useTranslations>) {
  return z.object({
    productId: z.string().min(1, t("validation.selectProduct")),
    date: z.string().min(1, t("validation.enterDate")),
    quantitySold: z.coerce.number().min(0, t("validation.negativeQuantity")),
    quantityEndOfDay: z.coerce.number().min(0).optional().or(z.literal("")),
    isPromoDay: z.boolean(),
  });
}

type FormValues = z.infer<ReturnType<typeof buildSaleSchema>>;

interface Props {
  /** Store to log the sale against — the header's global selector, not a field in this form. */
  storeId: string;
  products: Product[];
  isPending: boolean;
  error: string | null;
  onClose: () => void;
  onSubmit: (payload: UpsertDailySalePayload) => void;
}

const inputStyle: React.CSSProperties = {
  width: "100%",
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "8px 12px",
  outline: "none",
  boxSizing: "border-box",
};

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 5,
};

const errStyle: React.CSSProperties = { color: "#F87171", fontSize: 11, marginTop: 3 };

export function SaleEntryForm({
  storeId, products, isPending, error, onClose, onSubmit,
}: Props) {
  const t = useTranslations("Dashboard.sales.entryForm");
  const tCommon = useTranslations("Common");
  const saleSchema = useMemo(() => buildSaleSchema(t), [t]);

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(saleSchema),
    defaultValues: {
      productId: "",
      date: new Date().toISOString().slice(0, 10),
      quantitySold: 0,
      quantityEndOfDay: "",
      isPromoDay: false,
    },
  });

  function submit(values: FormValues) {
    onSubmit({
      storeId,
      productId: values.productId,
      date: values.date,
      quantitySold: values.quantitySold,
      quantityEndOfDay: values.quantityEndOfDay === "" ? null : Number(values.quantityEndOfDay),
      isPromoDay: values.isPromoDay,
    });
  }

  return (
    <Modal title={t("title")} onClose={onClose}>
      <form onSubmit={handleSubmit(submit)} style={{ display: "grid", gap: 14 }}>
        <div>
          <label style={labelStyle}>{t("productLabel")}</label>
          <select {...register("productId")} style={inputStyle}>
            <option value="">{t("selectProductPlaceholder")}</option>
            {products.map((p) => (
              <option key={p.id} value={p.id}>{p.name}</option>
            ))}
          </select>
          {errors.productId && <div style={errStyle}>{errors.productId.message}</div>}
        </div>

        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 12 }}>
          <div>
            <label style={labelStyle}>{t("dateLabel")}</label>
            <input type="date" {...register("date")} style={inputStyle}
              max={new Date().toISOString().slice(0, 10)} />
            {errors.date && <div style={errStyle}>{errors.date.message}</div>}
          </div>
          <div>
            <label style={labelStyle}>{t("quantitySoldLabel")}</label>
            <input type="number" step="0.01" min={0} {...register("quantitySold")} style={inputStyle} />
            {errors.quantitySold && <div style={errStyle}>{errors.quantitySold.message}</div>}
          </div>
          <div>
            <label style={labelStyle}>{t("quantityEndOfDayLabel")}</label>
            <input type="number" step="0.01" min={0} placeholder="—"
              {...register("quantityEndOfDay")} style={inputStyle} />
          </div>
        </div>

        <label style={{ display: "flex", alignItems: "center", gap: 8, color: "#9CA3AF", fontSize: 13 }}>
          <input type="checkbox" {...register("isPromoDay")} />
          {t("promoLabel")}
        </label>

        {error && <div style={{ ...errStyle, fontSize: 13 }}>{error}</div>}

        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 6 }}>
          <Btn variant="ghost" type="button" onClick={onClose}>{tCommon("cancel")}</Btn>
          <Btn type="submit" disabled={isPending}>
            {isPending ? t("saving") : t("save")}
          </Btn>
        </div>
      </form>
    </Modal>
  );
}
