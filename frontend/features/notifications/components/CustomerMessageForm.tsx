"use client";

import { useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { Image, MessageCircle, Send, Smartphone } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { useCreateCustomerMessage } from "../hooks/useNotifications";
import type { CustomerMessageAudience, CustomerMessageChannel, CustomerMessageContentType, CustomerMessageDeliveryMode, MessengerProvider } from "../types";
import { useMarketingAnalyticsOverview } from "@/features/marketing-analytics/hooks/useMarketingAnalytics";
import type { MarketingAnalyticsPeriodPreset, RfmSegmentKey } from "@/features/marketing-analytics/types";
import { useStoreContext } from "@/lib/useStoreContext";
import { usePromotionCampaigns } from "@/features/consumer-app/hooks/usePromotionCampaigns";
import { useBanners } from "@/features/consumer-app/hooks/useBanners";
import { useMobileCatalogSettings } from "@/features/consumer-app/hooks/useMobileCatalogSettings";
import { useAudienceOverview } from "@/features/marketing-analytics/audience-builder/hooks/useAudienceBuilder";
import type { AudienceBuildRequest, AudienceCombineMode } from "@/features/marketing-analytics/audience-builder/types";

const inputStyle = {
  width: "100%",
  boxSizing: "border-box" as const,
  border: "1px solid #263244",
  background: "#0D1117",
  color: "#E8EDF5",
  borderRadius: 8,
  padding: "10px 12px",
  fontSize: 13,
};

const sectionStyle = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 12,
  padding: 24,
};

