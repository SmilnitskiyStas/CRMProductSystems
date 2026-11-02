function validateBuildApiUrl(raw, allowLocal = false) {
  if (!raw || !raw.trim()) throw new Error('NEXT_PUBLIC_API_URL is required for a production build.');
  let url;
  try { url = new URL(raw); } catch { throw new Error('NEXT_PUBLIC_API_URL must be an absolute API origin.'); }
  const host = url.hostname.toLowerCase().replace(/\.$/, '');
  const local = host === 'localhost' || host.endsWith('.localhost') || host === '[::]' || host.startsWith('[::ffff:7f') || host === '[::1]' || host === '::1' || host === '0.0.0.0' || /^127\./.test(host);
  if (local && !allowLocal) throw new Error('Production API URL cannot point at loopback. Use the public HTTPS API origin.');
  if (url.protocol !== 'https:' && !(local && allowLocal && url.protocol === 'http:')) throw new Error('Production API URL must use HTTPS.');
  if (url.username || url.password || url.search || url.hash || url.pathname !== '/') throw new Error('NEXT_PUBLIC_API_URL must contain only the API origin, without credentials, path, query or fragment.');
  return url.origin;
}
module.exports = { validateBuildApiUrl };
