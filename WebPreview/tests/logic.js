const { chromium } = require('playwright');
const N='/tmp/claude-0/-home-claude/6c00615d-1648-5a6c-bdd8-0f13d8cdbcca/scratchpad/npm3/node_modules/three/';
(async()=>{
 const b=await chromium.launch({args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']});
 const pg=await b.newPage({viewport:{width:400,height:300}});
 const errs=[]; pg.on('pageerror',e=>errs.push('PAGEERR '+e.message+' '+(e.stack||'').split('\n')[1]));
 await pg.route('https://cdnjs.cloudflare.com/**',r=>r.fulfill({path:N+'build/three.min.js',contentType:'text/javascript'}));
 await pg.route('https://cdn.jsdelivr.net/**',r=>{const u=r.request().url(); r.fulfill({path:N+(u.includes('GLTF')?'examples/js/loaders/GLTFLoader.js':'examples/js/utils/SkeletonUtils.js'),contentType:'text/javascript'})});
 await pg.route('https://fonts.googleapis.com/**',r=>r.fulfill({body:'',contentType:'text/css'}));
 await pg.goto('http://localhost:8765/page.html'); await pg.waitForTimeout(3000);
 const res=await pg.evaluate(()=>{
  const S=MZ.S, out=[]; const M=JSON.parse(JSON.stringify(window.__M||null));
  const t=(i,fn)=>{ S.mi=i; S.mp=0; const before=S.money; try{fn()}catch(e){out.push(i+' THROW '+e.message);return} MZ.checkMission(); out.push(i+' '+(S.mi>i?'PASS':'fail mp='+S.mp)+' money+'+(S.money-before)); };
  const P=MZ.P;
  // 6 plowPlant
  t(4,()=>{S.plots[0]={s:'plowed',crop:null,g:0,w:0};S.seeds.wheat=2;MZ.plant(0,'wheat')});
  t(5,()=>{S.plots[0].g=1;MZ.harvestPlot(0)});
  // tractor plow 2 plots
  t(6,()=>{S.plots[1].s='empty';S.plots[2].s='empty';const v=S.veh.tractor;v.impl=0;v.r=Math.PI;v.x=24;v.z=-16.4;MZ.tractorWork(v);v.x=34;MZ.tractorWork(v);});
  t(7,()=>{const v=S.veh.tractor;v.impl=1;S.seeds={wheat:2,onion:2};v.x=24;MZ.tractorWork(v);v.x=34;MZ.tractorWork(v);S.plots[3].s='empty';S.plots[4].s='empty';v.impl=0;v.z=-26.4;v.x=14;MZ.tractorWork(v);v.x=24;MZ.tractorWork(v);});
  t(8,()=>MZ.enter('truck'));
  t(9,()=>{S.goods={crops:120};MZ.loadCargo()});
  t(10,()=>{MZ.sellCargo()});
  t(11,()=>{S.contracts.push({g:'milk',kg:60,left:60,mult:1.5,until:S.day+3}); const c=S.contracts[S.contracts.length-1]; S.veh.truck.cargo[c.g]=c.left; MZ.sellCargo()});
  MZ.exitVehicle();
  // shop: open & buy by function to emit
  t(12,()=>{S.money+=1000; MZ.openShop(); document.querySelector('[data-i="4"]').click();});
  t(13,()=>{S.money+=1000; document.querySelector('[data-i="5"]').click();});
  document.getElementById('mx').click();
  t(14,()=>{S.pen.crate={eggs:3}; MZ.player.position.set(26,0,9.2); MZ.interact();});
  t(15,()=>{S.pen.clean=0.5; MZ.player.position.set(22,0,22.6); MZ.interact();});
  t(16,()=>{S.gh.forEach(g=>{for(const k in g.mods) g.mods[k]=20})});
  t(17,()=>{S.money+=5000; MZ.openShop(); document.querySelector('[data-i="9"]').click(); document.getElementById('mx').click();});
  t(18,()=>{S.veh.truck.cargo={crops:400}; MZ.sellCargo()});
  t(19,()=>{MZ.emit('earnMoney',10000)});
  t(20,()=>{S.feed=50;S.pen.water=1;S.pen.clean=1;S.animals.push({t:'chicken'}); for(let i=0;i<40;i++){MZ.simulate(5);S.pen.water=1;S.pen.clean=1;}});
  t(21,()=>{S.day=6});
  t(22,()=>{S.money+=20000; MZ.openShop(); document.querySelector('[data-i="6"]').click(); document.querySelector('[data-i="7"]').click(); document.getElementById('mx').click();});
  t(23,()=>{const v=S.veh.tractor;v.impl=2;[0,1,2].forEach(i=>{S.plots[i]={s:'grow',crop:'wheat',g:1,w:1}});v.z=-16.4;[14,24,34].forEach(x=>{v.x=x;MZ.tractorWork(v)})});
  t(24,()=>{S.money=30000});
  out.push('final mi '+S.mi+' animals '+S.animals.map(a=>a.t).join(','));
  return out;
 });
 console.log(res.join('\n')); console.log(errs.join('\n')); await b.close();
})();
