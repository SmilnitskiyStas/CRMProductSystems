/** Ukrainian plural form for "відгук": 1 відгук, 2–4 відгуки, 5+ відгуків. */
export function reviewWord(n: number): string {
  const m10 = n % 10;
  const m100 = n % 100;
  if (m10 === 1 && m100 !== 11) return "відгук";
  if (m10 >= 2 && m10 <= 4 && (m100 < 10 || m100 >= 20)) return "відгуки";
  return "відгуків";
}
