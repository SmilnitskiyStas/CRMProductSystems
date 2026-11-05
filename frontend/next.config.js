const { PHASE_PRODUCTION_BUILD } = require('next/constants');
const { validateBuildApiUrl } = require('./build/api-config.cjs');
const createNextIntlPlugin = require('next-intl/plugin');

const withNextIntl = createNextIntlPlugin('./i18n/request.ts');

/** @type {import('next').NextConfig} */
const config = {
  output: 'standalone',
};

module.exports = (phase) => {
  if (phase === PHASE_PRODUCTION_BUILD) {
    validateBuildApiUrl(process.env.NEXT_PUBLIC_API_URL, process.env.ALLOW_LOCAL_API_BUILD === 'true');
  }
  return withNextIntl(config);
};
