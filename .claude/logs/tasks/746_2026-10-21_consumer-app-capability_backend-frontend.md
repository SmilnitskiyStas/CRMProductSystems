# TASK-746 — capability `consumer_app.manage` для розділу «Застосунок»

Що: enterprise_admin може через TenantRole-шаблон дати нижчій ролі (store_manager тощо) доступ до всього розділу «Застосунок».
- Backend: `TenantRoleCapabilities.ConsumerAppManage` (+група «Застосунок», у `All`); політика `AppPolicies.ConsumerAppManageOrCapability` (AtLeastEnterpriseAdminRoles OR capability); `TenantRoleTabs`: група `consumer_app` + 10 item-ключів `/consumer-app/*` (без `/consumer-app/analytics` — лишається під analytics permission).
- Контролери переключено з AtLeastEnterpriseAdmin: Banners, LoyaltySettings, LoyaltyTierSettings, MobileBlocks, MobileCatalogSettings, MobileConfigDraft/Preview/Publish/Versions, MobileTheme, PromotionCampaigns, NotificationsController (4 customer-messages). `[RequireModule("mobile_app")]` без змін. PriceSegmentSettings не чіпали (маркетингова аналітика, не mobile_app).
- Frontend: `Sidebar.tsx` — `NavItem.capability`, role OR capability для пунктів consumer_app; редактор ролей бере каталог capabilities/tabs з бекенда, правок i18n не треба.
- Тести: AppPoliciesTests (+4 кейси), TenantRoleServiceTests (+1). 168 passed (фільтр AppPolicies|TenantRole|AuthService*).
- Build: dotnet build OK, tsc/eslint чисті.
- Безпека: capability не додано до жодної supplier/provider-політики; supplier_admin не в базових ролях.
