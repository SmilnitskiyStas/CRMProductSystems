import { describe, expect, it } from "vitest";
import { isBeautyLead, isVerifiedBeauty, leadRequestType } from "./leadRequest";

const base = { source: "website", beautyTenantSlug: null, beautyTenantName: null, requestType: null } as const;

describe("leadRequest helpers", () => {
  it("accepts only known request types", () => {
    expect(leadRequestType({ requestType: "bug" })).toBe("bug");
    expect(leadRequestType({ requestType: "cooperation" })).toBe("cooperation");
    expect(leadRequestType({ requestType: "other" as never })).toBeNull();
    expect(leadRequestType({})).toBeNull();
  });

  it("detects Beauty leads by source, tenant data or request type", () => {
    expect(isBeautyLead(base)).toBe(false);
    expect(isBeautyLead({ ...base, source: "beauty" })).toBe(true);
    expect(isBeautyLead({ ...base, beautyTenantSlug: "salon" })).toBe(true);
    expect(isBeautyLead({ ...base, requestType: "improvement" })).toBe(true);
  });
});

describe("isVerifiedBeauty", () => {
  it("is true only for the backend verifiedBeauty flag", () => {
    expect(isVerifiedBeauty({ verifiedBeauty: true })).toBe(true);
    expect(isVerifiedBeauty({ verifiedBeauty: false })).toBe(false);
    expect(isVerifiedBeauty({})).toBe(false);
  });
});
