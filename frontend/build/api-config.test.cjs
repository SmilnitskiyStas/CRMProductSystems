const { test } = require('node:test');
const assert = require('node:assert/strict');
const { validateBuildApiUrl } = require('./api-config.cjs');

test('accepts the public production HTTPS API origin', () => {
  assert.equal(validateBuildApiUrl('https://api.agrusystems.pp.ua'), 'https://api.agrusystems.pp.ua');
});
for (const url of [undefined, '', 'http://localhost:5000', 'https://localhost:5000', 'http://127.0.0.1:5101', 'https://127.1', 'http://[::1]:5000', 'https://0.0.0.0', 'https://localhost.', 'https://[::]', 'https://[::ffff:127.0.0.1]', '/api', 'http://api.example.test', 'https://name:password@api.example.test', 'https://api.example.test/api', 'https://api.example.test/?key=value', 'https://api.example.test/#part']) {
  test(`rejects unsafe or missing production API origin: ${url}`, () => assert.throws(() => validateBuildApiUrl(url)));
}
test('allows loopback only with explicit local staging opt-in', () => {
  assert.equal(validateBuildApiUrl('http://localhost:5101', true), 'http://localhost:5101');
  assert.throws(() => validateBuildApiUrl('http://api.example.test', true));
});

test('Next production config rejects the development localhost endpoint', () => {
  const previous = process.env.NEXT_PUBLIC_API_URL;
  const optIn = process.env.ALLOW_LOCAL_API_BUILD;
  try {
    process.env.NEXT_PUBLIC_API_URL = 'http://localhost:5000';
    delete process.env.ALLOW_LOCAL_API_BUILD;
    const config = require('../next.config.js');
    assert.throws(() => config('phase-production-build'), /loopback/);
    process.env.NEXT_PUBLIC_API_URL = 'https://api.agrusystems.pp.ua';
    assert.equal(config('phase-production-build').output, 'standalone');
  } finally {
    if (previous === undefined) delete process.env.NEXT_PUBLIC_API_URL; else process.env.NEXT_PUBLIC_API_URL = previous;
    if (optIn === undefined) delete process.env.ALLOW_LOCAL_API_BUILD; else process.env.ALLOW_LOCAL_API_BUILD = optIn;
  }
});
