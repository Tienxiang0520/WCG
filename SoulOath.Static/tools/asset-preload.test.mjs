import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { preloadAssets, assetKey, ASSET_CACHE, META_CACHE, validateManifest } from '../wwwroot/asset-preload.js';
import { createAssetManifest } from './asset-manifest.mjs';
const base='https://game.test/';
const hash=value=>createHash('sha256').update(value).digest('hex');
const asset=(path,body)=>({path,size:Buffer.byteLength(body),sha256:hash(body)});
const manifest=assets=>({version:hash(JSON.stringify(assets)),assets});
export function fakeCaches() {
    const stores=new Map();
    return {stores,async open(name){
        if(!stores.has(name))stores.set(name,new Map());
        const store=stores.get(name);
        return {async match(key){return store.get(String(key))?.clone();},async put(key,response){store.set(String(key),response.clone());}};
    }};
}
test('all files must finish and verify before entry; concurrent downloads stay bounded',async()=>{
    const assets=Array.from({length:12},(_,n)=>asset(`card-art/WCG-${n+1}.webp`,`image${n}`));
    const cacheStorage=fakeCaches();const release=[];let active=0,maximum=0,entered=false;
    const job=preloadAssets(manifest(assets),{base,cacheStorage,concurrency:3,fetchFile:async url=>{
        active++;maximum=Math.max(maximum,active);await new Promise(resolve=>release.push(resolve));active--;
        return new Response(`image${assets.findIndex(a=>url.endsWith(a.path))}`);
    }}).then(()=>{entered=true;});
    while(release.length<3)await new Promise(r=>setImmediate(r));
    assert.equal(entered,false);assert.equal(maximum,3);
    for(let batch=0;batch<4;batch++){
        release.splice(0).forEach(resolve=>resolve());
        if(batch<3)while(release.length<3)await new Promise(r=>setImmediate(r));
    }
    await job;assert.equal(entered,true);assert.equal(cacheStorage.stores.get(ASSET_CACHE).size,12);
});
test('interruption blocks entry; reload resumes verified files and retries missing files',async()=>{
    const assets=[asset('card-art/a.webp','ok'),asset('card-art/b.webp','next')];const cacheStorage=fakeCaches();
    await assert.rejects(preloadAssets(manifest(assets),{base,cacheStorage,concurrency:1,fetchFile:async url=>new Response(url.endsWith('a.webp')?'ok':'broken')}),/下載未完成/);
    assert.equal(cacheStorage.stores.get(ASSET_CACHE).size,1);
    assert.equal(cacheStorage.stores.has(META_CACHE),false);
    const requested=[];
    await preloadAssets(manifest(assets),{base,cacheStorage,fetchFile:async url=>{requested.push(url);return new Response('next');}});
    assert.deepEqual(requested,[`${base}card-art/b.webp`]);
    await preloadAssets(manifest(assets),{base,cacheStorage,fetchFile:()=>{throw new Error('should use cache');}});
});
test('update fetches only changed files and retains older versions for open games',async()=>{
    const cacheStorage=fakeCaches();const first=[asset('data/cards.json','old'),asset('card-art/a.webp','picture')];
    await preloadAssets(manifest(first),{base,cacheStorage,fetchFile:async url=>new Response(url.endsWith('.json')?'old':'picture')});
    const second=[asset('data/cards.json','new'),first[1]];const requested=[];
    await preloadAssets(manifest(second),{base,cacheStorage,fetchFile:async url=>{requested.push(url);return new Response('new');}});
    assert.deepEqual(requested,[`${base}data/cards.json`]);
    assert.equal(await (await cacheStorage.open(ASSET_CACHE)).match(assetKey(base,first[0])).then(r=>r.text()),'old');
    assert.equal(cacheStorage.stores.get(META_CACHE).size,2);
});
test('streamed bytes report actual progress, corrupt/error responses never become ready',async()=>{
    const cacheStorage=fakeCaches();const states=[];const item=asset('battle/test.ogg','123456');
    await preloadAssets(manifest([item]),{base,cacheStorage,onProgress:s=>states.push(s),fetchFile:async()=>new Response(new ReadableStream({start(c){c.enqueue(new TextEncoder().encode('123'));c.enqueue(new TextEncoder().encode('456'));c.close();}}))});
    assert.ok(states.some(s=>s.bytes===3&&s.completed===0));assert.equal(states.at(-1).completed,1);
    await assert.rejects(preloadAssets(manifest([asset('data/a.json','ok')]),{base,cacheStorage,fetchFile:async()=>new Response('<html>error</html>',{status:404})}),/下載未完成/);
});
test('cache quota failure blocks entry without deleting existing caches',async()=>{
    const cacheStorage={async open(){return{async match(){},async put(){throw new DOMException('full','QuotaExceededError');}};}};
    await assert.rejects(preloadAssets(manifest([asset('a.js','code')]),{base,cacheStorage,fetchFile:async()=>new Response('code')}),/空間不足/);
});
test('manifest rejects external/traversal/duplicate paths and builds all media plus boot files',async()=>{
    for(const path of ['../secret.json','https://external/a.js','/data/cards.json'])assert.throws(()=>validateManifest(manifest([asset(path,'x')])));
    assert.throws(()=>validateManifest(manifest([asset('a.js','x'),asset('a.js','x')])));
    const dir=await mkdtemp(join(tmpdir(),'wcg-preload-'));
    try{
        for(const path of ['card-art/a.webp','print-art/a.jpg','battle/audio/turn.ogg','_framework/game.wasm','_framework/game.wasm.br','index.html','staticwebapp.config.json','asset-worker.js']){
            await mkdir(join(dir,path,'..'),{recursive:true});await writeFile(join(dir,path),'content');
        }
        const result=await createAssetManifest(dir);
        assert.deepEqual(result.assets.map(a=>a.path),['_framework/game.wasm','battle/audio/turn.ogg','card-art/a.webp','print-art/a.jpg']);
        const again=await createAssetManifest(dir);assert.equal(again.version,result.version);
    }finally{await rm(dir,{recursive:true,force:true});}
});
