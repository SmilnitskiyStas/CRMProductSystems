// Copies the backend golden vectors over the local copy: npm run sync-vectors
import { copyFileSync } from 'node:fs';
import { join } from 'node:path';

const from = join(import.meta.dirname, '..', '..', 'backend', 'ShelfGuard.Tests', 'Promotions', 'golden-vectors.json');
const to = join(import.meta.dirname, '..', 'src', 'shared', 'promotions', 'golden-vectors.json');
copyFileSync(from, to);
console.log(`copied ${from} -> ${to}`);
