import assert from "node:assert/strict";
import { test } from "node:test";
import { formatPasswordRecoveryEmail, PASSWORD_RECOVERY_EVENT, recoveryExpired, redactedTargetedPayload } from "./password-recovery-notification";

const token = "A".repeat(43);
const resetUrl = `https://web.example/reset-password#email=person%40example.com&token=${token}`;
test("email reset proof stays in a fragment and HTML link parameters are escaped", () => {
  const message = formatPasswordRecoveryEmail({ resetUrl });
  assert.ok(message.html.includes("&amp;token="));
  assert.ok(message.html.includes(token));
  assert.ok(!message.html.includes("?token="));
  assert.ok(!message.html.includes("тимчасовий пароль"));
});
test("notification history strips credentials for new links and legacy temporary-password messages", () => {
  for (const event of [PASSWORD_RECOVERY_EVENT, "auth.password_reset_requested"])
    assert.deepEqual(redactedTargetedPayload(event, { resetUrl, tempPassword: "SYNTHETIC", expiresInMinutes: 15 }), { expiresInMinutes: 15 });
});
test("expired or malformed delivery is rejected before sending", () => {
  const now = Date.parse("2026-10-23T09:00:00Z");
  assert.equal(recoveryExpired({ expiresAt: "2026-10-23T09:00:00Z" }, now), true);
  assert.equal(recoveryExpired({}, now), true);
  assert.equal(recoveryExpired({ expiresAt: "2026-10-23T09:15:00Z" }, now), false);
});
for (const url of ["javascript:alert(1)", resetUrl.replace("https:", "http:"),
  resetUrl.replace("web.example", "name:secret@web.example"), "https://web.example/reset-password?token=marker", "https://web.example/other#token=marker"])
  test("unsafe email notification URL is refused: " + url.split(":")[0], () => {
    assert.throws(() => formatPasswordRecoveryEmail({ resetUrl: url }), /Invalid password recovery/);
  });
