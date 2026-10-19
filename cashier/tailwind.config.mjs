/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: { brand: { 400: '#34d399', 500: '#10b981', 600: '#059669' } },
      fontFamily: { sans: ['Segoe UI', 'system-ui', 'sans-serif'] },
    },
  },
  plugins: [],
};
