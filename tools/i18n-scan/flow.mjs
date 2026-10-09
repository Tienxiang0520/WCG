// English-mode CJK scan (Playwright). Usage: node flow.mjs <base-url> [desktop|mobile] [label]
// Switches Settings to English, walks the pages, and fails if any Han character remains in text, title,
// aria-label, placeholder or alt (allow-list: the native language name 「繁體中文」), on console errors,
// and prints overflowing buttons/labels. Screenshots go to $SHOTS (default ./shots).
import { chromium } from 'playwright-core';
import { mkdirSync } from 'node:fs';
mkdirSync(process.env.SHOTS ?? 'shots', { recursive: true });
const [,, base, mode = 'desktop', label = 'server'] = process.argv;
const mobile = mode === 'mobile';
const browser = await chromium.launch({ executablePath: process.env.CHROME_PATH ?? '/usr/bin/google-chrome', args: ['--no-sandbox'] });
const ctx = await browser.newContext(mobile ? { viewport: { width: 390, height: 664 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true, locale: 'zh-TW' } : { viewport: { width: 1440, height: 900 }, locale: 'zh-TW' });
const page = await ctx.newPage();
const errors = []; page.on('pageerror', e => errors.push(String(e))); page.on('console', m => { if (m.type() === 'error') errors.push('console: ' + m.text()); });
const tap = async loc => { if (mobile) await loc.tap(); else await loc.click(); };
const nav = async r => { await page.evaluate(r => { history.pushState({}, '', r); dispatchEvent(new PopStateEvent('popstate')); }, r); await page.waitForTimeout(1200); };
const ALLOW = ['繁體中文'];
const all = {};
async function scan(name) {
  const found = await page.evaluate(allow => {
    const han = /[\u3400-\u9fff]/; const out = new Set();
    const w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    while (w.nextNode()) { const n = w.currentNode; const t = n.textContent.trim(); if (!t || !han.test(t)) continue; const el = n.parentElement; if (!el || el.closest('script,style,noscript,[hidden]')) continue; if (allow.includes(t)) continue; out.add(t.slice(0, 140)); }
    for (const el of document.querySelectorAll('[title],[aria-label],[placeholder],[alt]')) for (const a of ['title', 'aria-label', 'placeholder', 'alt']) { const v = el.getAttribute(a); if (v && han.test(v) && !allow.includes(v)) out.add(`@${a}: ${v.slice(0, 140)}`); }
    if (han.test(document.title)) out.add('@doctitle: ' + document.title);
    return [...out];
  }, ALLOW);
  const lang = await page.evaluate(() => document.documentElement.lang);
  console.log(`[${name}] lang=${lang} cjk=${found.length}`); for (const f of found) console.log('   ', f);
  all[name] = found; return found;
}
// Detect text overflow on buttons/labels: elements whose scrollWidth exceeds clientWidth.
async function overflow(name) {
  const o = await page.evaluate(() => [...document.querySelectorAll('button, .btn, .hand-availability, .turn-chip, .battle-message, .nav-label, .filter-chip, .badge, .card-title, .v06-hand-card strong, .field-card strong, .target-banner, h1, h2, h3, .segmented button')]
    .filter(e => { const r = e.getBoundingClientRect(); if (!r.width || getComputedStyle(e).visibility === 'hidden') return false; for (let a = e.parentElement; a; a = a.parentElement) { const ox = getComputedStyle(a).overflowX; if (ox === 'auto' || ox === 'scroll') return false; } return e.scrollWidth > e.clientWidth + 2 && getComputedStyle(e).overflow !== 'visible' || r.right > innerWidth + 1; })
    .map(e => `${e.className.toString().slice(0, 40)} "${e.textContent.trim().slice(0, 40)}" ${e.scrollWidth}>${e.clientWidth} right=${Math.round(e.getBoundingClientRect().right)}`).slice(0, 25));
  if (o.length) { console.log(`  overflow[${name}]`); for (const x of o) console.log('     ', x); }
}
const shot = async n => { await page.waitForTimeout(400); await page.screenshot({ path: `${process.env.SHOTS ?? 'shots'}/${label}-${n}-${mode}.png` }); };
await page.goto(base + '/settings', { waitUntil: 'networkidle' }); await page.waitForTimeout(1500);
const later = page.locator('.tutorial-prompt-shade button', { hasText: /稍後再說|Later/ }); if (await later.count()) { await tap(later.first()); await page.waitForTimeout(500); }
await tap(page.locator('.language-switch button', { hasText: 'English' })); await page.waitForTimeout(1200);
await shot('settings'); await scan('settings'); await overflow('settings');
const pages = (process.env.PAGES ?? '/,/tutorial,/ranked,/deckbuilder,/cards,/rules').split(',').filter(Boolean);
for (const p of pages) { await nav(p); await shot(p === '/' ? 'lobby' : p.slice(1)); await scan(p); await overflow(p); }
// Card preview in gallery
if (process.env.FLOW !== 'pages') {
  await nav('/cards'); await tap(page.locator('.gallery-entry .wcg-card').nth(3)); await page.waitForTimeout(600); await shot('card-preview'); await scan('card-preview'); await tap(page.locator('.card-detail .btn-primary'));
  // Deck builder: load a preset into the editor
  await nav('/deckbuilder'); await tap(page.locator('button', { hasText: 'Choose / load deck' })); await page.waitForTimeout(600); await shot('deck-select'); await scan('deck-select');
  await tap(page.locator('button', { hasText: 'Copy and edit' }).first()); await page.waitForTimeout(800); await shot('deckbuilder-loaded'); await scan('deckbuilder-loaded'); await overflow('deckbuilder-loaded');
  // Battle
  await nav('/'); await tap(page.locator('.lobby-actions .btn-primary, button.btn-primary.btn-lg').first()); await page.waitForSelector('.v06-board', { timeout: 30000 }); await page.waitForTimeout(2500);
  await shot('battle'); await scan('battle'); await overflow('battle');
  const card = page.locator('.v06-hand-card').first(); await card.click({ force: true, timeout: 5000 }).catch(() => {}); await page.waitForTimeout(600); await shot('battle-card'); await scan('battle-card'); await tap(page.locator('.card-detail .close-dialog'));
  // end turn and let the computer act
  for (let t = 0; t < 3; t++) {
    const end = page.locator('.v06-board .end-turn'); if (await end.isEnabled().catch(() => false)) { await tap(end); }
    await page.waitForTimeout(6000);
    const choice = page.locator('.v06-board .modal-shade .v06-dialog .choice-list button'); if (await choice.count()) { await scan('battle-choice'); await tap(choice.first()); await page.waitForTimeout(2000); }
  }
  await scan('battle-later'); await shot('battle-later');
  await tap(page.locator('.turn-controls button').nth(1)); await page.waitForTimeout(600); await shot('battle-log'); await scan('battle-log'); await tap(page.locator('.history-dialog .close-dialog'));
  // Ranked replay
  await nav('/ranked'); const rp = page.locator('.record-replay').first();
  if (await rp.count()) { await tap(rp); await page.waitForSelector('.replay-step-card', { timeout: 60000 }); await page.waitForTimeout(800); for (let i = 0; i < 6; i++) { const nb = page.locator('.replay-buttons button').nth(3); if (!(await nb.isEnabled())) { console.log('replay next disabled; error=', await page.locator('.replay-error').allInnerTexts()); break; } await nb.click({ timeout: 3000, force: true }).catch(() => {}); } await page.waitForTimeout(500); await shot('ranked-replay'); await scan('ranked-replay'); await overflow('ranked-replay'); await tap(page.locator('.replay-close')); }
  const dt = page.locator('.record-deck-toggle').first(); if (await dt.count()) { await tap(dt); await page.waitForTimeout(500); await shot('ranked-history'); await scan('ranked-history'); }
  // Tutorial lesson
  await nav('/tutorial'); await tap(page.locator('.lesson-go').first()); await page.waitForSelector('.tutorial-coach .coach-body', { timeout: 60000 }); await page.waitForTimeout(1500);
  await shot('tutorial-lesson'); await scan('tutorial-lesson'); await overflow('tutorial-lesson');
  for (let i = 0; i < 3; i++) { const nx = page.locator('.coach-next'); if (await nx.isEnabled().catch(() => false)) { await tap(nx); await page.waitForTimeout(900); } }
  await shot('tutorial-step'); await scan('tutorial-step');
  // Tour lesson (deck builder tour)
  await nav('/tutorial'); const tours = page.locator('.lesson-go'); const n = await tours.count(); await tap(tours.nth(n - 4)); await page.waitForTimeout(2500); await shot('tutorial-tour'); await scan('tutorial-tour');
}
const total = Object.values(all).reduce((a, b) => a + b.length, 0);
console.log('TOTAL CJK', total, 'errors', errors.length, errors.slice(0, 10));
await browser.close();
if (total || errors.length) process.exit(1);
