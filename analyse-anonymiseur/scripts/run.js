const { chromium } = require('playwright');
const dir = process.argv[2];
(async () => {
  const b = await chromium.launch();
  const ctx = await b.newContext({ acceptDownloads: true, viewport:{width:1400,height:900} });
  const reqs = [];
  ctx.on('request', r => reqs.push({t:Date.now(), m:r.method(), u:r.url(), post:r.postData()?.slice(0,200)}));
  const p = await ctx.newPage();
  p.on('console', m => console.log('CONSOLE', m.type(), m.text().slice(0,200)));
  p.on('pageerror', e => console.log('PAGEERR', e.message));
  await p.goto('https://worldofnumeric.fr/outils/anonymiseur/app/#dashboard', {waitUntil:'networkidle'});
  const mark = reqs.length;
  await p.screenshot({path: dir+'/1_dash.png'});
  await p.setInputFiles('#fileMain', dir+'/FEC_test.txt');
  await p.waitForTimeout(2500);
  // accept any disclaimer dialog
  for (const txt of ['J’ai compris','J\'ai compris','Accepter','Continuer','OK']) { const btn = p.getByRole('button',{name:txt}); if (await btn.count()) { await btn.first().click(); await p.waitForTimeout(1000);} }
  await p.screenshot({path: dir+'/2_loaded.png', fullPage:true});
  await p.click('#btnRun').catch(e=>console.log('run err',e.message));
  await p.waitForTimeout(4000);
  await p.screenshot({path: dir+'/3_run.png', fullPage:true});
  console.log('TEXT', (await p.innerText('main')).slice(0,3000));
  console.log('--- requests after load:'); reqs.slice(mark).forEach(r=>console.log(r.m, r.u, r.post||''));
  console.log('--- initial requests:'); reqs.slice(0,mark).forEach(r=>console.log(r.m, r.u.slice(0,120)));
  await b.close();
})();
