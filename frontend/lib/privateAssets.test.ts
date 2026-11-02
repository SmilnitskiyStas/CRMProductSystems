import { describe, expect, it } from "vitest";
import { API_BASE } from "./api";
import { privateAssetPath } from "./privateAssets";

const tenant = "11111111-1111-1111-1111-111111111111";
describe("private asset routing", () => {
  it.each(["shelf-scans", "floor-plans", "contract-assets"])("uses authenticated reading for %s", (category) => {
    const path = `/uploads/${category}/${tenant}/synthetic.png`;
    expect(privateAssetPath(path)).toBe(`/api/private-files/${category}/${tenant}/synthetic.png`);
    expect(privateAssetPath(`${API_BASE}${path}`)).toBe(`/api/private-files/${category}/${tenant}/synthetic.png`);
  });
  it.each([
    `https://untrusted.example/uploads/shelf-scans/${tenant}/synthetic.png`,
    `//untrusted.example/uploads/shelf-scans/${tenant}/synthetic.png`,
    `/uploads/shelf-scans/${tenant}/synthetic.png?access_token=marker`,
    `/uploads/contracts/${tenant}/contract.pdf`,
    `/uploads/items/${tenant}/public.png`,
    `/uploads/shelf-scans/${tenant}/image.svg`,
  ])("does not forward credentials to an unsafe or unrelated URL: %s", (source) => {
    expect(privateAssetPath(source)).toBeNull();
  });
});
