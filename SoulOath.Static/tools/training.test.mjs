import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFile } from 'node:fs/promises';
const source=await readFile(new URL('../../WcgWeb/wwwroot/battle/training.js',import.meta.url),'utf8');
function load(map,setItem){
    const context={localStorage:{getItem:key=>map.get(key)??null,setItem:setItem??((key,value)=>map.set(key,value))},Date,Math,JSON,Error,String,Array};
    vm.createContext(context);vm.runInContext(source.replaceAll('export function','function'),context);return context;
}
test('multiple named scenes save, load, rename and delete without touching player saves',()=>{
    const map=new Map([['soul-oath.player.v1','original-player-data']]);const c=load(map);
    assert.equal(c.listScenes().length,0);
    const a=c.saveScene('嘲諷','scene-a');const b=c.saveScene('倒數','scene-b');
    assert.deepEqual(c.listScenes().map(s=>s.name),['嘲諷','倒數']);assert.equal(c.readScene(b),'scene-b');
    c.saveScene('',  'scene-a2',a);assert.equal(c.readScene(a),'scene-a2');assert.equal(c.listScenes()[0].name,'嘲諷');
    c.renameScene(b,'倒數 2');assert.equal(c.listScenes()[1].name,'倒數 2');
    c.deleteScene(a);assert.deepEqual(c.listScenes().map(s=>s.id),[b]);assert.equal(c.readScene(a),null);
    assert.throws(()=>c.saveScene('  ','x'),/名稱/);assert.equal(map.get('soul-oath.player.v1'),'original-player-data');
});
test('the old single slot is migrated and kept',()=>{
    const map=new Map([['wcg.trainingScene.v1','old-scene']]);const c=load(map);
    const [old]=c.listScenes();assert.equal(old.name,'舊保存場面');assert.equal(c.readScene(old.id),'old-scene');
    c.saveScene('新','new');assert.equal(c.listScenes().length,2);assert.equal(map.get('wcg.trainingScene.v1'),'old-scene');
});
test('failed storage writes leave saved scenes intact',()=>{
    const map=new Map([['wcg.trainingScenes.v2',JSON.stringify([{id:'x',name:'n',savedAt:1,scene:'kept'}])]]);
    const c=load(map,()=>{throw new Error('quota');});
    assert.throws(()=>c.saveScene('new','new-scene'),/quota/);assert.equal(c.readScene('x'),'kept');assert.equal(c.listScenes().length,1);
});
