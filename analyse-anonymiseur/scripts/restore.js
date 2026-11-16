// node restore.js <dir> <table> <analyse> [password]
const { chromium } = require('playwright');
const [dir, table, analyse, pw] = process.argv.slice(2);
(async () => {
  const b = await chromium.launch(); const ctx = await b.newContext({acceptDownloads:true});
  const reqs=[]; ctx.on('request', r=>reqs.push(r.method()+' '+r.url()));
  const p = await ctx.newPage();
  await p.goto('https://worldofnumeric.fr/outils/anonymiseur/app/', {waitUntil:'networkidle'}); const mark=reqs.length;
  await p.click('[data-view="restore"][data-action="nav"]'); await p.waitForTimeout(800);
  await p.setInputFiles('#fileRsKey', dir+'/'+table); await p.waitForTimeout(1500);
  if (pw) {
    await p.fill('#rsPw', pw);
    await p.getByRole('button',{name:'Ouvrir la table'}).click(); await p.waitForTimeout(5000);
    const T0=await p.innerText('#view-restore'); console.log('AFTERPW', T0.slice(T0.indexOf('Votre table'), T0.indexOf('Votre table')+250).replace(/\n+/g,' | '));
    if (!/codes connus/.test(T0)) { await b.close(); return; }
  }
  console.log('KEYSTATE', (await p.innerText('#view-restore')).slice(0,700).replace(/\n+/g,' | '));
  await p.setInputFiles('#fileRid', dir+'/'+analyse); await p.waitForTimeout(3000);
  const T = await p.innerText('#view-restore');
  console.log('RESULT', T.slice(T.indexOf('3\nL’analyse'), T.indexOf('3\nL’analyse')+1500));
  const btns = p.locator('#view-restore button:visible'); console.log('BTNS', (await btns.allInnerTexts()).join(' | '));
  const dlb = btns.filter({hasText:/Télécharger/});
  if (await dlb.count()) { const [dl]=await Promise.all([p.waitForEvent('download',{timeout:15000}), dlb.first().click()]); const o=dir+'/RESTORED_'+dl.suggestedFilename(); await dl.saveAs(o); console.log('saved', o); }
  console.log('NONSTATIC_REQS', reqs.slice(mark).filter(u=>!/\/assets\//.test(u)).join('\n')||'none');
  await b.close();
})();
