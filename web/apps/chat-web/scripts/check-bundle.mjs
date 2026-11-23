// Bundle budget (Phase 22). Run after `vite build`: measures what the chat page loads up front (the
// entry script and the chunks index.html preloads) and the largest single chunk, gzip-compressed,
// and fails when a budget is exceeded. Budgets are set a little above the measured sizes so growth
// is noticed in review rather than by users.
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { gzipSync } from 'node:zlib';

const dist = new URL('../dist/', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1');
const budgets = {
  initialGzipKb: 180, // entry + preloaded chunks of index.html
  largestChunkGzipKb: 80, // any single JavaScript chunk
};

const gzipKb = (file) => gzipSync(readFileSync(file)).length / 1024;
const html = readFileSync(join(dist, 'index.html'), 'utf8');
const initial = [...html.matchAll(/(?:src|href)="\/?(assets\/[^"]+\.js)"/g)].map((m) => m[1]);
const initialKb = initial.reduce((sum, file) => sum + gzipKb(join(dist, file)), 0);

const chunks = readdirSync(join(dist, 'assets'))
  .filter((name) => name.endsWith('.js'))
  .map((name) => ({ name, kb: gzipKb(join(dist, 'assets', name)), rawKb: statSync(join(dist, 'assets', name)).size / 1024 }))
  .sort((a, b) => b.kb - a.kb);
const largest = chunks[0];

const report = [
  `Initial JavaScript (${initial.length} files): ${initialKb.toFixed(1)} kB gzip (budget ${budgets.initialGzipKb} kB)`,
  `Largest chunk: ${largest.name} ${largest.kb.toFixed(1)} kB gzip (budget ${budgets.largestChunkGzipKb} kB)`,
  `Chunks: ${chunks.length}, total ${chunks.reduce((s, c) => s + c.kb, 0).toFixed(1)} kB gzip`,
];
console.log(report.join('\n'));

const failures = [];
if (initialKb > budgets.initialGzipKb) failures.push('initial JavaScript is over budget');
if (largest.kb > budgets.largestChunkGzipKb) failures.push(`${largest.name} is over the single-chunk budget`);
if (failures.length > 0) {
  console.error(`Bundle budget exceeded: ${failures.join('; ')}.`);
  process.exit(1);
}
