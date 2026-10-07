import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {CUES,createBattleAudio} from '../../WcgWeb/wwwroot/battle/battle-audio.js';

function setup(options={}) {
    const saved=new Map([['wcg.playerDeck','preserve this save']]), sources=[], contexts=[];
    let time=100;
    const context={state:'running',currentTime:10,destination:{},decodeAudioData:options.decode??(async()=>({decoded:true})),
        createGain(){return {gain:{value:0},connect(){},disconnect(){}};},
        createBufferSource(){const s={connect(){},disconnect(){},start(at){this.at=at;},stop(){this.stopped=true;}};sources.push(s);return s;},
        resume(){this.state='running';return Promise.resolve();},close(){this.state='closed';return Promise.resolve();}};
    const sound=createBattleAudio(null,{storage:{getItem:k=>saved.get(k)??null,setItem:(k,v)=>saved.set(k,v)},
        fetch:options.fetch??(async()=>({ok:true,arrayBuffer:async()=>new ArrayBuffer(16)})),
        contextFactory:()=>{contexts.push(context);return context;},hidden:options.hidden??(()=>false),now:()=>time,activated:false});
    return {sound,saved,sources,context,contexts,advance(){time+=100;}};
}
test('all mapped samples are bundled with their original CC0 license and download hashes',async()=>{
    const base=new URL('../../WcgWeb/wwwroot/battle/audio/',import.meta.url);
    const manifest=JSON.parse(await readFile(new URL('manifest.json',base),'utf8'));
    for(const [file] of Object.values(CUES)){
        const entry=manifest.find(x=>x.file===file);assert.ok(entry,file);
        const bytes=await readFile(new URL(file,base));assert.equal(bytes.subarray(0,4).toString(),'OggS');
        assert.equal(createHash('sha256').update(bytes).digest('hex'),entry.sha256);
        assert.match(await readFile(new URL(`licenses/${entry.pack}.txt`,base),'utf8'),/CC0/);
        assert.ok(entry.duration>0&&entry.duration<2);
    }
});
test('decoded attack sample starts at the scheduled collision time',async()=>{
    const h=setup();await h.sound.play('attack',.18);
    assert.equal(h.contexts.length,1);assert.equal(h.sources.length,1);assert.equal(h.sources[0].at,10.18);h.sound.dispose();
});
test('muting during decode prevents a delayed sound and preserves unrelated saves',async()=>{
    let release;const delayed=new Promise(r=>release=r);const h=setup({decode:()=>delayed});
    const play=h.sound.play('damage');h.sound.setEnabled(false);release({decoded:true});await play;
    assert.equal(h.sources.length,0);assert.equal(h.saved.get('wcg.soundEnabled'),'false');assert.equal(h.saved.get('wcg.playerDeck'),'preserve this save');h.sound.dispose();
});
test('dispose stops both scheduled and active sounds and future playback',async()=>{
    const h=setup();await h.sound.play('attack',.23);h.advance();await h.sound.play('death');
    h.sound.dispose();assert.ok(h.sources.every(s=>s.stopped));assert.equal(h.context.state,'closed');
    await h.sound.play('damage');assert.equal(h.sources.length,2);
});
test('bursts are deduplicated and limited to six voices',async()=>{
    const h=setup();await h.sound.play('select');await h.sound.play('select');assert.equal(h.sources.length,1);
    for(let i=0;i<8;i++){h.advance();await h.sound.play('select');}
    assert.equal(h.sources.length,9);assert.equal(h.sources.filter(s=>!s.stopped).length,6);h.sound.dispose();
});
test('missing assets and hidden pages do not reject or start audio',async()=>{
    const missing=setup({fetch:async()=>({ok:false})});await missing.sound.play('damage');assert.equal(missing.sources.length,0);missing.sound.dispose();
    const hidden=setup({hidden:()=>true});await hidden.sound.play('turn');assert.equal(hidden.contexts.length,0);hidden.sound.dispose();
});
