const { defineConfig, globalIgnores } = require('eslint/config');
const expoConfig = require('eslint-config-expo/flat');

module.exports = defineConfig([
  globalIgnores([
    '.expo/**',
    'android/**',
    'dist/**',
    'node_modules/**',
    'coverage/**',
    'tsconfig.json.*',
  ]),
  expoConfig,
  {
    files: ['babel.config.js', 'eslint.config.js', 'jest.config.js', 'metro.config.js'],
    languageOptions: {
      sourceType: 'commonjs',
    },
  },
  {
    files: ['**/__tests__/**/*.{ts,tsx}', '**/*.{test,spec}.{ts,tsx}'],
    languageOptions: {
      globals: {
        afterEach: 'readonly',
        beforeEach: 'readonly',
        describe: 'readonly',
        expect: 'readonly',
        it: 'readonly',
        jest: 'readonly',
        test: 'readonly',
      },
    },
  },
  {
    rules: {
      // React Native text is not HTML, so entity escaping is unnecessary and
      // makes Ukrainian user-facing copy harder to maintain.
      'react/no-unescaped-entities': 'off',
      // Keep the new React 19 compiler-oriented diagnostics visible while the
      // existing screens are migrated in their dedicated UX tasks.
      'react-hooks/purity': 'warn',
      'react-hooks/set-state-in-effect': 'warn',
    },
  },
]);
