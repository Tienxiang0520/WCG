import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFile } from 'node:fs/promises';
const source=await readFile(new URL('../../WcgWeb/wwwroot/battle/training.js',import.meta.url),'utf8');
test('training scenes use a separate key and reject stale writes without touching player saves',()=>{
    const map=new Map([['soul-oath.player.v1','original-player-data']]);
    const context={localStorage:{getItem:key=>map.get(key)??null,setItem:(key,value)=>map.set(key,value)}};
    vm.createContext(context);vm.runInContext(source.replaceAll('export function','function'),context);
    assert.equal(context.readScene(),null);context.saveScene('first',null);assert.equal(context.readScene(),'first');
    assert.throws(()=>context.saveScene('stale',null),/其他頁面/);assert.equal(context.readScene(),'first');
    context.saveScene('second','first');assert.equal(context.readScene(),'second');assert.equal(map.get('soul-oath.player.v1'),'original-player-data');
});
test('failed training storage writes leave the last scene intact',()=>{
    const context={localStorage:{getItem:()=> 'original-scene',setItem:()=>{throw new Error('quota');}}};
    vm.createContext(context);vm.runInContext(source.replaceAll('export function','function'),context);
    assert.throws(()=>context.saveScene('new-scene','original-scene'),/quota/);assert.equal(context.readScene(),'original-scene');
});
