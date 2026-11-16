// Usage: node gen.js <dir> <file> [password]
// Charge un fichier, lance, exporte tous les fichiers proposés à l'étape 4 + la table (.json, en clair ou chiffrée)
const { chromium } = require('playwright');
const [dir, file, pw] = process.argv.slice(2);
(async () => {
  const b = await chromium.launch();
  const ctx = await b.newContext({ acceptDownloads: true, viewport:{width:1400,height:900} });
  const reqs = []; ctx.on('request', r => reqs.push(r.method()+' '+r.url()));
  const p = await ctx.newPage();
  p.on('pageerror', e => console.log('PAGEERR', e.message));
  await p.goto('https://worldofnumeric.fr/outils/anonymiseur/app/#dashboard', {waitUntil:'networkidle'});
  const mark = reqs.length;
  await p.setInputFiles('#fileMain', dir+'/'+file);
  // attendre fin de traitement (OCR possible)
  for (let i=0;i<90;i++){ await p.waitForTimeout(1000); const t=await p.innerText('main'); if (/informations? masquée|passages? remplacé/.test(t)) break; }
  await p.waitForTimeout(1500);
  await p.screenshot({path: dir+'/'+file+'_run.png', fullPage:true});
  const T = await p.innerText('main');
  const i1=T.indexOf('Vérifiez ce qui est masqué'), i2=T.indexOf('Relisez le fichier');
  console.log('DETECTION\n'+T.slice(i1, i2).replace(/\n{2,}/g,'\n'));
  // exports
  const btns = await p.locator('button:visible').allInnerTexts();
  console.log('BUTTONS', btns.filter(x=>/PDF|Word|Excel|PowerPoint|seul|Version|\.|Table/.test(x)).join(' | '));
  const want = [/seul$/i, /^Version (Word|Excel)/i, /^PDF seul/i];
  for (const name of [/_pseudonymise\.\w+$/, /^PDF seul$/, /^Image seule$/, /^Version Word modifiable$/, /^Version Excel$/]) {
    const bt = p.getByRole('button',{name});
    if (await bt.count()) { try { const [dl]=await Promise.all([p.waitForEvent('download',{timeout:20000}), bt.first().click()]); const o=dir+'/OUT_'+file.replace(/\W/g,'_')+'__'+dl.suggestedFilename(); await dl.saveAs(o); console.log('saved', o);} catch(e){console.log('dl err',name,e.message.slice(0,80));} }
  }
  // table json via menu export
  console.log('DLG_BEFORE', (await p.innerText('#dialogRoot').catch(()=>'')).slice(0,300)); await p.keyboard.press('Escape'); await p.waitForTimeout(300); await p.click('#btnExport',{timeout:5000}).catch(async()=>{const c=p.locator('#dialogRoot button:visible'); console.log('dlgbtns',(await c.allInnerTexts()).join('|')); await c.filter({hasText:/Annuler|Fermer|Plus tard/}).first().click().catch(()=>{}); await p.click('#btnExport');}); await p.waitForTimeout(400);
  const items = await p.locator('#exportMenu button').allInnerTexts(); console.log('MENU', items.join(' || '));
  const tb = p.locator('#exportMenu button').filter({hasText:/\.json/});
  if (await tb.count()) {
    await tb.first().click(); await p.waitForTimeout(800); if(pw){} 
    if (pw) { await p.fill('#kpw1', pw).catch(()=>{}); await p.fill('#kpw2', pw).catch(()=>{}); }
    const dlg = p.locator('#dialogRoot button:visible'); console.log('DIALOG', (await dlg.allInnerTexts()).join(' | '));
    try { const [dl]=await Promise.all([p.waitForEvent('download',{timeout:20000}), dlg.filter({hasText:/Enregistrer|Télécharger/}).last().click()]); const o=dir+'/OUT_'+file.replace(/\W/g,'_')+'__'+dl.suggestedFilename(); await dl.saveAs(o); console.log('saved', o);} catch(e){console.log('table err', e.message.slice(0,100)); console.log(await p.innerText('#dialogRoot').catch(()=>''));}
  }
  console.log('NONSTATIC_REQS', reqs.slice(mark).filter(u=>!/^GET https:\/\/worldofnumeric\.fr\/assets\/[\w.@-]+\.(js|mjs|woff2|css|wasm)(\?v=\d+)?$/.test(u)).join('\n') || 'none');
  await b.close();
})();
