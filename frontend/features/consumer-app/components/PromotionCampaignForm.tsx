"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { Switch } from "@/components/ui/switch";
import { useCatalogProducts } from "@/features/catalog/hooks/useCatalog";
import { useLocations } from "@/features/locations/hooks/useLocations";
import { LocationsMultiSelectDropdown } from "@/features/users/components/LocationsMultiSelectDropdown";
import { API_BASE } from "@/lib/api";
import { useLoyaltyTiers } from "../hooks/useLoyaltyTiers";
import { usePromotionCampaign, useSavePromotionCampaign, useUploadPromotionCampaignImage } from "../hooks/usePromotionCampaigns";
import type { PromotionAudienceType } from "../types";

const inputStyle: React.CSSProperties = { width: "100%", boxSizing: "border-box", background: "#0D1117", border: "1px solid #374151", borderRadius: 8, padding: "9px 12px", color: "#E8EDF5", fontSize: 13, outline: "none" };
const textareaStyle: React.CSSProperties = { ...inputStyle, resize: "vertical", fontFamily: "inherit" };
const labelStyle: React.CSSProperties = { display: "block", color: "#9CA3AF", fontSize: 12, fontWeight: 500, marginBottom: 6 };
const hintStyle: React.CSSProperties = { color: "#4B5563", fontSize: 11, marginTop: 4 };
const sectionStyle: React.CSSProperties = { width: "100%", boxSizing: "border-box", background: "#10151D", border: "1px solid #202938", borderRadius: 10, padding: 16, display: "flex", flexDirection: "column", gap: 14 };
const sectionTitleStyle: React.CSSProperties = { color: "#E8EDF5", fontSize: 13, fontWeight: 700, margin: 0 };
const toInput = (value?: string | null) => value ? value.slice(0, 16) : "";
const toIso = (value: string) => new Date(value).toISOString();
const assetUrl = (value: string | null) => !value ? null : /^https?:\/\//i.test(value) ? value : `${API_BASE.replace(/\/$/, "")}/${value.replace(/^\//, "")}`;

type SelectedProduct = { productId: string; name: string; discountPercent: string };

