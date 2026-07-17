// Review-count pluralization used to live here as a hand-rolled Ukrainian-only
// helper (`reviewWord`). It now lives in messages/{uk,en}.json as the ICU
// `Dashboard.marketplace.reviewCount` plural message (one/few/many/other),
// resolved via `useTranslations("Dashboard.marketplace")` + `t("reviewCount",
// { count })` at each call site (i18n rollout Block 6, TASK-384).
