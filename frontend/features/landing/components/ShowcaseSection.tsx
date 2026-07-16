import Image from "next/image";
import { getTranslations } from "next-intl/server";
import { Reveal } from "./Reveal";
import { BrowserFrame } from "./BrowserFrame";

interface ShowcaseImage {
  src: string;
  alt: string;
  width: number;
  height: number;
}

interface ShowcaseBlockData {
  eyebrow: string;
  title: string;
  text: string;
  points: string[];
  image1Alt: string;
  image2Alt: string;
}

const EYEBROW_COLORS = ["text-[#5EA3E8]", "text-[#ef4444]", "text-[#22c55e]"];

const IMAGE_SETS: [ShowcaseImage, ShowcaseImage][] = [
  [
    { src: "/landing/dashboard-1.jpg", alt: "", width: 1280, height: 574 },
    { src: "/landing/dashboard-2.jpg", alt: "", width: 1280, height: 181 },
  ],
  [
    { src: "/landing/inventory-1.jpg", alt: "", width: 1280, height: 519 },
    { src: "/landing/inventory-2.jpg", alt: "", width: 1280, height: 266 },
  ],
  [
    { src: "/landing/analytics-1.jpg", alt: "", width: 1280, height: 596 },
    { src: "/landing/analytics-2.jpg", alt: "", width: 1280, height: 645 },
  ],
];

export async function ShowcaseSection() {
  const t = await getTranslations("Landing.showcase");
  const blocks = t.raw("blocks") as ShowcaseBlockData[];

  return (
    <section id="screenshots" className="scroll-mt-20 py-16 sm:py-24">
      <div className="mx-auto max-w-6xl px-4 sm:px-6 lg:px-8">
        <Reveal className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            {t("heading")}
          </h2>
          <p className="mt-4 text-lg text-slate-400">{t("subheading")}</p>
        </Reveal>

        <div className="mt-16 space-y-20 sm:space-y-28">
          {blocks.map((block, i) => {
            const images: ShowcaseImage[] = [
              { ...IMAGE_SETS[i][0], alt: block.image1Alt },
              { ...IMAGE_SETS[i][1], alt: block.image2Alt },
            ];
            return (
              <div
                key={block.eyebrow}
                className="grid items-center gap-10 lg:grid-cols-12 lg:gap-12"
              >
                <Reveal className={`lg:col-span-4 ${i % 2 === 1 ? "lg:order-2" : ""}`}>
                  <p
                    className={`text-sm font-semibold uppercase tracking-wider ${EYEBROW_COLORS[i]}`}
                  >
                    {block.eyebrow}
                  </p>
                  <h3 className="mt-3 text-2xl font-bold tracking-tight text-white sm:text-[28px] sm:leading-9">
                    {block.title}
                  </h3>
                  <p className="mt-4 leading-relaxed text-slate-400">{block.text}</p>
                  <ul className="mt-6 space-y-3">
                    {block.points.map((point) => (
                      <li key={point} className="flex gap-3 text-[15px] text-slate-300">
                        <span
                          aria-hidden="true"
                          className="mt-[7px] h-1.5 w-1.5 shrink-0 rounded-full bg-[#2D7DD2]"
                        />
                        {point}
                      </li>
                    ))}
                  </ul>
                </Reveal>

                <Reveal delay={100} className={`lg:col-span-8 ${i % 2 === 1 ? "lg:order-1" : ""}`}>
                  <div className="space-y-4">
                    {images.map((img) => (
                      <BrowserFrame key={img.src}>
                        <Image
                          src={img.src}
                          alt={img.alt}
                          width={img.width}
                          height={img.height}
                          sizes="(max-width: 640px) 100vw, (max-width: 1024px) 92vw, 690px"
                          className="h-auto w-full"
                        />
                      </BrowserFrame>
                    ))}
                  </div>
                </Reveal>
              </div>
            );
          })}
        </div>
      </div>
    </section>
  );
}
