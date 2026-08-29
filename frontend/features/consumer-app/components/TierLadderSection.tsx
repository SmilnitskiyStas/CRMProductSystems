"use client";

import { useEffect, useMemo, useState } from "react";
import { useFieldArray, useForm, useWatch, type Control, type UseFormRegister, type UseFormSetValue } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { ChevronDown, ChevronUp, ImagePlus, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { Switch } from "@/components/ui/switch";
import { ApiError, API_BASE } from "@/lib/api";
import { uploadLoyaltyTierImage } from "../api/loyaltyTiers";
import { useLoyaltyTiers, useUpdateLoyaltyTiers } from "../hooks/useLoyaltyTiers";
import { useLoyaltySettings } from "../hooks/useLoyaltySettings";
import { useUnsavedChangesGuard } from "../hooks/useUnsavedChangesGuard";
import type { LoyaltyTierDefinitionDto, UpsertTierRequest } from "../types";

const card: React.CSSProperties = { background: "#0D1117", border: "1px solid #1F2937", borderRadius: 12, padding: 20 };
const input: React.CSSProperties = { width: "100%", background: "#0D1117", border: "1px solid #374151", borderRadius: 8, padding: "9px 11px", color: "#E8EDF5", fontSize: 13, boxSizing: "border-box" };
const label: React.CSSProperties = { color: "#9CA3AF", fontSize: 12, fontWeight: 600, display: "block", marginBottom: 6 };
const hint: React.CSSProperties = { color: "#6B7280", fontSize: 11, margin: "4px 0 0" };

const optionalNumber = z.preprocess((value) => value === "" || Number.isNaN(value) ? null : value, z.number().min(0).nullable());
const schema = z.object({ tiers: z.array(z.object({
  id: z.string().nullable(), name: z.string().trim().min(1).max(100), description: z.string().max(1000), imageUrl: z.string().nullable(),
  minCompositeScore: z.number().min(0), accrualMultiplier: z.number().min(0).max(100), discountPercent: z.number().min(0).max(100),
  requireCompletedProfile: z.boolean(), minMembershipDays: optionalNumber, minEarnedBonuses: optionalNumber, minCashSpend: optionalNumber,
  minBonusSpend: optionalNumber, minPurchaseCount: optionalNumber, minReviewCount: optionalNumber,
})) });
type FormValues = z.infer<typeof schema>;
type TierRowValue = FormValues["tiers"][number];

const blankTier = (defaultCashbackPercent: number): TierRowValue => ({ id: null, name: "", description: "", imageUrl: null, minCompositeScore: 0, accrualMultiplier: defaultCashbackPercent, discountPercent: 0, requireCompletedProfile: false, minMembershipDays: null, minEarnedBonuses: null, minCashSpend: null, minBonusSpend: null, minPurchaseCount: null, minReviewCount: null });
const fromDto = (tier: LoyaltyTierDefinitionDto): TierRowValue => ({ id: tier.id, name: tier.name, description: tier.description ?? "", imageUrl: tier.imageUrl, minCompositeScore: tier.minCompositeScore, accrualMultiplier: tier.accrualMultiplier, discountPercent: tier.discountPercent, requireCompletedProfile: tier.requireCompletedProfile, minMembershipDays: tier.minMembershipDays, minEarnedBonuses: tier.minEarnedBonuses, minCashSpend: tier.minCashSpend, minBonusSpend: tier.minBonusSpend, minPurchaseCount: tier.minPurchaseCount, minReviewCount: tier.minReviewCount });
const toRequest = (tier: TierRowValue, sortOrder: number): UpsertTierRequest => ({ name: tier.name.trim(), description: tier.description.trim() || null, imageUrl: tier.imageUrl, sortOrder, minCompositeScore: tier.minCompositeScore, accrualMultiplier: tier.accrualMultiplier, discountPercent: tier.discountPercent, requireCompletedProfile: tier.requireCompletedProfile, minMembershipDays: tier.minMembershipDays, minEarnedBonuses: tier.minEarnedBonuses, minCashSpend: tier.minCashSpend, minBonusSpend: tier.minBonusSpend, minPurchaseCount: tier.minPurchaseCount, minReviewCount: tier.minReviewCount });

export function TierLadderSection() {
  const query = useLoyaltyTiers();
  const settings = useLoyaltySettings();
  const baseAccrualRate = settings.data?.accrualRatePercent ?? 0;
  const update = useUpdateLoyaltyTiers();
  const [files, setFiles] = useState<Record<string, File>>({});
  const [error, setError] = useState<string | null>(null);
  const { control, register, reset, setValue, handleSubmit, formState: { isDirty, errors } } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { tiers: [] } });
  const { fields, append, remove, move } = useFieldArray({ control, name: "tiers" });
  useUnsavedChangesGuard(isDirty || Object.keys(files).length > 0, "Є незбережені зміни у рівнях лояльності.");
  useEffect(() => { if (query.data) reset({ tiers: query.data.map(fromDto) }); }, [query.data, reset]);

  async function save(values: FormValues) {
    setError(null);
    try {
      const fileByIndex = fields.map((field) => files[field.id]);
      const saved = await update.mutateAsync(values.tiers.map(toRequest));
      for (let index = 0; index < saved.length; index++) if (fileByIndex[index]) await uploadLoyaltyTierImage(saved[index].id, fileByIndex[index]);
      setFiles({}); await query.refetch(); toast.success("Програму лояльності збережено");
    } catch (cause) { setError(cause instanceof ApiError || cause instanceof Error ? cause.message : "Не вдалося зберегти рівні"); }
  }

  if (query.isLoading) return <div style={card}>Завантаження рівнів…</div>;
  if (query.isError) return <div style={{ ...card, color: "#F87171" }}>Не вдалося завантажити рівні.</div>;
  return <section style={{ ...card, width: "100%", boxSizing: "border-box" }}>
    <div style={{ display: "flex", justifyContent: "space-between", gap: 16, alignItems: "flex-start" }}><div><h2 style={{ color: "#E8EDF5", fontSize: 18, margin: 0 }}>Рівні та прогресія</h2><p style={hint}>Для кожного рівня задайте власний кешбек і всі обов’язкові умови переходу.</p></div><Btn size="sm" variant="ghost" icon={<Plus size={14} />} onClick={() => append(blankTier(baseAccrualRate || 3))}>Додати рівень</Btn></div>
    <form onSubmit={handleSubmit(save)} style={{ marginTop: 18 }}><div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
      {fields.map((field, index) => <TierCard key={field.id} fieldId={field.id} index={index} count={fields.length} control={control} register={register} setValue={setValue} baseAccrualRate={baseAccrualRate} file={files[field.id]} onFile={(file) => setFiles((current) => ({ ...current, [field.id]: file }))} onRemove={() => remove(index)} onMoveUp={() => move(index, index - 1)} onMoveDown={() => move(index, index + 1)} />)}
    </div>{fields.length === 0 && <p style={{ ...hint, padding: 20, textAlign: "center" }}>Додайте перший рівень програми лояльності.</p>}{errors.tiers && <p style={{ color: "#F87171", fontSize: 12 }}>Перевірте назви та числові значення рівнів.</p>}{error && <p style={{ color: "#F87171", fontSize: 12 }}>{error}</p>}<div style={{ borderTop: "1px solid #1F2937", marginTop: 18, paddingTop: 18 }}><Btn type="submit" disabled={update.isPending || (!isDirty && Object.keys(files).length === 0)}>{update.isPending ? "Збереження…" : "Зберегти рівні"}</Btn></div></form>
  </section>;
}

