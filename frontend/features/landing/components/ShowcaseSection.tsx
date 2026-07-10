import Image from "next/image";
import { Reveal } from "./Reveal";
import { BrowserFrame } from "./BrowserFrame";

interface ShowcaseImage {
  src: string;
  alt: string;
  width: number;
  height: number;
}

interface ShowcaseBlock {
  eyebrow: string;
  eyebrowColor: string;
  title: string;
  text: string;
  points: string[];
  images: ShowcaseImage[];
}

const BLOCKS: ShowcaseBlock[] = [
  {
    eyebrow: "Дашборд",
    eyebrowColor: "text-[#5EA3E8]",
    title: "Стан магазину — з першого погляду",
    text: "Чотири статуси партій — Safe, Warning, Critical, Expired — одразу показують, де проблема. Поруч — швидкі дії та карта магазину за зонами.",
    points: [
      "Товари, що потребують уваги, — окремим списком",
      "Швидкі дії: перевірити критичні, списати, замовити",
      "Карта зон: стелажі й холодильники зі статусами",
    ],
    images: [
      {
        src: "/landing/dashboard-1.jpg",
        alt: "Дашборд ShelfGuard зі статус-картками та списком товарів, що потребують уваги",
        width: 1280,
        height: 574,
      },
      {
        src: "/landing/dashboard-2.jpg",
        alt: "Карта магазину за зонами: стелажі та холодильники зі статусами Safe, Warning, Critical",
        width: 1280,
        height: 181,
      },
    ],
  },
  {
    eyebrow: "Залишки і терміни",
    eyebrowColor: "text-[#ef4444]",
    title: "Система показує проблему одразу",
    text: "Кожна партія — з датою придатності, кількістю днів до кінця терміну і статусом. Прострочене підсвічується червоним — його неможливо не помітити.",
    points: [
      "Червоне «Прострочено» — з точною кількістю днів",
      "Пошук за назвою чи штрихкодом, фільтр за статусом",
      "Здорові партії — зелена «Норма», без зайвого шуму",
    ],
    images: [
      {
        src: "/landing/inventory-1.jpg",
        alt: "Таблиця партій із простроченими позиціями, підсвіченими червоним статусом «Прострочено»",
        width: 1280,
        height: 519,
      },
      {
        src: "/landing/inventory-2.jpg",
        alt: "Партії зі статусом «Норма» — зелені позначки та запас днів до кінця терміну",
        width: 1280,
        height: 266,
      },
    ],
  },
  {
    eyebrow: "Аналітика",
    eyebrowColor: "text-[#22c55e]",
    title: "Цифри, за якими видно гроші",
    text: "Розподіл партій за статусами, стан за зонами й категоріями, збитки від списань у гривнях. По одному магазину — і по всій мережі.",
    points: [
      "Розподіл за статусами: скільки в нормі, скільки прострочено",
      "Розрізи за зонами і категоріями товарів",
      "Збитки від списань — у гривнях, а не «на відчуттях»",
    ],
    images: [
      {
        src: "/landing/analytics-1.jpg",
        alt: "Аналітика ShelfGuard: діаграма розподілу партій за статусами та зведення по магазину",
        width: 1280,
        height: 596,
      },
      {
        src: "/landing/analytics-2.jpg",
        alt: "Аналітика за категоріями: стовпчикова діаграма стану залишків по категоріях товарів",
        width: 1280,
        height: 645,
      },
    ],
  },
];

export function ShowcaseSection() {
  return (
    <section id="screenshots" className="scroll-mt-20 py-16 sm:py-24">
      <div className="mx-auto max-w-6xl px-4 sm:px-6 lg:px-8">
        <Reveal className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            Подивіться, як це виглядає
          </h2>
          <p className="mt-4 text-lg text-slate-400">
            Реальний інтерфейс системи — темна тема, українська мова, нічого зайвого.
          </p>
        </Reveal>

        <div className="mt-16 space-y-20 sm:space-y-28">
          {BLOCKS.map((block, i) => (
            <div
              key={block.eyebrow}
              className="grid items-center gap-10 lg:grid-cols-12 lg:gap-12"
            >
              <Reveal
                className={`lg:col-span-4 ${i % 2 === 1 ? "lg:order-2" : ""}`}
              >
                <p className={`text-sm font-semibold uppercase tracking-wider ${block.eyebrowColor}`}>
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

              <Reveal
                delay={100}
                className={`lg:col-span-8 ${i % 2 === 1 ? "lg:order-1" : ""}`}
              >
                <div className="space-y-4">
                  {block.images.map((img) => (
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
          ))}
        </div>
      </div>
    </section>
  );
}