export function PromotionCampaignForm({ campaignId }: { campaignId: string | null }) {
  const router = useRouter();
  const editing = campaignId !== null;
  const { data: campaign, isLoading } = usePromotionCampaign(campaignId);
  const { data: locations = [] } = useLocations();
  const { data: tiers = [] } = useLoyaltyTiers();
  const save = useSavePromotionCampaign();
  const upload = useUploadPromotionCampaignImage();
  const [pickerOpen, setPickerOpen] = useState(false);
  const [search, setSearch] = useState("");
  const { data: catalog = [], isLoading: catalogLoading } = useCatalogProducts({ search: search || undefined }, { enabled: pickerOpen });

  const [title, setTitle] = useState("");
  const [eyebrow, setEyebrow] = useState("");
  const [description, setDescription] = useState("");
  const [body, setBody] = useState("");
  const [terms, setTerms] = useState("");
  const [startsAt, setStartsAt] = useState(() => new Date().toISOString().slice(0, 16));
  const [endsAt, setEndsAt] = useState("");
  const [locationIds, setLocationIds] = useState<string[]>([]);
  const [audienceType, setAudienceType] = useState<PromotionAudienceType>("all");
  const [audienceTierIds, setAudienceTierIds] = useState<string[]>([]);
  const [products, setProducts] = useState<SelectedProduct[]>([]);
  const [backgroundColor, setBackgroundColor] = useState("#14532D");
  const [accentColor, setAccentColor] = useState("#86EFAC");
  const [publish, setPublish] = useState(false);
  const [image, setImage] = useState<File | null>(null);
  const [imagePreview, setImagePreview] = useState<string | null>(null);
  const [initialized, setInitialized] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const activeLocations = useMemo(() => locations.filter((x) => x.isActive), [locations]);
  const availableProducts = useMemo(() => catalog.filter((p) => p.isActive && !products.some((x) => x.productId === p.id)), [catalog, products]);
  const isPending = save.isPending || upload.isPending;

  useEffect(() => {
    if (!campaign || initialized) return;
    setTitle(campaign.title); setEyebrow(campaign.eyebrow ?? ""); setDescription(campaign.description);
    setBody(campaign.body); setTerms(campaign.terms); setStartsAt(toInput(campaign.startsAt)); setEndsAt(toInput(campaign.endsAt));
    setLocationIds(campaign.locationIds); setAudienceType(campaign.audienceType); setAudienceTierIds(campaign.audienceTierIds);
    setBackgroundColor(campaign.backgroundColor); setAccentColor(campaign.accentColor); setImagePreview(assetUrl(campaign.imageUrl));
    setProducts(campaign.products.map((p) => ({ productId: p.productId, name: p.productName ?? p.productId, discountPercent: String(p.discountPercent) })));
    setInitialized(true);
  }, [campaign, initialized]);

  function toggleLocation(id: string) { setLocationIds((value) => value.includes(id) ? value.filter((x) => x !== id) : [...value, id]); }
  function toggleTier(id: string) { setAudienceTierIds((value) => value.includes(id) ? value.filter((x) => x !== id) : [...value, id]); }

  async function submit(event: React.FormEvent) {
    event.preventDefault(); setFormError(null);
    if (!title.trim() || !description.trim() || !startsAt || locationIds.length === 0 || products.length === 0) { setFormError("Заповніть назву, опис, період, магазини та додайте хоча б один товар."); return; }
    if (audienceType === "loyalty_tiers" && audienceTierIds.length === 0) { setFormError("Оберіть хоча б один рівень лояльності."); return; }
    if (endsAt && new Date(endsAt) <= new Date(startsAt)) { setFormError("Дата завершення повинна бути пізніше дати запуску."); return; }
    try {
      const saved = await save.mutateAsync({ id: campaignId, body: { title: title.trim(), eyebrow: eyebrow.trim() || null, description: description.trim(), body, terms, backgroundColor, accentColor, audienceType, audienceTierIds: audienceType === "loyalty_tiers" ? audienceTierIds : [], startsAt: toIso(startsAt), endsAt: endsAt ? toIso(endsAt) : null, sortOrder: 0, locationIds, products: products.map((p) => ({ productId: p.productId, discountPercent: Number(p.discountPercent) })), publishImmediately: publish } });
      if (image) await upload.mutateAsync({ id: saved.id, file: image });
      toast.success(editing ? "Акцію оновлено" : "Акцію створено"); router.push("/consumer-app/promotions");
    } catch (error) { setFormError(error instanceof Error ? error.message : "Не вдалося зберегти акцію"); }
  }

  return <div style={{ width: "100%", background: "#0D1117", border: "1px solid #1F2937", borderRadius: 14, overflow: "hidden" }}>
    <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", padding: "18px 22px", borderBottom: "1px solid #1F2937" }}>
      <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 }}>{editing ? "Редагування акції" : "Нова акція"}</h2>
      <button type="button" onClick={() => router.push("/consumer-app/promotions")} style={{ background: "transparent", border: "1px solid #1F2937", borderRadius: 8, padding: "5px 9px", color: "#4B5563", fontSize: 16, cursor: "pointer" }}>✕</button>
    </div>
    {editing && isLoading ? <div style={{ padding: 22, color: "#4B5563", fontSize: 13 }}>Завантаження…</div> :
      <form onSubmit={submit} style={{ padding: 18, display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(min(100%, 440px), 1fr))", alignItems: "start", gap: 16, maxWidth: 1240, width: "100%", boxSizing: "border-box", margin: "0 auto" }}>
        <section style={sectionStyle}>
          <h3 style={sectionTitleStyle}>Основний контент</h3>
          <div style={{ display: "grid", gridTemplateColumns: "2fr 1fr", gap: 12 }}><div><label style={labelStyle}>Назва</label><input style={inputStyle} value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Наприклад, Знижки на улюблені товари" /></div><div><label style={labelStyle}>Напис над назвою</label><input style={inputStyle} value={eyebrow} onChange={(e) => setEyebrow(e.target.value)} placeholder="Акція тижня" /></div></div>
          <div><label style={labelStyle}>Короткий опис</label><textarea rows={2} style={textareaStyle} value={description} onChange={(e) => setDescription(e.target.value)} /></div>
          <div><label style={labelStyle}>Повний текст</label><textarea rows={4} style={textareaStyle} value={body} onChange={(e) => setBody(e.target.value)} /><p style={hintStyle}>Кожен рядок відображатиметься окремим абзацом.</p></div>
          <div><label style={labelStyle}>Умови акції</label><textarea rows={3} style={textareaStyle} value={terms} onChange={(e) => setTerms(e.target.value)} /></div>
        </section>

        <section style={sectionStyle}>
          <h3 style={sectionTitleStyle}>Оформлення</h3>
          <div><label style={labelStyle}>Фото / банер акції</label><div style={{ display: "flex", alignItems: "center", gap: 12 }}>
            {imagePreview && <div role="img" aria-label="Фото акції" style={{ width: 60, height: 60, flexShrink: 0, backgroundImage: `url("${imagePreview}")`, backgroundSize: "cover", backgroundPosition: "center", borderRadius: 8, border: "1px solid #1F2937" }} />}
            <label style={{ padding: "8px 14px", borderRadius: 8, cursor: "pointer", background: "#111827", border: "1px solid #374151", color: "#9CA3AF", fontSize: 12 }}>{imagePreview ? "Замінити" : "Завантажити"}<input type="file" accept="image/jpeg,image/png,image/webp" style={{ display: "none" }} onChange={(e) => { const file = e.target.files?.[0]; if (file) { setImage(file); setImagePreview(URL.createObjectURL(file)); } }} /></label>
            {imagePreview && <button type="button" onClick={() => { setImage(null); setImagePreview(null); }} style={{ background: "none", border: 0, color: "#EF4444", cursor: "pointer", fontSize: 12 }}>Видалити</button>}
          </div><p style={hintStyle}>JPG, PNG або WEBP, до 5 МБ.</p></div>
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}><div><label style={labelStyle}>Колір фону</label><input type="color" style={{ ...inputStyle, height: 38, padding: 4, cursor: "pointer" }} value={backgroundColor} onChange={(e) => setBackgroundColor(e.target.value)} /></div><div><label style={labelStyle}>Акцентний колір</label><input type="color" style={{ ...inputStyle, height: 38, padding: 4, cursor: "pointer" }} value={accentColor} onChange={(e) => setAccentColor(e.target.value)} /></div></div>
        </section>

        <section style={sectionStyle}>
          <h3 style={sectionTitleStyle}>Магазини й аудиторія</h3>
          <div><label style={labelStyle}>Магазини, де діє акція</label>{activeLocations.length === 0 ? <p style={hintStyle}>Немає активних магазинів.</p> : <LocationsMultiSelectDropdown locations={activeLocations} selectedIds={locationIds} onToggle={toggleLocation} summaryLabel={`Вибрано магазинів: ${locationIds.length}`} placeholderLabel="Оберіть магазини" doneLabel="Готово" />}</div>
          <div><label style={labelStyle}>Аудиторія</label><select style={inputStyle} value={audienceType} onChange={(e) => setAudienceType(e.target.value as PromotionAudienceType)}><option value="all">Усі покупці</option><option value="loyalty_members">Учасники програми лояльності</option><option value="loyalty_tiers">Конкретні рівні лояльності</option></select></div>
          {audienceType === "loyalty_tiers" && <div><label style={labelStyle}>Рівні лояльності</label><div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>{tiers.map((tier) => <label key={tier.id} style={{ padding: "7px 9px", border: "1px solid #293241", borderRadius: 8, color: "#D1D5DB", fontSize: 12, cursor: "pointer" }}><input type="checkbox" checked={audienceTierIds.includes(tier.id)} onChange={() => toggleTier(tier.id)} /> {tier.name}</label>)}</div></div>}
        </section>

        <section style={sectionStyle}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: 12 }}><div><h3 style={sectionTitleStyle}>Товари акції</h3><p style={{ ...hintStyle, marginBottom: 0 }}>{products.length ? `Вибрано товарів: ${products.length}` : "Для кожного товару задається власна знижка."}</p></div><Btn type="button" size="sm" variant="ghost" onClick={() => setPickerOpen((value) => !value)}>{pickerOpen ? "Згорнути" : products.length ? "Змінити" : "Додати товари"}</Btn></div>
          {pickerOpen && <div><input autoFocus style={{ ...inputStyle, marginBottom: 8 }} placeholder="Пошук товару" value={search} onChange={(e) => setSearch(e.target.value)} /><div style={{ maxHeight: 190, overflowY: "auto", border: "1px solid #293241", background: "#0D1117", borderRadius: 8, padding: 8 }}>{catalogLoading ? <p style={hintStyle}>Завантаження товарів…</p> : availableProducts.length === 0 ? <p style={hintStyle}>Товарів не знайдено.</p> : availableProducts.map((product) => <button type="button" key={product.id} onClick={() => setProducts((value) => [...value, { productId: product.id, name: product.name, discountPercent: "10" }])} style={{ display: "block", width: "100%", textAlign: "left", padding: 8, border: 0, borderBottom: "1px solid #202938", background: "transparent", color: "#D1D5DB", cursor: "pointer" }}>{product.name}</button>)}</div></div>}
          <div style={{ display: "flex", flexDirection: "column", gap: 7 }}>{products.map((product) => <div key={product.productId} style={{ display: "grid", gridTemplateColumns: "minmax(0, 1fr) 110px auto", gap: 8, alignItems: "center", padding: 8, background: "#0D1117", border: "1px solid #202938", borderRadius: 8 }}><span style={{ color: "#D1D5DB", fontSize: 13, overflow: "hidden", textOverflow: "ellipsis" }}>{product.name}</span><div style={{ position: "relative" }}><input aria-label={`Знижка для ${product.name}`} type="number" min={0.01} max={100} step={0.01} style={{ ...inputStyle, paddingRight: 25 }} value={product.discountPercent} onChange={(e) => setProducts((value) => value.map((x) => x.productId === product.productId ? { ...x, discountPercent: e.target.value } : x))} /><span style={{ position: "absolute", right: 9, top: 9, color: "#6B7280", fontSize: 12 }}>%</span></div><button type="button" aria-label={`Видалити ${product.name}`} onClick={() => setProducts((value) => value.filter((x) => x.productId !== product.productId))} style={{ border: 0, background: "transparent", color: "#F87171", cursor: "pointer" }}>✕</button></div>)}</div>
        </section>

        <section style={sectionStyle}>
          <h3 style={sectionTitleStyle}>Публікація</h3>
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}><div><label style={labelStyle}>Дата і час запуску</label><input type="datetime-local" style={inputStyle} value={startsAt} onChange={(e) => setStartsAt(e.target.value)} /></div><div><label style={labelStyle}>Дата і час завершення</label><input type="datetime-local" style={inputStyle} value={endsAt} onChange={(e) => setEndsAt(e.target.value)} /></div></div>
          {!editing && <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: 12 }}><div><div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500 }}>Опублікувати одразу</div><p style={{ ...hintStyle, marginBottom: 0 }}>Якщо вимкнено, акція збережеться як чернетка.</p></div><Switch checked={publish} onCheckedChange={setPublish} /></div>}
        </section>

        {formError && <p style={{ color: "#F87171", fontSize: 12, gridColumn: "1 / -1", margin: 0 }}>{formError}</p>}
        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, paddingTop: 12, borderTop: "1px solid #1F2937", gridColumn: "1 / -1" }}><Btn type="submit" disabled={isPending}>{isPending ? "Збереження…" : editing ? "Зберегти зміни" : "Створити акцію"}</Btn><Btn type="button" variant="ghost" onClick={() => router.push("/consumer-app/promotions")}>Скасувати</Btn></div>
      </form>}
  </div>;
}
