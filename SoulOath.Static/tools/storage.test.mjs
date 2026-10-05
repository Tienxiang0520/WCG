import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFile } from 'node:fs/promises';
const source = await readFile(new URL('../wwwroot/local-data.js', import.meta.url),'utf8');
const playerKey = 'soul-oath.player.v1';
async function browser(map = new Map(), locks = {held:false}, failure = {}) {
    const events = new Map(), tools = [];
    const sandbox = {
        window: {addEventListener(name, fn){events.set(name,[...(events.get(name)??[]),fn]);}},
        navigator: {locks:{async request(name, options, fn){
            if(locks.held) return fn(null);
            locks.held=true;try{return await fn({});}finally{locks.held=false;}
        }}},
        localStorage: {getItem:k=>map.get(k)??null,removeItem:k=>map.delete(k),setItem(k,v){if(failure.key===k)throw new Error('quota exceeded');map.set(k,v);}},
        document: {modelContext:{registerTool(tool){tools.push(tool);}}},
        location: {reload(){sandbox.reloaded=true;}},
        setTimeout, AbortController, Map, Set, Blob, URL,
    };
    vm.createContext(sandbox);vm.runInContext(source,sandbox);
    await sandbox.window.soulOathStorage.ready;
    return {api:sandbox.window.soulOathStorage,map,tools,close:()=>events.get('pagehide')?.forEach(fn=>fn()),sandbox};
}
const backup = (data={},preferences={})=>JSON.stringify({format:'soul-oath-local',version:1,data,preferences});
test('independent save sections survive updates; stale writes are rejected',async()=>{
    const b=await browser();b.api.write('decks','[]',null);b.api.write('ranked','{"Stars":2}',null);
    assert.equal(b.api.read('decks'),'[]');assert.equal(b.api.read('ranked'),'{"Stars":2}');
    assert.throws(()=>b.api.write('decks','[1]',null),/另一個分頁/);assert.equal(b.api.read('decks'),'[]');b.close();
});
test('malformed existing save cannot be overwritten',async()=>{
    const map=new Map([[playerKey,'broken']]);const b=await browser(map);
    assert.throws(()=>b.api.write('decks','[]',null));assert.equal(map.get(playerKey),'broken');b.close();
});
test('write quota failure keeps previous save',async()=>{
    const failure={};const b=await browser(new Map(),{held:false},failure);b.api.write('decks','[]',null);
    failure.key=playerKey;assert.throws(()=>b.api.write('decks','[1]','[]'),/quota/);assert.equal(b.api.read('decks'),'[]');b.close();
});
test('a second tab is read-only until the writer is closed and reopened',async()=>{
    const map=new Map(), locks={held:false};const a=await browser(map,locks),b=await browser(map,locks);
    a.api.write('decks','[]',null);assert.equal(b.api.read('decks'),'[]');
    assert.throws(()=>b.api.write('ranked','{}',null),/另一個魂誓分頁/);
    a.close();await new Promise(resolve=>setImmediate(resolve));const c=await browser(map,locks);c.api.write('ranked','{}',null);assert.equal(c.api.read('ranked'),'{}');c.close();b.close();
});
test('invalid backup leaves all saved data intact',async()=>{
    const b=await browser();b.api.write('decks','[]',null);const original=b.map.get(playerKey);
    assert.throws(()=>b.api.importBackup(backup({secrets:'{}'})));assert.equal(b.map.get(playerKey),original);b.close();
});
test('backup import is committed together and reloads; failure rolls preferences back',async()=>{
    const failure={};const map=new Map([['lcg.confirmEnergy','true']]);const b=await browser(map,{held:false},failure);
    b.api.write('decks','[]',null);const original=map.get(playerKey);failure.key=playerKey;
    assert.throws(()=>b.api.importBackup(backup({decks:'[1]'},{'lcg.confirmEnergy':'false'})),/quota/);
    assert.equal(map.get('lcg.confirmEnergy'),'true');assert.equal(map.get(playerKey),original);assert.equal(b.sandbox.reloaded,undefined);
    failure.key=null;b.api.importBackup(backup({decks:'[]'},{'lcg.confirmEnergy':'false'}));
    assert.equal(b.api.read('decks'),'[]');assert.equal(map.get('lcg.confirmEnergy'),'false');assert.equal(b.sandbox.reloaded,true);b.close();
});
test('browser tools share the save adapter and reject invalid inputs',async()=>{
    const b=await browser();b.api.write('decks','[]',null);const tool=b.tools.find(t=>t.name==='read_saved_decks');
    assert.equal(JSON.stringify(tool.execute({})),'[]');assert.throws(()=>tool.execute({unexpected:true}));
    assert.equal(tool.annotations.readOnlyHint,true);b.close();
});
