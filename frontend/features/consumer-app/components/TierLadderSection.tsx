"use client";
import { inputStyle as input, labelStyle as label } from "@/components/ui/fieldStyles";

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
import { describeTierRequirements, getTierRatingError, hasTierRequirements, TIER_REQUIREMENT_FIELDS } from "../utils/tierRequirements";
import type { LoyaltyTierDefinitionDto, UpsertTierRequest } from "../types";

const card: React.CSSProperties = { background: "#0D1117", border: "1px solid #1F2937", borderRadius: 12, padding: 20 };
const hint: React.CSSProperties = { color: "#6B7280", fontSize: 11, margin: "4px 0 0" };

const optionalNumber = z.preprocess((value) => value === "" || Number.isNaN(value) ? null : value, z.number().min(0).nullable());
const optionalCount = z.preprocess((value) => value === "" || Number.isNaN(value) ? null : value, z.number().int("Вкажіть ціле число").min(0).nullable());
const schema = z.object({ tiers: z.array(z.object({
  id: z.string().nullable(), name: z.string().trim().min(1).max(100), description: z.string().max(1000), imageUrl: z.string().nullable(),
  minCompositeScore: z.number().min(0), accrualMultiplier: z.number().min(0).max(100), discountPercent: z.number().min(0).max(100),
  requireCompletedProfile: z.boolean(), minMembershipDays: optionalCount, minEarnedBonuses: optionalNumber, minCashSpend: optionalNumber,
  minBonusSpend: optionalNumber, minPurchaseCount: optionalCount, minReviewCount: optionalCount,
})) }).superRefine(({ tiers }, ctx) => {
  tiers.forEach((tier, index) => {
    const message = getTierRatingError(tier);
    if (message) ctx.addIssue({ code: z.ZodIssueCode.custom, path: ["tiers", index, "minCompositeScore"], message });
  });
});
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
    <div style={{ display: "flex", justifyContent: "space-between", gap: 16, alignItems: "flex-start" }}><div><h2 style={{ color: "#E8EDF5", fontSize: 18, margin: 0 }}>Рівні та прогресія</h2><p style={hint}>Для кожного рангу задайте власні переваги та умови його отримання. Покупець має виконати всі вибрані умови одночасно.</p></div><Btn size="sm" variant="ghost" icon={<Plus size={14} />} onClick={() => append(blankTier(baseAccrualRate || 3))}>Додати рівень</Btn></div>
    <div style={{ marginTop: 16, border: "1px solid #3B82F640", background: "#1D346130", borderRadius: 10, padding: 14 }}>
      <strong style={{ color: "#93C5FD", fontSize: 13 }}>Як налаштувати перехід</strong>
      <p style={{ ...hint, color: "#9CA3AF", lineHeight: 1.7 }}>Перший ранг видається після приєднання до програми. Для переходу з «Бронзи» на «Срібло» задавайте умови саме у «Срібла», а для «Золота» — у «Золота». Виберіть потрібні показники та вкажіть пороги вашого бізнесу. У застосунку покупець побачить ці умови та свій прогрес. Зміни застосовуються після збереження, а ранг перераховується щоночі.</p>
    </div>
    <form onSubmit={handleSubmit(save)} style={{ marginTop: 18 }}><div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
      {fields.map((field, index) => <TierCard key={field.id} fieldId={field.id} index={index} count={fields.length} control={control} register={register} setValue={setValue} ratingError={errors.tiers?.[index]?.minCompositeScore?.message} file={files[field.id]} onFile={(file) => setFiles((current) => ({ ...current, [field.id]: file }))} onRemove={() => remove(index)} onMoveUp={() => move(index, index - 1)} onMoveDown={() => move(index, index + 1)} />)}
    </div>{fields.length === 0 && <p style={{ ...hint, padding: 20, textAlign: "center" }}>Додайте перший рівень програми лояльності.</p>}{errors.tiers && <p style={{ color: "#F87171", fontSize: 12 }}>Перевірте назви та числові значення рівнів.</p>}{error && <p style={{ color: "#F87171", fontSize: 12 }}>{error}</p>}<div style={{ borderTop: "1px solid #1F2937", marginTop: 18, paddingTop: 18 }}><Btn type="submit" disabled={update.isPending || (!isDirty && Object.keys(files).length === 0)}>{update.isPending ? "Збереження…" : "Зберегти рівні"}</Btn></div></form>
  </section>;
}

