const { chromium } = require(process.env.PW);
const scenarios = require('./scenarios.js'); const mode = process.argv[2] || 'both'; const only = process.argv[3];
const base = 'http://localhost:8123';
async function run(b, side, s) {
  const ctx = await b.newContext(); const p = await ctx.newPage(); const errs = [];
  p.on('pageerror', e => { errs.push('PAGEERR ' + e.message.slice(0, 160)); if (process.env.STACK) console.log('STACK ' + (e.stack || '').split(String.fromCharCode(10)).slice(0, 4).join(' | ')); });
  p.on('console', m => { if (/REQERR/.test(m.text())) errs.push(m.text().slice(0, 300)); });
  const html = `<!doctype html><html><head><meta charset=utf-8><title>start</title></head><body>${s.body}<script src="/lib/requirejs/require.js"></script><script src="/fx/shims.js"></script>${process.env.TRACE ? '<script src="/fx/spy.js"></script>' : ''}<script src="/fx/boot.js"></script></body></html>`;
  const starts = []; await p.route('**/start.html*', r => { starts.push(r.request().url().replace(base, '').replace(/side=\w+/, 'side=X') + ' ' + r.request().method()); r.fulfill({ contentType: 'text/html', body: html }); });
  await fetch(base + '/__log');
  if (s.now) await p.clock.setFixedTime(new Date(s.now));
  await p.goto(base + '/start.html?side=' + side + (s.query || '')); await p.waitForFunction('window.PAGE_READY', null, { timeout: 8000 }).catch(() => errs.push('NOT READY'));
  const settle = async () => { await p.waitForTimeout(350); };
  await settle();
  if (process.env.TRACE === '1') await p.evaluate(() => { window.SEEN = {}; });
  try { await s.steps(p, settle); } catch (e) { errs.push('STEP ' + e.message.split('\n')[0].slice(0, 160)); }
  await settle();
  if (process.env.TRACE) console.log('TRACE ' + s.name + ' ' + JSON.stringify(await p.evaluate(() => Object.fromEntries(Object.entries(window.SEEN).map(([k, v]) => [k, Object.keys(v)])))));
  const snap = await p.evaluate(() => { const c = document.body.cloneNode(true); c.querySelectorAll('script').forEach(x => x.remove());
    return { url: location.pathname + location.search, title: document.title, body: c.innerHTML.replace(/\s+/g, ' ').replace(/ data-version="[^"]*"/g, '').replace(/ data-nav-key="[^"]*"/g, '').replace(/<title[^>]*>[^<]*<\/title>/g, '').replace(/ ?w3-semi-fade-in/g, '').replace(/<meta[^>]*>/g, '').replace(/ class="undefined"/g, '').replace(/ class=""/g, '').trim(), calls: window.CALLS, ran: window.RAN, values: [...document.querySelectorAll('input,select,textarea')].map(e => (e.name || e.id) + '=' + (e.type === 'checkbox' ? e.checked : e.value)).join('&'), active: document.activeElement && document.activeElement.tagName }; }).catch(e => ({ error: e.message.slice(0, 120) }));
  if (s.ev) snap.ev = String(JSON.stringify(await p.evaluate(s.ev).catch(e => 'EVERR ' + e.message.slice(0, 200)))).replace(/side=\w+/g, 'side=X');
  if (s.ev) snap.calls2 = await p.evaluate(() => window.CALLS);
  snap.url = (snap.url || '').replace(/side=\w+/, 'side=X').replace(/returnUrl=.*/, 'returnUrl=X');
  snap.requests = (await (await fetch(base + '/__log')).json()).map(r => r.replace(/side(=|%3D)\w+/g, 'side=X').replace('.Olive-Requested-With', '.Requested-With')); snap.errors = errs.sort(); snap.starts = starts;
  await ctx.close(); return snap;
}
(async () => { const b = await chromium.launch({ executablePath: process.env.CHROME, headless: true }); let same = 0, diff = 0;
  for (const s of scenarios) { if (only && !s.name.includes(only)) continue;
    const o = await run(b, 'olive', s);
    if (mode === 'olive') { console.log('### ' + s.name + '\n' + JSON.stringify(o).slice(0, +process.env.MAX || 1400)); continue; }
    const m = await run(b, 'platform', s); const keys = Object.keys(o).filter(k => !(s.ignore || []).includes(k) && JSON.stringify(o[k]) !== JSON.stringify(m[k]));
    if (!keys.length) same++; else { diff++; console.log('DIFF ' + s.name); for (const k of keys) { const a = JSON.stringify(o[k]) || '', c = JSON.stringify(m[k]) || ''; let i = 0; while (i < a.length && a[i] === c[i]) i++; i = Math.max(0, i - 60); console.log('  ' + k + ' @' + i + '\n    olive: ' + a.slice(i, i + 420) + '\n    ours:  ' + c.slice(i, i + 420)); } } }
  if (mode !== 'olive') console.log(`same ${same} diff ${diff}`); await b.close(); })();
