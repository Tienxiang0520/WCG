import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
const source=await readFile(new URL('../wwwroot/asset-worker.js',import.meta.url),'utf8');
const root='https://game.test/';
function setup(){
    const stores=new Map(), handlers={};const network=[];
    const caches={async open(name){if(!stores.has(name))stores.set(name,new Map());const store=stores.get(name);return{async match(key){return store.get(String(key))?.clone();},async put(key,r){store.set(String(key),r.clone());}};}};
    const context={URL,Response,caches,self:{registration:{scope:root},location:{origin:'https://game.test'},addEventListener(name,fn){handlers[name]=fn;}},fetch:async r=>{network.push(r.url);return new Response('network');}};
    vm.createContext(context);vm.runInContext(source,context);
    return{caches,handlers,network,restart(){vm.runInNewContext(source,{...context,self:{...context.self,addEventListener(name,fn){handlers[name]=fn;}}});}};
}
async function bind(state,id,body){
    const version=createHash('sha256').update(body).digest('hex');
    const hash=createHash('sha256').update(body).digest('hex');
    const manifest={version,assets:[{path:'data/cards.json',sha256:hash}]};
    await(await state.caches.open('wcg-asset-manifests-v1')).put(`${root}__wcg/manifest/${version}`,new Response(JSON.stringify(manifest)));
    await(await state.caches.open('wcg-assets-v1')).put(`${root}data/cards.json?wcg-hash=${hash}`,new Response(body));
    let pending,reply;
    state.handlers.message({data:{type:'WCG_BIND',version},source:{id},ports:[{postMessage(value){reply=value;}}],waitUntil(p){pending=p;}});
    await pending;assert.equal(reply.ok,true);
}
async function request(state,id,path,extra={}){
    let response;
    state.handlers.fetch({clientId:id,request:{url:new URL(path,root).href,method:'GET',mode:'cors',headers:new Headers(),...extra},respondWith(p){response=p;}});
    return response?await(await response).text():undefined;
}
test('each open tab uses its own complete version, surviving worker restarts',async()=>{
    const state=setup();await bind(state,'old-tab','old-rules');await bind(state,'new-tab','new-rules');
    assert.equal(await request(state,'old-tab','data/cards.json'),'old-rules');
    assert.equal(await request(state,'new-tab','data/cards.json'),'new-rules');
    state.restart();assert.equal(await request(state,'old-tab','data/cards.json'),'old-rules');
    assert.equal(state.network.length,0);
});
test('never caches navigation, external services, writes, or preload requests',async()=>{
    const state=setup();await bind(state,'tab','rules');
    for(const extra of [{mode:'navigate'},{method:'POST'},{url:'https://other.test/a.js'},{headers:new Headers({'X-WCG-Preload':'1'})}])assert.equal(await request(state,'tab','data/cards.json',extra),undefined);
    assert.equal(await request(state,'tab','unknown.json'),'network');
});
test('after binding, closed tabs, their manifests and unreferenced files are removed; open tabs and downloads in progress keep theirs',async()=>{
    const stores=new Map(),handlers={};let live=[];
    const caches={async keys(){return[...stores.keys()];},async delete(name){return stores.delete(name);},async open(name){if(!stores.has(name))stores.set(name,new Map());const store=stores.get(name);
        return{async match(key){return store.get(String(key))?.clone();},async put(key,r){store.set(String(key),r.clone());},async delete(key){return store.delete(String(key));},async keys(){return[...store.keys()].map(url=>({url}));}};}};
    const context={URL,Response,caches,Date,self:{registration:{scope:root},location:{origin:'https://game.test'},clients:{async matchAll(){return live.map(id=>({id}));},async claim(){}},addEventListener(name,fn){handlers[name]=fn;}},fetch:async()=>new Response('network')};
    vm.createContext(context);vm.runInContext(source,context);
    const h=value=>createHash('sha256').update(value).digest('hex');
    const meta=await caches.open('wcg-asset-manifests-v1'),assets=await caches.open('wcg-assets-v1');
    // complete: downloaded, manifest recorded, marker kept until its tab binds (asset-preload.js); pending: still downloading.
    async function version(name,files,{pending=false,started=Date.now()}={}){
        const list=Object.entries(files).map(([path,body])=>({path,sha256:h(body)}));const v=h(name);
        const body=JSON.stringify({version:v,assets:list});
        await meta.put(`${root}__wcg/pending/${v}`,new Response(body,{headers:{'X-WCG-Started':String(started)}}));
        if(!pending)await meta.put(`${root}__wcg/manifest/${v}`,new Response(body));
        for(const a of list)await assets.put(`${root}${a.path}?wcg-hash=${a.sha256}`,new Response(files[a.path]));
        return v;
    }
    async function bindTab(id,v){let p,reply;handlers.message({data:{type:'WCG_BIND',version:v},source:{id},ports:[{postMessage(x){reply=x;}}],waitUntil(x){p=x;}});await p;assert.equal(reply.ok,true);}
    const old=await version('old',{'a.json':'shared','b.json':'old-only'});
    const closed=await version('closed',{'c.json':'closed-only'});
    const busy=await version('busy',{'d.json':'downloading'},{pending:true});
    const stale=await version('stale',{'e.json':'abandoned'},{pending:true,started:Date.now()-7*3600e3});
    const next=await version('new',{'a.json':'shared','f.json':'new-only'});
    live=['old-tab','closed-tab'];await bindTab('old-tab',old);await bindTab('closed-tab',closed);
    live=['old-tab','new-tab'];await bindTab('new-tab',next);
    const keys=[...stores.get('wcg-assets-v1').keys()].map(k=>k.split('?')[0].slice(root.length)).sort();
    assert.deepEqual(keys,['a.json','b.json','d.json','f.json']);
    const metaKeys=[...stores.get('wcg-asset-manifests-v1').keys()];
    assert.ok(!metaKeys.some(k=>k.includes(closed)||k.includes(stale)||k.endsWith('closed-tab')||k.endsWith(`pending/${next}`)||k.endsWith(`pending/${old}`)));
    assert.ok(metaKeys.some(k=>k.endsWith(`manifest/${old}`))&&metaKeys.some(k=>k.endsWith(`pending/${busy}`)));
    // Still served from cache for the open old tab.
    let response;handlers.fetch({clientId:'old-tab',request:{url:`${root}b.json`,method:'GET',mode:'cors',headers:new Headers()},respondWith(p){response=p;}});
    assert.equal(await(await response).text(),'old-only');
    // Renamed cache generations are dropped on activation.
    stores.set('wcg-assets-v0',new Map());let done;handlers.activate({waitUntil(p){done=p;}});await done;
    assert.ok(!stores.has('wcg-assets-v0')&&stores.has('wcg-assets-v1'));
});
test('an uncontrolled (hard-reloaded) tab is claimed on request',async()=>{
    const state=setup();let claimed=0,pending;
    const handlers=[];const context={URL,Response,caches:state.caches,self:{registration:{scope:root},location:{origin:'https://game.test'},clients:{async claim(){claimed++;}},addEventListener(name,fn){if(name==='message')handlers.push(fn);}},fetch:async()=>new Response('network')};
    vm.createContext(context);vm.runInContext(source,context);
    for(const fn of handlers)fn({data:{type:'WCG_CLAIM'},source:{id:'tab'},ports:[],waitUntil(p){pending=p;}});
    await pending;assert.equal(claimed,1);
    pending=undefined;for(const fn of handlers)fn({data:{type:'OTHER'},source:{id:'tab'},ports:[],waitUntil(p){pending=p;}});
    assert.equal(pending,undefined);assert.equal(claimed,1);
});