function TierCard({ fieldId, index, count, control, register, setValue, ratingError, file, onFile, onRemove, onMoveUp, onMoveDown }: { fieldId: string; index: number; count: number; control: Control<FormValues>; register: UseFormRegister<FormValues>; setValue: UseFormSetValue<FormValues>; ratingError?: string; file?: File; onFile: (file: File) => void; onRemove: () => void; onMoveUp: () => void; onMoveDown: () => void }) {
  const row = useWatch({ control, name: `tiers.${index}` });
  const [isExpanded, setIsExpanded] = useState(!row?.id);
  const [editingRequirements, setEditingRequirements] = useState((!row?.id && index > 0) || !!getTierRatingError(row ?? {}));
  const preview = useMemo(() => file ? URL.createObjectURL(file) : row?.imageUrl ? `${API_BASE}${row.imageUrl}` : null, [file, row?.imageUrl]);
  const previousName = useWatch({ control, name: `tiers.${Math.max(0, index - 1)}.name` });
  const conditions = describeTierRequirements(row ?? {});
  const rulesError = ratingError ?? getTierRatingError(row ?? {});
  const tierName = row?.name?.trim() || `Рівень ${index + 1}`;
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

    <div style={{ display: "flex", justifyContent: "space-between", marginTop: 14 }}><div style={{ display: "flex", gap: 6 }}><Btn type="button" size="sm" variant="ghost" disabled={index === 0} onClick={onMoveUp}>Вище</Btn><Btn type="button" size="sm" variant="ghost" disabled={index === count - 1} onClick={onMoveDown}>Нижче</Btn></div><Btn type="button" size="sm" variant="ghost" icon={<Trash2 size={13} />} onClick={onRemove}>Видалити</Btn></div>
    </div>}
    <div style={{ marginTop: 16, borderTop: "1px solid #293241", paddingTop: 14 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", gap: 12, flexWrap: "wrap" }}>
        <div><h3 style={{ color: "#E8EDF5", fontSize: 14, margin: 0 }}>{index === 0 ? 'Початковий ранг' : `Перехід: ${previousName?.trim() || 'попередній ранг'} → ${tierName}`}</h3>
          <p style={hint}>{conditions.length ? `Обов’язкових умов: ${conditions.length}. Потрібно виконати всі.` : index === 0 ? 'Присвоюється після приєднання до програми.' : 'Конкретні умови покупок, бонусів чи участі поки не вибрано.'}</p>
        </div>
        <button type="button" aria-expanded={editingRequirements} aria-controls={`tier-rules-${fieldId}`} aria-label={`${editingRequirements ? 'Згорнути' : 'Налаштувати'} умови отримання ${tierName}`} onClick={() => setEditingRequirements((current) => !current)} style={{ border: "1px solid #374151", background: "transparent", color: "#9CA3AF", borderRadius: 8, padding: "6px 10px", fontSize: 12, cursor: "pointer" }}>{editingRequirements ? 'Згорнути умови' : 'Налаштувати умови'}</button>
      </div>
      {conditions.length > 0 ? <ul style={{ margin: "10px 0 0", paddingLeft: 20, color: "#D1D5DB", fontSize: 12, lineHeight: 1.9 }}>{conditions.map((condition) => <li key={condition}>{condition}</li>)}</ul> : null}
      {index === 0 && conditions.length > 0 ? <p style={{ ...hint, color: "#FBBF24", marginTop: 10 }}>Це перший ранг: його видають одразу при приєднанні. Задані умови перевірятимуться при нічному перерахунку. Якщо вони призначені для наступного рангу, налаштуйте їх у наступній картці.</p> : null}
      {rulesError ? <p role="alert" style={{ color: "#FBBF24", fontSize: 12, lineHeight: 1.6, marginTop: 10 }}>{rulesError}</p> : !hasTierRequirements(row ?? {}) && index > 0 ? <p style={{ ...hint, marginTop: 10 }}>{(row?.minCompositeScore ?? 0) > 0 ? `Поки використовується рейтинг активності: ${row.minCompositeScore} з 5. Це не бонусний баланс.` : 'Без умов і без порогу рейтингу цей ранг не вимагатиме додаткових дій покупця.'}</p> : null}
      {editingRequirements ? <div id={`tier-rules-${fieldId}`} role="region" aria-label={`Умови отримання ${tierName}`} style={{ marginTop: 14, borderRadius: 10, border: "1px solid #374151", padding: 14 }}>
        <h4 style={{ color: "#D1D5DB", fontSize: 13, margin: "0 0 6px" }}>Що потрібно для отримання «{tierName}»</h4>
        <p style={{ ...hint, marginBottom: 14 }}>Увімкніть лише потрібні умови. Кожне значення — мінімум за весь час участі у програмі. Усі вибрані вимоги діють одночасно.</p>
        <RequirementToggle active={row?.requireCompletedProfile ?? false} label="Заповнити ім’я та email у профілі" onChange={(active) => setValue(`tiers.${index}.requireCompletedProfile`, active, { shouldDirty: true, shouldValidate: true })} />
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(260px, 1fr))", gap: 10, marginTop: 10 }}>
          {TIER_REQUIREMENT_FIELDS.map(({ key, label: title, unit, step }) => <NumericRequirement key={key} label={title} unit={unit} value={row?.[key] ?? null} step={step} onChange={(value) => setValue(`tiers.${index}.${key}`, value, { shouldDirty: true, shouldValidate: true })} />)}
        </div>
        <details style={{ marginTop: 14 }}><summary style={{ color: "#9CA3AF", fontSize: 12, cursor: "pointer" }}>Додатково: рейтинг активності замість конкретних умов</summary>
          <div style={{ marginTop: 8, maxWidth: 300 }}><label style={label} htmlFor={`rating-${fieldId}`}>Мінімальний рейтинг, від 0 до 5</label><input id={`rating-${fieldId}`} type="number" min={0} max={hasTierRequirements(row ?? {}) ? undefined : 5} step="0.01" readOnly={hasTierRequirements(row ?? {})} {...register(`tiers.${index}.minCompositeScore`, { valueAsNumber: true })} style={input} /><p style={hint}>{hasTierRequirements(row ?? {}) ? 'Не застосовується, поки вибрано конкретні умови вище.' : 'Оцінка давності, частоти й суми покупок. Значення 100 або 500 недосяжні: максимальний рейтинг — 5.'}</p></div>
        </details>
      </div> : null}
    </div>
  </article>;
}

