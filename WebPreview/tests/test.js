const { chromium } = require('playwright');
const path=require('path'); const N='/tmp/claude-0/-home-claude/6c00615d-1648-5a6c-bdd8-0f13d8cdbcca/scratchpad/npm3/node_modules/three/';
(async()=>{
 const b=await chromium.launch({args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']});
 const pg=await b.newPage({viewport:{width:1280,height:760}});
 const errs=[]; pg.on('pageerror',e=>errs.push('PAGEERR '+e.message)); pg.on('console',m=>{if(m.type()==='error'||m.type()==='warning')errs.push(m.type()+' '+m.text())});
 await pg.route('https://cdnjs.cloudflare.com/**',r=>r.fulfill({path:N+'build/three.min.js',contentType:'text/javascript'}));
 await pg.route('https://cdn.jsdelivr.net/**',r=>{const u=r.request().url(); r.fulfill({path:N+(u.includes('GLTF')?'examples/js/loaders/GLTFLoader.js':'examples/js/utils/SkeletonUtils.js'),contentType:'text/javascript'})});
 await pg.route('https://fonts.googleapis.com/**',r=>r.fulfill({body:'',contentType:'text/css'}));
 await pg.goto('http://localhost:8765/page.html'); await pg.waitForTimeout(6000);
 await pg.screenshot({path:'s0.png'});
 console.log(errs.join('\n')); await pg.click('#go',{timeout:3000}).catch(e=>console.log('noclick')); await pg.waitForTimeout(500);
 const shot=async(n,fn)=>{ if(fn) await pg.evaluate(fn); await pg.waitForTimeout(1200); await pg.screenshot({path:n}); };
 await shot('s1.png');
 for (const a of process.argv.slice(2)) { const [n,code]=a.split('::'); await shot(n, code); }
 console.log(errs.slice(0,15).join('\n')); await b.close();
})();
