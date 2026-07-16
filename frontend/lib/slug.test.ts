import { describe, expect, it } from "vitest";
import { slugify } from "./slug";

// Tenant/company slug generation with UA->LAT transliteration — feeds URLs and
// (indirectly) identifiers, so garbage-in must never produce garbage/unsafe
// slugs. Zero coverage before this (audit Block 13).

describe("slugify", () => {
  it("lowercases and hyphenates spaces", () => {
    expect(slugify("Fresh Market")).toBe("fresh-market");
  });

  it("transliterates Ukrainian Cyrillic to Latin", () => {
    expect(slugify("Свіжий Хліб")).toBe("svizhyi-khlib");
  });

  it("transliterates Russian-only extra letters (ы, э, ё, ъ)", () => {
    expect(slugify("Быстрый")).toBe("bystryi");
  });

  it("strips characters outside [a-z0-9-]", () => {
    expect(slugify("Café & Co!!!")).toBe("caf-co");
  });

  it("collapses multiple separators into a single hyphen", () => {
    expect(slugify("A   B___C")).toBe("a-b-c");
  });

  it("trims leading/trailing hyphens produced by punctuation", () => {
    expect(slugify("--Hello--")).toBe("hello");
  });

  it("truncates to 32 characters", () => {
    const long = "a".repeat(50);
    expect(slugify(long)).toHaveLength(32);
  });

  it("returns an empty string for input that is entirely stripped (never crashes)", () => {
    expect(slugify("!!!")).toBe("");
  });

  it("handles the apostrophe-like soft sign ь by dropping it, not keeping a stray hyphen", () => {
    expect(slugify("Пальто")).toBe("palto");
  });
});
