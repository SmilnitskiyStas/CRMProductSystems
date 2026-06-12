import { Resend } from "resend";

const RESEND_API_KEY = process.env.RESEND_API_KEY ?? "";
const FROM_EMAIL    = process.env.FROM_EMAIL ?? "noreply@shelfguard.app";

let resend: Resend | null = null;

function getClient(): Resend {
  if (!RESEND_API_KEY) throw new Error("RESEND_API_KEY not set");
  resend ??= new Resend(RESEND_API_KEY);
  return resend;
}

// Returns "skipped" when the channel is not configured; throws on delivery error.
export async function sendEmail(params: {
  to: string;
  subject: string;
  html: string;
}): Promise<"sent" | "skipped"> {
  if (!RESEND_API_KEY) {
    console.warn("[email] RESEND_API_KEY not set — skipping send");
    return "skipped";
  }

  const { error } = await getClient().emails.send({
    from: FROM_EMAIL,
    to:   params.to,
    subject: params.subject,
    html: params.html,
  });

  if (error) throw new Error(`Resend error: ${error.message}`);
  return "sent";
}
