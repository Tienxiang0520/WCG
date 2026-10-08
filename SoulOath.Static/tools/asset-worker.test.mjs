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
