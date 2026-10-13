import test from 'node:test';
import assert from 'node:assert/strict';
import {CloudSync} from '../wwwroot/cloud-sync.js';
const snapshot=n=>({data:{ranked:JSON.stringify({Stars:n})},preferences:{'wcg.confirmEnergy':'false'}});
function scene({cached=snapshot(1),dirty=false,etag='v1',server=snapshot(1),serverEtag='v1'}={}){
    let value=cached,meta={dirty,etag,preferences:cached?.preferences},cloud=server,version=serverEtag,leaseOwner='me';
    const writes=[];
    const local={read:()=>structuredClone(value),write:s=>{value=structuredClone(s);},meta:()=>structuredClone(meta),setMeta:m=>{meta={...meta,...m};}};
    const remote={
        read:async()=>({exists:!!cloud,save:structuredClone(cloud),etag:version,updatedAt:'2026-10-13T00:00:00Z'}),
        lease:async action=>{if(action==='takeover')leaseOwner='me';if(leaseOwner!=='me')throw Object.assign(new Error(),{kind:'other-device'});},
        write:async(s,e)=>{if(e!==version)throw Object.assign(new Error(),{kind:'conflict'});cloud=structuredClone(s);version='v'+(Number(version.slice(1))+1);writes.push(cloud);return {etag:version,updatedAt:'2026-10-13T01:00:00Z'};}
    };
    const engine=new CloudSync({local,remote});
    return {engine,local,remote,writes,server:(s,e)=>{cloud=s;version=e;},other:()=>{leaseOwner='other';},cloud:()=>cloud};
}
test('first sign-in offers migration and does not overwrite a guest or remote save',async()=>{
    const s=scene({cached:null,server:snapshot(20)});await s.engine.initialize();
    assert.equal(s.engine.mode,'migration');assert.equal(s.engine.attached,false);assert.equal(s.local.read(),null);assert.equal(s.writes.length,0);
    await s.engine.choose(false,snapshot(4));assert.deepEqual(s.local.read(),snapshot(20));assert.equal(s.writes.length,0);
});
test('explicit first upload preserves settings and waits for successful acknowledgement',async()=>{
    const s=scene({cached:null,server:null});await s.engine.initialize();await s.engine.choose(true,snapshot(14));
    assert.deepEqual(s.cloud(),snapshot(14));assert.equal(s.engine.mode,'synced');assert.equal(s.local.meta().dirty,false);
});
test('offline write remains queued across reload rather than being overwritten by cloud data',async()=>{
    const s=scene({cached:snapshot(3),dirty:true});await s.engine.initialize();s.remote.write=async()=>{throw new Error('network');};await s.engine.flush();
    assert.equal(s.engine.mode,'offline');assert.equal(s.local.meta().dirty,true);assert.deepEqual(s.local.read(),snapshot(3));assert.deepEqual(s.cloud(),snapshot(1));
    const reload=new CloudSync({local:s.local,remote:s.remote});await reload.initialize();assert.equal(reload.mode,'pending');assert.deepEqual(s.local.read(),snapshot(3));
});
test('a stale ETag preserves both branches and prevents continued writes until resolved',async()=>{
    const s=scene();await s.engine.initialize();s.local.write(snapshot(4));s.engine.markDirty();s.server(snapshot(8),'v2');await s.engine.flush();
    assert.equal(s.engine.mode,'conflict');assert.equal(s.engine.canWrite(),false);assert.deepEqual(s.local.read(),snapshot(4));assert.deepEqual(s.cloud(),snapshot(8));assert.equal(s.local.meta().dirty,true);
    await s.engine.choose(false,snapshot(0));assert.deepEqual(s.local.read(),snapshot(8));assert.equal(s.engine.canWrite(),true);
});
test('a newer local action while upload is in flight stays queued with the acknowledged ETag',async()=>{
    const s=scene({dirty:true});await s.engine.initialize();let release;
    const write=s.remote.write;s.remote.write=async(...args)=>{await new Promise(r=>release=r);return write(...args);};
    const uploading=s.engine.flush();await new Promise(r=>setImmediate(r));s.local.write(snapshot(2));s.engine.markDirty();release();await uploading;
    assert.equal(s.local.meta().etag,'v2');assert.equal(s.local.meta().dirty,true);assert.equal(s.engine.mode,'pending');assert.deepEqual(s.local.read(),snapshot(2));assert.deepEqual(s.cloud(),snapshot(1));
    s.remote.write=write;await s.engine.flush();assert.deepEqual(s.cloud(),snapshot(2));assert.equal(s.local.meta().dirty,false);
});
test('another device cannot write, and an explicit takeover uses current cloud progress',async()=>{
    const s=scene();s.other();await s.engine.initialize();assert.equal(s.engine.mode,'other-device');assert.equal(s.engine.canWrite(),false);
    s.server(snapshot(9),'v2');await s.engine.takeControl();assert.equal(s.engine.mode,'synced');assert.deepEqual(s.local.read(),snapshot(9));
});
test('bad save validation and expired authentication keep local changes without claiming success',async()=>{
    for(const kind of ['invalid-save','unauthorized']){const s=scene({dirty:true});await s.engine.initialize();s.remote.write=async()=>{throw Object.assign(new Error(),{kind});};await s.engine.flush();assert.equal(s.engine.mode,kind==='unauthorized'?'reauth':'invalid-save');assert.equal(s.local.meta().dirty,true);assert.equal(s.writes.length,0);}
});
