/**
 * Derives the brand asset set from the single supplied logo.
 *
 * Everything here is generated from one source file so there is exactly one thing to replace when
 * the logo changes — the canonical `logo.png` — and no hand-cropped variants that quietly drift
 * out of step with it.
 */
import { mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

// sharp is deliberately NOT a workspace dependency: it ships a ~10 MB native binary, and this
// script runs by hand when the brand changes — not on every build or CI install.
let sharp;
try {
  ({ default: sharp } = await import('sharp'));
} catch {
  console.error(
    [
      'This tool needs sharp, which is not a workspace dependency.',
      'It runs by hand when the brand changes, not in a build. Install it and re-run:',
      '',
      '  cd web && npm i -D sharp && node assets/brand/generate.mjs',
      '',
    ].join('\n'),
  );
  process.exit(1);
}

// Usage, from anywhere:
//   node web/assets/brand/generate.mjs                     re-derive from logo-source.png
//   node web/assets/brand/generate.mjs /path/to/new.png    adopt a new lockup
//
// The canonical file is **logo-source.png**, at the highest resolution available. Everything else
// in this folder — logo.png included — is derived from it. Deriving from logo.png instead would
// lose a little resolution on every run, and after a couple of rebrands the favicon would be
// visibly soft.
const OUT = dirname(fileURLToPath(import.meta.url));
const SRC = process.argv[2] ?? join(OUT, 'logo-source.png');

mkdirSync(OUT, { recursive: true });

// Trim the flat white margin so the lockup can be positioned by its own edges rather than by
// whatever padding happened to be in the export.
const trimmed = await sharp(SRC).trim({ threshold: 10 }).toBuffer();
const t = await sharp(trimmed).metadata();
console.log(`trimmed lockup: ${t.width}x${t.height}`);

// 1. Full lockup — the header and login mark. Capped at 900px wide: it is never displayed larger,
//    and shipping a 1774px original would be most of the page weight on a phone.
await sharp(trimmed)
  .resize({ width: 900, withoutEnlargement: true })
  .png({ compressionLevel: 9, palette: true })
  .toFile(join(OUT, 'logo.png'));

// Keep the source alongside its derivatives, so a future rebrand starts from full resolution
// rather than from a display-sized copy.
if (SRC !== join(OUT, 'logo-source.png')) {
  await sharp(SRC).png().toFile(join(OUT, 'logo-source.png'));
}

// 2. The mark alone — the cart, without the wordmark. Used where the lockup would be illegible:
//    a favicon, a square avatar, a collapsed mobile header.
//    The cart occupies the upper-middle of the lockup; take that band and pad it square.
const markTop = 0;
const markHeight = Math.round(t.height * 0.62);
const markLeft = Math.round(t.width * 0.28);
const markWidth = Math.round(t.width * 0.44);

const mark = await sharp(trimmed)
  .extract({ left: markLeft, top: markTop, width: markWidth, height: markHeight })
  .trim({ threshold: 10 })
  .toBuffer();

const m = await sharp(mark).metadata();
const side = Math.max(m.width, m.height);
console.log(`mark: ${m.width}x${m.height} -> square ${side}`);

const square = await sharp({
  create: {
    width: Math.round(side * 1.16), // a little breathing room, so a favicon is not edge-to-edge
    height: Math.round(side * 1.16),
    channels: 4,
    background: { r: 255, g: 255, b: 255, alpha: 1 },
  },
})
  .composite([{ input: mark, gravity: 'center' }])
  .png()
  .toBuffer();

await sharp(square).resize(512, 512).png({ compressionLevel: 9 }).toFile(join(OUT, "logo-mark.png"));

// 3. Horizontal lockup — mark beside wordmark, for slim chrome.
//
// The supplied lockup is stacked: cart above wordmark above tagline, roughly 2:1. Constrained to a
// 40px-tall header that leaves the wordmark about six pixels high and unreadable. Re-composing the
// same two pieces side by side gives roughly 4.5:1, so at the same height the name is legible.
// Still derived from the one source — not a separately drawn asset that could drift.
const wordTop = Math.round(t.height * 0.635);
const wordmark = await sharp(trimmed)
  // Down to the baseline of the wordmark: the tagline is too small to survive header sizing and
  // only makes the lockup taller.
  .extract({ left: 0, top: wordTop, width: t.width, height: Math.round(t.height * 0.235) })
  .trim({ threshold: 10 })
  .toBuffer();

const w = await sharp(wordmark).metadata();
const markH = Math.round(w.height * 2.1);        // the cart reads as the taller element
const markW = Math.round((m.width / m.height) * markH);
const gap = Math.round(markH * 0.16);
const padY = Math.round(markH * 0.08);

await sharp({
  create: {
    width: markW + gap + w.width,
    height: markH + padY * 2,
    channels: 4,
    background: { r: 255, g: 255, b: 255, alpha: 0 },
  },
})
  .composite([
    { input: await sharp(mark).resize({ height: markH }).toBuffer(), left: 0, top: padY },
    {
      input: wordmark,
      left: markW + gap,
      top: padY + Math.round((markH - w.height) / 2),
    },
  ])
  .png({ compressionLevel: 9 })
  .toFile(join(OUT, 'logo-horizontal.png'));

console.log(`horizontal lockup: ${markW + gap + w.width}x${markH + padY * 2}`);

// 4. Favicons. 32px is what a browser tab actually renders; 180px is the iOS home-screen icon.
await sharp(square).resize(32, 32).png().toFile(join(OUT, "favicon-32.png"));
await sharp(square).resize(180, 180).png().toFile(join(OUT, "apple-touch-icon.png"));

// 5. Social preview. 1200x630 is the size every scraper expects; the lockup is centred on white
//    rather than stretched, so it is never distorted by the aspect change.
await sharp({
  create: { width: 1200, height: 630, channels: 4, background: { r: 255, g: 255, b: 255, alpha: 1 } },
})
  .composite([
    { input: await sharp(trimmed).resize({ width: 820, withoutEnlargement: true }).toBuffer(), gravity: 'center' },
  ])
  .png({ compressionLevel: 9 })
  .toFile(join(OUT, "og-image.png"));

console.log('wrote logo.png, logo-horizontal.png, logo-mark.png, favicon-32.png, apple-touch-icon.png, og-image.png');
