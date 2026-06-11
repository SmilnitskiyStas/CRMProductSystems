"use client";

import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import type { Product } from "@/features/inventory/types";
import type { StoreDto as Store } from "@/features/stores/types";
import type { UpsertDailySalePayload } from "../types";

const saleSchema = z.object({
  storeId: z.string().min(1, "Оберіть магазин"),
  productId: z.string().min(1, "Оберіть товар"),
  date: z.string().min(1, "Вкажіть дату"),
  quantitySold: z.coerce.number().min(0, "Не може бути від'ємною"),
  quantityEndOfDay: z.coerce.number().min(0).optional().or(z.literal("")),
  isPromoDay: z.boolean(),
});

type FormValues = z.infer<typeof saleSchema>;

interface Props {
  stores: Store[];
  products: Product[];
  defaultStoreId: string;
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
  stores, products, defaultStoreId, isPending, error, onClose, onSubmit,
}: Props) {
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(saleSchema),
    defaultValues: {
      storeId: defaultStoreId,
      productId: "",
      date: new Date().toISOString().slice(0, 10),
      quantitySold: 0,
      quantityEndOfDay: "",
      isPromoDay: false,
    },
  });

  function submit(values: FormValues) {
    onSubmit({
      storeId: values.storeId,
      productId: values.productId,
      date: values.date,
      quantitySold: values.quantitySold,
      quantityEndOfDay: values.quantityEndOfDay === "" ? null : Number(values.quantityEndOfDay),
      isPromoDay: values.isPromoDay,
    });
  }

  return (
    <Modal title="Внести продажі за день" onClose={onClose}>
      <form onSubmit={handleSubmit(submit)} style={{ display: "grid", gap: 14 }}>
        <div>
          <label style={labelStyle}>Магазин</label>
          <select {...register("storeId")} style={inputStyle}>
            {stores.map((s) => (
              <option key={s.id} value={s.id}>{s.name}</option>
            ))}
          </select>
          {errors.storeId && <div style={errStyle}>{errors.storeId.message}</div>}
        </div>

        <div>
          <label style={labelStyle}>Товар</label>
          <select {...register("productId")} style={inputStyle}>
            <option value="">— оберіть товар —</option>
            {products.map((p) => (
              <option key={p.id} value={p.id}>{p.name}</option>
            ))}
          </select>
          {errors.productId && <div style={errStyle}>{errors.productId.message}</div>}
        </div>

        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 12 }}>
          <div>
            <label style={labelStyle}>Дата</label>
            <input type="date" {...register("date")} style={inputStyle}
              max={new Date().toISOString().slice(0, 10)} />
            {errors.date && <div style={errStyle}>{errors.date.message}</div>}
          </div>
          <div>
            <label style={labelStyle}>Продано, шт</label>
            <input type="number" step="0.01" min={0} {...register("quantitySold")} style={inputStyle} />
            {errors.quantitySold && <div style={errStyle}>{errors.quantitySold.message}</div>}
          </div>
          <div>
            <label style={labelStyle}>Залишок на кінець дня</label>
            <input type="number" step="0.01" min={0} placeholder="—"
              {...register("quantityEndOfDay")} style={inputStyle} />
          </div>
        </div>

        <label style={{ display: "flex", alignItems: "center", gap: 8, color: "#9CA3AF", fontSize: 13 }}>
          <input type="checkbox" {...register("isPromoDay")} />
          Акційний день (виключається з розрахунку ADU)
        </label>

        {error && <div style={{ ...errStyle, fontSize: 13 }}>{error}</div>}

        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 6 }}>
          <Btn variant="ghost" type="button" onClick={onClose}>Скасувати</Btn>
          <Btn type="submit" disabled={isPending}>
            {isPending ? "Збереження…" : "Зберегти"}
          </Btn>
        </div>
      </form>
    </Modal>
  );
}
