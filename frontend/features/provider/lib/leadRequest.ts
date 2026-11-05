import type { LandingLeadDto, LeadRequestType } from "../types";
import { LEAD_REQUEST_TYPES } from "../types";

/** Unknown / missing values are treated as "no request type" (ordinary website lead). */
export function leadRequestType(l: Pick<LandingLeadDto, "requestType">): LeadRequestType | null {
  return LEAD_REQUEST_TYPES.includes(l.requestType as LeadRequestType) ? (l.requestType as LeadRequestType) : null;
}

/** A lead raised from inside Beauty CRM (source "beauty*" or carrying Beauty tenant data). */
export function isBeautyLead(l: Pick<LandingLeadDto, "source" | "beautyTenantSlug" | "beautyTenantName" | "requestType">): boolean {
  return (
    /^beauty/i.test(l.source ?? "") ||
    Boolean(l.beautyTenantSlug) ||
    Boolean(l.beautyTenantName) ||
    leadRequestType(l) !== null
  );
}

/** Verified only by the backend flag (signed HMAC channel); source/tenant fields alone prove nothing. */
export function isVerifiedBeauty(l: Pick<LandingLeadDto, "verifiedBeauty">): boolean {
  return l.verifiedBeauty === true;
}