function RequirementToggle({ active, label: text, onChange }: { active: boolean; label: string; onChange: (active: boolean) => void }) { return <div style={{ display: "flex", alignItems: "center", gap: 10 }}><Switch aria-label={text} checked={active} onCheckedChange={onChange} /><span style={{ color: "#D1D5DB", fontSize: 12 }}>{text}</span></div>; }
function NumericRequirement({ label: text, unit, value, step, onChange }: { label: string; unit: string; value: number | null; step: number; onChange: (value: number | null) => void }) {
  const active = value !== null;
  return <div style={{ background: "#0D1117", borderRadius: 8, padding: 12 }}>
    <div style={{ display: "flex", alignItems: "center", gap: 10 }}><Switch aria-label={`Враховувати: ${text}`} checked={active} onCheckedChange={(checked) => onChange(checked ? 0 : null)} /><span style={{ color: active ? "#D1D5DB" : "#6B7280", fontSize: 12 }}>{text}</span></div>
    {active ? <label style={{ ...hint, display: "block", marginTop: 10 }}>Не менше ({unit})<input aria-label={`Мінімум: ${text}`} type="number" min={0} step={step} value={value ?? ""} onChange={(e) => onChange(e.target.value === "" ? 0 : Number(e.target.value))} style={{ ...input, marginTop: 5 }} /></label> : <p style={{ ...hint, marginTop: 10 }}>Не враховується для цього рангу</p>}
  </div>;
}
