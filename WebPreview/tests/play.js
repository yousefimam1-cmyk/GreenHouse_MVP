const { chromium } = require('playwright');
const N='/tmp/claude-0/-home-claude/6c00615d-1648-5a6c-bdd8-0f13d8cdbcca/scratchpad/npm3/node_modules/three/';
(async()=>{
 const b=await chromium.launch({args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']});
 const ctx=await b.newContext({viewport:{width:1280,height:760}}); const pg=await ctx.newPage();
 const errs=[]; pg.on('pageerror',e=>errs.push('PAGEERR '+e.message+' '+e.stack.split('\n')[1]));
 await pg.route('https://cdnjs.cloudflare.com/**',r=>r.fulfill({path:N+'build/three.min.js',contentType:'text/javascript'}));
 await pg.route('https://cdn.jsdelivr.net/**',r=>{const u=r.request().url(); r.fulfill({path:N+(u.includes('GLTF')?'examples/js/loaders/GLTFLoader.js':'examples/js/utils/SkeletonUtils.js'),contentType:'text/javascript'})});
 await pg.route('https://fonts.googleapis.com/**',r=>r.fulfill({body:'',contentType:'text/css'}));
 await pg.goto('http://localhost:8765/page.html'); await pg.waitForTimeout(4000); console.log('fps', await pg.evaluate(()=>new Promise(r=>{let n=0;const t=performance.now();(function f(){n++; if(performance.now()-t<2000) requestAnimationFrame(f); else r(n/2)})()})));
 await pg.click('#go');
 const ev=(f,a)=>pg.evaluate(f,a); const W=ms=>pg.waitForTimeout(ms||700);
 const key=async c=>{await pg.keyboard.press(c); await W(150)};
 const goTo=async(x,z)=>{await ev(([x,z])=>{MZ.player.position.set(x,0,z)},[x,z]); await W(400)};
 const mi=()=>ev(()=>[MZ.S.mi,MZ.S.mp,MZ.S.money]);
 const log=async l=>console.log(l, JSON.stringify(await mi()), await ev(()=>document.getElementById('prompt').textContent));
 await goTo(-11.5,-5.6); await log('at well'); await key('e'); await W(); await log('after well');
 await goTo(-12.6,-15.4); await key('e'); await W(); await pg.screenshot({path:'p_station.png'});
 // optimize all modules on both greenhouses via buttons
 for (const gi of [0,1]) { await ev(g=>{},gi); await goTo(-12.6, gi?6.6:-15.4); if(gi) {await key('Escape'); await key('e');} for(let d=1;d<=6;d++) await key('Digit'+d); await key('Escape'); }
 await log('after optimize'); console.log('ERRS',errs.slice(0,5).join('\n'), await ev(()=>document.getElementById('modal').hidden));
 await ev(()=>{MZ.simulate(50);}); await W(600); await log('after grow');
 // harvest beds
 const beds=[[-25.3,-14.6],[-19.9,-14.6],[-25.3,-9.4],[-19.9,-9.4]];
 for (const [x,z] of beds){ await goTo(x, z<-12? z+1.6 : z-1.6); await key('e'); }
 await log('after harvest gh1');
 await ev(()=>{MZ.simulate(50);}); for (const [x,z] of beds){ await goTo(x, z<-12? z+1.6 : z-1.6); await key('e'); } await W(500); await log('h2');
 await ev(()=>{MZ.simulate(50);}); for (const [x,z] of beds){ await goTo(x, z<-12? z+1.6 : z-1.6); await key('e'); } await W(500); await log('h3');
 // plant field
 await goTo(14,-9.5); await key('e'); await log('plowed'); await key('e'); await W(); await log('planted');
 await ev(()=>{for(let i=0;i<30;i++)MZ.simulate(10); MZ.S.plots[0].w=1;}); await W(500); await ev(()=>{for(let i=0;i<30;i++){MZ.simulate(10);MZ.S.plots[0].w=1;}}); await W(500);
 await goTo(14,-9.5); await log('field state '+JSON.stringify(await ev(()=>MZ.S.plots[0]))); await key('e'); await W(); await log('field harvest');
 // tractor
 await ev(()=>MZ.enter('tractor')); await W(); await log('in tractor');
 await ev(()=>{const v=MZ.S.veh.tractor; v.x=24; v.z=-10; v.r=Math.PI;}); await pg.keyboard.down('w'); await W(2500); await pg.keyboard.up('w'); await log('tractor drive');
 await pg.screenshot({path:'p_tractor.png'});
 await ev(()=>{const v=MZ.S.veh.tractor; v.x=34; v.z=-10; v.r=Math.PI;}); await pg.keyboard.down('w'); await W(2500); await pg.keyboard.up('w'); await log('tractor 2');
 await key('q'); await ev(()=>{MZ.S.seeds.wheat=4; const v=MZ.S.veh.tractor; v.x=14; v.z=-10; v.r=Math.PI;}); await pg.keyboard.down('w'); await W(2200); await pg.keyboard.up('w');
 await ev(()=>{const v=MZ.S.veh.tractor; v.x=24; v.z=-10; v.r=Math.PI;}); await pg.keyboard.down('w'); await W(2200); await pg.keyboard.up('w'); await log('seeded '+JSON.stringify(await ev(()=>MZ.S.plots.map(p=>p.s))));
 await ev(()=>{const v=MZ.S.veh.tractor; v.fuel=40;});
 await key('e'); await W(); await log('exit');
 // truck
 await ev(()=>MZ.enter('truck')); await W(); await log('truck');
 await ev(()=>{const v=MZ.S.veh.truck; v.x=-2; v.z=-32;}); await W(); await key('r'); await W(); await log('loaded '+JSON.stringify(await ev(()=>MZ.S.veh.truck.cargo)));
 await ev(()=>{const v=MZ.S.veh.truck; v.x=0; v.z=-80; v.r=Math.PI;}); await W(800); await pg.screenshot({path:'p_market.png'});
 await ev(()=>{const v=MZ.S.veh.truck; v.x=0; v.z=-93;}); await W(); await key('r'); await W(); await log('sold');
 await key('m'); await W(); await pg.screenshot({path:'p_mpanel.png'}); await key('Escape');
 // contract: put a contract good into cargo and sell
 await ev(()=>{const c=MZ.S.contracts[0]; MZ.S.veh.truck.cargo[c.g]=c.kg;}); await key('r'); await W(); await log('contract');
 await key('e'); await W();
 // shop
 await goTo(6,9.2); await key('e'); await W(); await pg.screenshot({path:'p_shop.png'});
 await ev(()=>{MZ.S.money+=20000}); await key('Escape'); await key('e');
 for (const d of ['5','6','6','7','8','9']) await key('Digit'+d); await key('Escape'); await log('bought '+JSON.stringify(await ev(()=>[MZ.S.animals.map(a=>a.t),MZ.S.feed,MZ.S.up])));
 await ev(()=>{for(let i=0;i<20;i++)MZ.simulate(10)}); await goTo(26,9.2); await key('e'); await log('collect');
 await goTo(22,22.6); await key('e'); await log('clean');
 await goTo(19,17); await W(800); await pg.screenshot({path:'p_pen.png'});
 for (let i=0;i<40;i++){ await ev(()=>{MZ.S.pen.water=1;MZ.S.pen.clean=1;MZ.S.feed=50;MZ.S.money=Math.max(MZ.S.money,0);for(let j=0;j<10;j++)MZ.simulate(5);MZ.checkMission()}); }
 await log('after sim'); 
 await key('l'); await W(500); await pg.screenshot({path:'p_ar.png'});
 await ev(()=>{MZ.S.time=330; MZ.S.rain=50}); await W(800); await pg.screenshot({path:'p_night.png'});
 console.log(errs.join('\n')); 
 const ph=await b.newPage({viewport:{width:390,height:780},isMobile:true,hasTouch:true});
 for (const u of ['https://cdnjs.cloudflare.com/**']) await ph.route(u,r=>r.fulfill({path:N+'build/three.min.js',contentType:'text/javascript'}));
 await ph.route('https://cdn.jsdelivr.net/**',r=>{const u=r.request().url(); r.fulfill({path:N+(u.includes('GLTF')?'examples/js/loaders/GLTFLoader.js':'examples/js/utils/SkeletonUtils.js'),contentType:'text/javascript'})});
 await ph.route('https://fonts.googleapis.com/**',r=>r.fulfill({body:'',contentType:'text/css'}));
 await ph.goto('http://localhost:8765/page.html',{timeout:90000}); await ph.waitForTimeout(4000); await ph.screenshot({path:'p_phone0.png'}); await ph.click('#go'); await ph.waitForTimeout(800); await ph.screenshot({path:'p_phone.png'});
 await b.close();
})();
