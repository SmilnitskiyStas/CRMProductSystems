// Prepares a user-picked floor-plan file for upload: PDF -> first page rendered to PNG
// (pdfjs-dist, lazily imported so it never loads on the server); images are passed through.

export const PLAN_ACCEPT = "application/pdf,image/png,image/jpeg,image/webp";
export const PLAN_MAX_BYTES = 8 * 1024 * 1024;
const PDF_TARGET_WIDTH = 2000;

export interface PreparedPlan {
  blob: Blob;
  fileName: string;
  naturalW: number;
  naturalH: number;
}

async function renderPdfFirstPage(file: File): Promise<PreparedPlan> {
  const pdfjs = await import("pdfjs-dist");
  // Worker is served statically from /public (copy of pdfjs-dist@4.10.38's build/pdf.worker.min.mjs) —
  // bundling the .mjs worker through Next's SWC minifier fails. Update the copy when bumping pdfjs-dist.
  pdfjs.GlobalWorkerOptions.workerSrc = "/pdfjs/pdf.worker.min.mjs";
  const doc = await pdfjs.getDocument({ data: await file.arrayBuffer() }).promise;
  try {
    const page = await doc.getPage(1);
    const base = page.getViewport({ scale: 1 });
    const scale = Math.min(4, PDF_TARGET_WIDTH / base.width);
    const viewport = page.getViewport({ scale });
    const canvas = document.createElement("canvas");
    canvas.width = Math.round(viewport.width);
    canvas.height = Math.round(viewport.height);
    const ctx = canvas.getContext("2d");
    if (!ctx) throw new Error("canvas unavailable");
    ctx.fillStyle = "#ffffff";
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    await page.render({ canvasContext: ctx, viewport }).promise;
    const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, "image/png"));
    if (!blob) throw new Error("render failed");
    return { blob, fileName: "plan.png", naturalW: canvas.width, naturalH: canvas.height };
  } finally {
    await doc.destroy();
  }
}

async function measureImage(file: File): Promise<PreparedPlan> {
  const bitmap = await createImageBitmap(file);
  const result = { blob: file, fileName: file.name, naturalW: bitmap.width, naturalH: bitmap.height };
  bitmap.close();
  return result;
}

export async function prepareFloorPlanFile(file: File): Promise<PreparedPlan> {
  const isPdf = file.type === "application/pdf" || /\.pdf$/i.test(file.name);
  return isPdf ? renderPdfFirstPage(file) : measureImage(file);
}
