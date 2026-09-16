import { getTranslations } from "next-intl/server";
import { Link } from "@/i18n/navigation";
import { Reveal } from "./Reveal";

// Short cross-link back to the full-feature homepage, placed between the
// vertical-specific content and the final lead CTA (TASK-713). Only links
// to "/" — auto-service/production/warehouses vertical pages don't exist
// yet, so this intentionally does not link out to sibling verticals.
export async function RetailBackHomeSection() {
  const t = await getTranslations("Landing.verticals.retail.backHome");

  return (
    <section className="py-6">
      <div className="mx-auto max-w-6xl px-4 text-center sm:px-6 lg:px-8">
        <Reveal>
          <p className="text-slate-400">
            {t("text")}{" "}
            <Link href="/" className="font-medium text-[#5EA3E8] transition-colors hover:text-white">
              {t("linkLabel")}
            </Link>
          </p>
        </Reveal>
      </div>
    </section>
  );
}