function TierCard({ fieldId, index, count, control, register, setValue, baseAccrualRate, file, onFile, onRemove, onMoveUp, onMoveDown }: { fieldId: string; index: number; count: number; control: Control<FormValues>; register: UseFormRegister<FormValues>; setValue: UseFormSetValue<FormValues>; baseAccrualRate: number; file?: File; onFile: (file: File) => void; onRemove: () => void; onMoveUp: () => void; onMoveDown: () => void }) {
  const [isExpanded, setIsExpanded] = useState(false);
  const row = useWatch({ control, name: `tiers.${index}` });
  const preview = useMemo(() => file ? URL.createObjectURL(file) : row?.imageUrl ? `${API_BASE}${row.imageUrl}` : null, [file, row?.imageUrl]);
  const criteria = [["minMembershipDays", "Днів від приєднання", 1], ["minEarnedBonuses", "Накопичено бонусів", .01], ["minCashSpend", "Оплачено грошима", .01], ["minBonusSpend", "Оплачено бонусами", .01], ["minPurchaseCount", "Кількість покупок", 1], ["minReviewCount", "Кількість відгуків", 1]] as const;
  return <article style={{ background: "#161B26", border: "1px solid #293241", borderRadius: 10, padding: 16 }}>
    <button type="button" aria-expanded={isExpanded} onClick={() => setIsExpanded((current) => !current)} style={{ width: "100%", display: "flex", alignItems: "center", justifyContent: "space-between", gap: 12, padding: 0, margin: 0, border: 0, background: "transparent", color: "#E8EDF5", cursor: "pointer", textAlign: "left" }}>
      <span style={{ display: "flex", alignItems: "center", gap: 10, minWidth: 0 }}>
        <span style={{ color: "#6B7280", fontSize: 12, flexShrink: 0 }}>Рівень {index + 1}</span>
        <strong style={{ fontSize: 14, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{row?.name?.trim() || "Новий рівень"}</strong>
      </span>
      <span style={{ display: "flex", alignItems: "center", gap: 6, color: "#9CA3AF", fontSize: 12, flexShrink: 0 }}>
        {isExpanded ? "Згорнути" : "Розгорнути"}
        {isExpanded ? <ChevronUp size={16} /> : <ChevronDown size={16} />}
      </span>
    </button>
    {isExpanded && <div style={{ marginTop: 16 }}>
    <div style={{ display: "flex", gap: 14 }}><label style={{ width: 92, height: 92, border: "1px dashed #4B5563", borderRadius: 12, display: "flex", alignItems: "center", justifyContent: "center", overflow: "hidden", cursor: "pointer", flexShrink: 0 }}>{preview ? <img src={preview} alt="" style={{ width: "100%", height: "100%", objectFit: "cover" }} /> : <ImagePlus size={24} color="#6B7280" />}<input type="file" accept="image/png,image/jpeg,image/webp" hidden onChange={(e) => { const selected = e.target.files?.[0]; if (selected) onFile(selected); }} /></label>
      <div style={{ flex: 1, display: "grid", gridTemplateColumns: "2fr 1fr 1fr", gap: 12 }}><div><label style={label}>Назва рівня</label><input {...register(`tiers.${index}.name`)} placeholder="Наприклад, Срібло" style={input} /></div><div><label style={label}>Кешбек рівня, %</label><input type="number" min={0} max={100} step="0.01" {...register(`tiers.${index}.accrualMultiplier`, { valueAsNumber: true })} style={input} /><p style={hint}>Нараховується від суми покупки.</p></div><div><label style={label}>Знижка, %</label><input type="number" min={0} max={100} step="0.01" {...register(`tiers.${index}.discountPercent`, { valueAsNumber: true })} style={input} /></div><div style={{ gridColumn: "1 / -1" }}><label style={label}>Опис для покупця</label><textarea {...register(`tiers.${index}.description`)} rows={2} placeholder="Поясніть переваги та умови рівня" style={{ ...input, resize: "vertical" }} /></div></div>
    </div>
    <div style={{ marginTop: 16, borderTop: "1px solid #293241", paddingTop: 14 }}><h3 style={{ color: "#D1D5DB", fontSize: 13, margin: "0 0 10px" }}>Умови переходу на цей рівень</h3><RequirementToggle active={row?.requireCompletedProfile ?? false} label="Профіль покупця повністю заповнений" onChange={(active) => setValue(`tiers.${index}.requireCompletedProfile`, active, { shouldDirty: true })} /><div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))", gap: 10, marginTop: 10 }}>{criteria.map(([key, title, step]) => <NumericRequirement key={key} label={title} value={row?.[key] ?? null} step={step} onChange={(value) => setValue(`tiers.${index}.${key}`, value, { shouldDirty: true })} />)}</div><details style={{ marginTop: 10 }}><summary style={{ color: "#6B7280", fontSize: 11, cursor: "pointer" }}>Сумісність зі старим RFM-рейтингом</summary><div style={{ marginTop: 8, maxWidth: 260 }}><label style={label}>Мінімальний RFM-рейтинг</label><input type="number" min={0} step="0.01" {...register(`tiers.${index}.minCompositeScore`, { valueAsNumber: true })} style={input} /><p style={hint}>Застосовується лише коли жодну явну умову вище не вибрано.</p></div></details></div>
    <div style={{ display: "flex", justifyContent: "space-between", marginTop: 14 }}><div style={{ display: "flex", gap: 6 }}><Btn type="button" size="sm" variant="ghost" disabled={index === 0} onClick={onMoveUp}>Вище</Btn><Btn type="button" size="sm" variant="ghost" disabled={index === count - 1} onClick={onMoveDown}>Нижче</Btn></div><Btn type="button" size="sm" variant="ghost" icon={<Trash2 size={13} />} onClick={onRemove}>Видалити</Btn></div>
    </div>}
  </article>;
}

function RequirementToggle({ active, label: text, onChange }: { active: boolean; label: string; onChange: (active: boolean) => void }) { return <div style={{ display: "flex", alignItems: "center", gap: 10 }}><Switch checked={active} onCheckedChange={onChange} /><span style={{ color: "#D1D5DB", fontSize: 12 }}>{text}</span></div>; }
function NumericRequirement({ label: text, value, step, onChange }: { label: string; value: number | null; step: number; onChange: (value: number | null) => void }) { const active = value !== null; return <div style={{ display: "grid", gridTemplateColumns: "auto 1fr 120px", alignItems: "center", gap: 10, background: "#0D1117", borderRadius: 8, padding: 10 }}><Switch checked={active} onCheckedChange={(checked) => onChange(checked ? 0 : null)} /><span style={{ color: active ? "#D1D5DB" : "#6B7280", fontSize: 12 }}>{text}</span><input disabled={!active} type="number" min={0} step={step} value={value ?? ""} onChange={(e) => onChange(e.target.value === "" ? 0 : Number(e.target.value))} style={{ ...input, opacity: active ? 1 : .5 }} /></div>; }