export function CustomerMessageForm() {
  const router = useRouter();
  const create = useCreateCustomerMessage();
  const [title, setTitle] = useState("");
  const [message, setMessage] = useState("");
  const [audience, setAudience] = useState<CustomerMessageAudience>("loyalty_members");
  const [channels, setChannels] = useState<CustomerMessageChannel[]>(["push"]);
  const [messengerProvider, setMessengerProvider] = useState<MessengerProvider>("telegram");
  const [rfmPeriod, setRfmPeriod] = useState<MarketingAnalyticsPeriodPreset>("6m");
  const [rfmSegment, setRfmSegment] = useState<RfmSegmentKey>("AtRisk");
  const [contentType, setContentType] = useState<CustomerMessageContentType | "none">("none");
  const [contentId, setContentId] = useState("");
  const today = new Date().toISOString().slice(0, 10);
  const sixMonthsAgo = new Date(new Date().setMonth(new Date().getMonth() - 6)).toISOString().slice(0, 10);
  const [purchaseFrom, setPurchaseFrom] = useState(sixMonthsAgo);
  const [purchaseTo, setPurchaseTo] = useState(today);
  const [purchaseTerms, setPurchaseTerms] = useState("");
  const [purchaseMode, setPurchaseMode] = useState<AudienceCombineMode>("Any");
  const [minQuantity, setMinQuantity] = useState("");
  const [minAmount, setMinAmount] = useState("");
  const [deliveryMode, setDeliveryMode] = useState<CustomerMessageDeliveryMode>("draft");
  const [scheduledAt, setScheduledAt] = useState("");
  const promotions = usePromotionCampaigns();
  const banners = useBanners();
  const catalogs = useMobileCatalogSettings();
  const storeIds = useStoreContext((state) => state.selectedStoreIds);
  const rfmFilters = useMemo(() => ({ period: rfmPeriod, storeIds: [...storeIds].sort() }), [rfmPeriod, storeIds]);
  const rfmOverview = useMarketingAnalyticsOverview(rfmFilters, audience === "rfm_segment");
  const selectedRfm = rfmOverview.data?.segments.find((segment) => segment.key === rfmSegment);
  const contentItems = contentType === "promotion"
    ? (promotions.data ?? []).map((item) => ({ id: item.id, title: item.title, imageUrl: item.imageUrl }))
    : contentType === "banner"
      ? (banners.data ?? []).map((item) => ({ id: item.id, title: item.title, imageUrl: item.imageUrl }))
      : contentType === "catalog"
        ? (catalogs.data ?? []).map((item) => ({ id: item.id, title: item.title, imageUrl: item.bannerUrl }))
        : [];
  const selectedContent = contentItems.find((item) => item.id === contentId);
  const contentLoading = contentType === "promotion" ? promotions.isLoading : contentType === "banner" ? banners.isLoading : contentType === "catalog" ? catalogs.isLoading : false;
  const parsedPurchaseTerms = useMemo(() => purchaseTerms.split(",").map((term) => term.trim()).filter(Boolean), [purchaseTerms]);
  const purchaseRequest = useMemo<AudienceBuildRequest>(() => ({
    from: purchaseFrom, to: purchaseTo, storeIds: storeIds.length ? storeIds : null,
    terms: parsedPurchaseTerms.map((text) => ({ kind: "Text", text, categoryId: null })),
    mode: purchaseMode, minQuantity: minQuantity ? Number(minQuantity) : null,
    minAmount: minAmount ? Number(minAmount) : null, excludedItemIds: null,
    page: 1, pageSize: 20, sortBy: null, sortDescending: true, canViewUnmaskedPii: false,
  }), [purchaseFrom, purchaseTo, storeIds, parsedPurchaseTerms, purchaseMode, minQuantity, minAmount]);
  const purchaseOverview = useAudienceOverview(purchaseRequest, audience === "purchase_history" && parsedPurchaseTerms.length > 0 && purchaseFrom <= purchaseTo);

  const toggle = (channel: CustomerMessageChannel) => setChannels((current) =>
    current.includes(channel) ? current.filter((item) => item !== channel) : [...current, channel]);

  const submit = () => create.mutate(
    {
      title,
      message,
      audience,
      channels,
      ...(channels.includes("messenger") ? { messengerProvider } : {}),
      ...(audience === "rfm_segment" ? { rfmAudience: { segment: rfmSegment, period: rfmPeriod, storeIds, estimatedRecipients: selectedRfm?.customerCount ?? 0 } } : {}),
      ...(audience === "purchase_history" ? { purchaseAudience: {
        from: purchaseFrom, to: purchaseTo, storeIds, terms: purchaseRequest.terms,
        mode: purchaseMode, minQuantity: purchaseRequest.minQuantity, minAmount: purchaseRequest.minAmount,
        estimatedRecipients: purchaseOverview.data?.participantsCount ?? 0,
      } } : {}),
      ...(contentType !== "none" && contentId ? { content: { type: contentType, id: contentId } } : {}),
      deliveryMode,
      ...(deliveryMode === "scheduled" && scheduledAt ? { scheduledAt: new Date(scheduledAt).toISOString() } : {}),
    },
    { onSuccess: () => router.push("/consumer-app/messages") },
  );

  const invalid = !title.trim() || !message.trim() || channels.length === 0 ||
    (audience === "rfm_segment" && (!rfmOverview.data || rfmOverview.isError)) ||
    (audience === "purchase_history" && (parsedPurchaseTerms.length === 0 || purchaseFrom > purchaseTo || !purchaseOverview.data || purchaseOverview.isError));
  const formInvalid = invalid || (contentType !== "none" && !contentId) ||
    (deliveryMode === "scheduled" && (!scheduledAt || new Date(scheduledAt) <= new Date()));

  return (
    <div style={{ display: "grid", gridTemplateColumns: "minmax(0, 2fr) minmax(280px, 1fr)", gap: 20, alignItems: "start" }}>
      <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>
        <section style={sectionStyle}>
          <h2 style={{ color: "#E8EDF5", fontSize: 15, margin: "0 0 4px" }}>Вміст повідомлення</h2>
          <p style={{ color: "#6B7280", fontSize: 12, margin: "0 0 18px" }}>Заголовок і текст, які побачить користувач застосунку.</p>
          <label style={{ display: "block", color: "#CBD5E1", fontSize: 13 }}>Заголовок
            <input value={title} maxLength={120} onChange={(e) => setTitle(e.target.value)} placeholder="Наприклад: Знижки вихідного дня" style={{ ...inputStyle, marginTop: 7 }}/>
          </label>
          <label style={{ display: "block", color: "#CBD5E1", fontSize: 13, marginTop: 16 }}>Повідомлення
            <textarea value={message} maxLength={2000} rows={7} onChange={(e) => setMessage(e.target.value)} placeholder="Текст повідомлення для покупців" style={{ ...inputStyle, marginTop: 7, resize: "vertical" }}/>
            <span style={{ display: "block", textAlign: "right", color: "#64748B", fontSize: 11, marginTop: 4 }}>{message.length}/2000</span>
          </label>
        </section>

        <section style={sectionStyle}>
          <h2 style={{ color: "#E8EDF5", fontSize: 15, margin: "0 0 4px" }}>Пов’язаний контент</h2>
          <p style={{ color: "#6B7280", fontSize: 12, margin: "0 0 18px" }}>Після відкриття повідомлення користувач зможе перейти до вибраної акції, банера або каталогу.</p>
          <div style={{ display: "grid", gridTemplateColumns: "minmax(180px, 1fr) minmax(260px, 2fr)", gap: 12 }}>
            <label style={{ color: "#CBD5E1", fontSize: 13 }}>Тип контенту
              <select value={contentType} onChange={(e) => { setContentType(e.target.value as CustomerMessageContentType | "none"); setContentId(""); }} style={{ ...inputStyle, marginTop: 7 }}>
                <option value="none">Без прив’язки</option><option value="promotion">Акція</option><option value="banner">Банер</option><option value="catalog">Каталог</option>
              </select>
            </label>
            <label style={{ color: "#CBD5E1", fontSize: 13 }}>Матеріал
              <select value={contentId} onChange={(e) => setContentId(e.target.value)} disabled={contentType === "none" || contentLoading} style={{ ...inputStyle, marginTop: 7 }}>
                <option value="">{contentLoading ? "Завантаження…" : contentType === "none" ? "Спочатку виберіть тип" : "Виберіть матеріал"}</option>
                {contentItems.map((item) => <option key={item.id} value={item.id}>{item.title}</option>)}
              </select>
            </label>
          </div>
        </section>

        <section style={sectionStyle}>
          <h2 style={{ color: "#E8EDF5", fontSize: 15, margin: "0 0 4px" }}>Аудиторія</h2>
          <p style={{ color: "#6B7280", fontSize: 12, margin: "0 0 18px" }}>Наразі доступні базові аудиторії. Сегменти Marketing Analytics підключатимуться як джерела, без копіювання клієнтів.</p>
          <label style={{ display: "block", color: "#CBD5E1", fontSize: 13 }}>Кому надіслати
            <select value={audience} onChange={(e) => setAudience(e.target.value as CustomerMessageAudience)} style={{ ...inputStyle, marginTop: 7 }}>
              <option value="loyalty_members">Учасники програми лояльності</option>
              <option value="all_customers">Усі покупці</option>
              <option value="rfm_segment">RFM-сегмент із Marketing Analytics</option>
              <option value="purchase_history">За минулими покупками</option>
            </select>
          </label>
          {audience === "rfm_segment" && <div style={{ display: "grid", gridTemplateColumns: "minmax(180px, 1fr) minmax(240px, 2fr)", gap: 12, marginTop: 16 }}>
            <label style={{ color: "#CBD5E1", fontSize: 13 }}>Період аналізу
              <select value={rfmPeriod} onChange={(e) => setRfmPeriod(e.target.value as MarketingAnalyticsPeriodPreset)} style={{ ...inputStyle, marginTop: 7 }}><option value="3m">Останні 3 місяці</option><option value="6m">Останні 6 місяців</option><option value="12m">Останні 12 місяців</option><option value="all">За весь час</option></select>
            </label>
            <label style={{ color: "#CBD5E1", fontSize: 13 }}>RFM-сегмент
              <select value={rfmSegment} onChange={(e) => setRfmSegment(e.target.value as RfmSegmentKey)} disabled={rfmOverview.isLoading || rfmOverview.isError} style={{ ...inputStyle, marginTop: 7 }}>
                {(rfmOverview.data?.segments ?? []).map((segment) => <option key={segment.key} value={segment.key}>{segment.labelUa} — {segment.customerCount} клієнтів</option>)}
              </select>
            </label>
            <div style={{ gridColumn: "1 / -1", border: "1px solid #1E3A5F", background: "#0F172A", borderRadius: 8, padding: "11px 12px", color: "#93C5FD", fontSize: 12 }}>
              {rfmOverview.isLoading ? "Розраховуємо аудиторію…" : rfmOverview.isError ? "Не вдалося отримати RFM-сегменти. Перевірте доступ до модуля Marketing Analytics." : <>Орієнтовно отримувачів: <strong>{selectedRfm?.customerCount ?? 0}</strong> · {storeIds.length === 0 ? "усі магазини" : `обрано магазинів: ${storeIds.length}`}</>}
            </div>
          </div>}
          {audience === "purchase_history" && <div style={{ display: "grid", gridTemplateColumns: "repeat(2, minmax(0, 1fr))", gap: 12, marginTop: 16 }}>
            <label style={{ color: "#CBD5E1", fontSize: 13 }}>Період від<input type="date" value={purchaseFrom} max={purchaseTo} onChange={(e) => setPurchaseFrom(e.target.value)} style={{ ...inputStyle, marginTop: 7 }}/></label>
            <label style={{ color: "#CBD5E1", fontSize: 13 }}>Період до<input type="date" value={purchaseTo} min={purchaseFrom} max={today} onChange={(e) => setPurchaseTo(e.target.value)} style={{ ...inputStyle, marginTop: 7 }}/></label>
            <label style={{ gridColumn: "1 / -1", color: "#CBD5E1", fontSize: 13 }}>Товари або категорії
              <input value={purchaseTerms} onChange={(e) => setPurchaseTerms(e.target.value)} placeholder="Наприклад: кава, молоко, випічка" style={{ ...inputStyle, marginTop: 7 }}/>
              <span style={{ display: "block", color: "#64748B", fontSize: 11, marginTop: 4 }}>Введіть назви, штрихкоди або ID через кому. Використовується Audience Builder.</span>
            </label>
            <label style={{ color: "#CBD5E1", fontSize: 13 }}>Умова збігу<select value={purchaseMode} onChange={(e) => setPurchaseMode(e.target.value as AudienceCombineMode)} style={{ ...inputStyle, marginTop: 7 }}><option value="Any">Купував будь-що зі списку</option><option value="All">Купував усе зі списку</option></select></label>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
              <label style={{ color: "#CBD5E1", fontSize: 13 }}>Мін. кількість<input type="number" min="0" value={minQuantity} onChange={(e) => setMinQuantity(e.target.value)} style={{ ...inputStyle, marginTop: 7 }}/></label>
              <label style={{ color: "#CBD5E1", fontSize: 13 }}>Мін. сума, ₴<input type="number" min="0" value={minAmount} onChange={(e) => setMinAmount(e.target.value)} style={{ ...inputStyle, marginTop: 7 }}/></label>
            </div>
            <div style={{ gridColumn: "1 / -1", border: "1px solid #1E3A5F", background: "#0F172A", borderRadius: 8, padding: "11px 12px", color: "#93C5FD", fontSize: 12 }}>
              {parsedPurchaseTerms.length === 0 ? "Додайте хоча б один товар або категорію." : purchaseOverview.isLoading ? "Розраховуємо аудиторію за покупками…" : purchaseOverview.isError ? "Не вдалося розрахувати аудиторію." : <>Знайдено покупців: <strong>{purchaseOverview.data?.participantsCount ?? 0}</strong> · товарів у вибірці: {purchaseOverview.data?.itemsInSelectionCount ?? 0}</>}
            </div>
          </div>}
        </section>

        <section style={sectionStyle}>
          <h2 style={{ color: "#E8EDF5", fontSize: 15, margin: "0 0 4px" }}>Канали відправлення</h2>
          <p style={{ color: "#6B7280", fontSize: 12, margin: "0 0 18px" }}>Можна вибрати один або декілька каналів.</p>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(3, minmax(0, 1fr))", gap: 10 }}>
            {([{ id: "push", label: "Push", icon: <Smartphone size={18}/> }, { id: "messenger", label: "Месенджер", icon: <MessageCircle size={18}/> }, { id: "sms", label: "SMS", icon: <Send size={18}/> }] as const).map((item) => (
              <button type="button" key={item.id} onClick={() => toggle(item.id)} style={{ display: "flex", alignItems: "center", justifyContent: "center", gap: 7, padding: 12, borderRadius: 8, cursor: "pointer", fontWeight: 600, border: `1px solid ${channels.includes(item.id) ? "#3B82F6" : "#374151"}`, background: channels.includes(item.id) ? "#1D3461" : "transparent", color: channels.includes(item.id) ? "#93C5FD" : "#9CA3AF" }}>{item.icon}{item.label}</button>
            ))}
          </div>
          {channels.includes("messenger") && <label style={{ display: "block", color: "#CBD5E1", fontSize: 13, marginTop: 16 }}>Месенджер
            <select value={messengerProvider} onChange={(e) => setMessengerProvider(e.target.value as MessengerProvider)} style={{ ...inputStyle, marginTop: 7 }}><option value="telegram">Telegram</option><option value="viber">Viber</option><option value="whatsapp">WhatsApp</option></select>
          </label>}
        </section>

        <section style={sectionStyle}>
          <h2 style={{ color: "#E8EDF5", fontSize: 15, margin: "0 0 4px" }}>Час відправлення</h2>
          <p style={{ color: "#6B7280", fontSize: 12, margin: "0 0 18px" }}>Провайдери ще не підключені: кампанія збереже готовий стан і не буде фактично відправлена до підключення інтеграції.</p>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(3, minmax(0, 1fr))", gap: 10 }}>
            {([{ id: "draft", label: "Зберегти чернетку" }, { id: "send_now", label: "Відправити зараз" }, { id: "scheduled", label: "Запланувати" }] as const).map((item) => <button type="button" key={item.id} onClick={() => setDeliveryMode(item.id)} style={{ padding: 12, borderRadius: 8, cursor: "pointer", fontWeight: 600, border: `1px solid ${deliveryMode === item.id ? "#3B82F6" : "#374151"}`, background: deliveryMode === item.id ? "#1D3461" : "transparent", color: deliveryMode === item.id ? "#93C5FD" : "#9CA3AF" }}>{item.label}</button>)}
          </div>
          {deliveryMode === "scheduled" && <label style={{ display: "block", color: "#CBD5E1", fontSize: 13, marginTop: 16 }}>Дата і час<input type="datetime-local" value={scheduledAt} min={new Date(Date.now() + 60_000).toISOString().slice(0, 16)} onChange={(e) => setScheduledAt(e.target.value)} style={{ ...inputStyle, marginTop: 7 }}/></label>}
        </section>
      </div>

      <aside style={{ ...sectionStyle, position: "sticky", top: 20 }}>
        <h2 style={{ color: "#E8EDF5", fontSize: 15, margin: "0 0 14px" }}>Попередній перегляд</h2>
        <div style={{ background: "#111827", border: "1px solid #263244", borderRadius: 12, padding: 16 }}>
          {selectedContent?.imageUrl && <img src={selectedContent.imageUrl} alt="" style={{ width: "100%", maxHeight: 150, objectFit: "cover", borderRadius: 8, marginBottom: 12 }}/>} 
          <strong style={{ display: "block", color: "#F3F4F6", fontSize: 14 }}>{title.trim() || "Заголовок повідомлення"}</strong>
          <p style={{ color: "#9CA3AF", fontSize: 13, lineHeight: 1.5, whiteSpace: "pre-wrap", overflowWrap: "anywhere", margin: "8px 0 0" }}>{message.trim() || "Тут буде показано текст повідомлення для користувача."}</p>
        </div>
        <div style={{ color: "#6B7280", fontSize: 12, lineHeight: 1.6, marginTop: 14 }}><div>Аудиторія: {audience === "all_customers" ? "усі покупці" : audience === "loyalty_members" ? "учасники лояльності" : audience === "purchase_history" ? `за покупками (${purchaseOverview.data?.participantsCount ?? 0})` : `${selectedRfm?.labelUa ?? rfmSegment} (${selectedRfm?.customerCount ?? 0})`}</div><div>Каналів: {channels.length}</div>{selectedContent && <div style={{ display: "flex", gap: 5, alignItems: "center" }}><Image size={12}/> {selectedContent.title}</div>}</div>
        {create.isError && <p style={{ color: "#FCA5A5", fontSize: 13 }}>Не вдалося зберегти повідомлення. Перевірте поля.</p>}
        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 20 }}><Btn variant="ghost" onClick={() => router.push("/consumer-app/messages")}>Скасувати</Btn><Btn disabled={formInvalid || create.isPending} onClick={submit}>{create.isPending ? "Збереження…" : deliveryMode === "draft" ? "Створити чернетку" : deliveryMode === "scheduled" ? "Запланувати" : "Підготувати відправлення"}</Btn></div>
      </aside>
    </div>
  );
}
