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
    const s=scene({dirty:true});await s.engine.initialize();let release,started;
    const ready=new Promise(resolve=>started=resolve);
    const write=s.remote.write;s.remote.write=async(...args)=>{await new Promise(r=>{release=r;started();});return write(...args);};
    const uploading=s.engine.flush();await ready;s.local.write(snapshot(2));s.engine.markDirty();release();await uploading;
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

test('a committed upload whose response is lost is acknowledged without a false conflict',async()=>{
    const s=scene({dirty:true});await s.engine.initialize();
    const write=s.remote.write;s.remote.write=async(...args)=>{await write(...args);throw new Error('response lost');};
    await s.engine.flush();
    assert.equal(s.engine.mode,'synced');assert.equal(s.local.meta().etag,'v2');assert.equal(s.local.meta().dirty,false);
    assert.equal(s.writes.length,1);assert.deepEqual(s.local.read(),snapshot(1));
});
test('lost acknowledgement preserves newer local actions and syncs them with the recovered ETag',async()=>{
    const s=scene({dirty:true});await s.engine.initialize();const write=s.remote.write;
    s.remote.write=async(...args)=>{await write(...args);s.local.write(snapshot(50));s.engine.markDirty();throw new Error('response lost');};
    await s.engine.flush();assert.equal(s.engine.mode,'pending');assert.equal(s.local.meta().etag,'v2');
    assert.deepEqual(s.local.read(),snapshot(50));assert.deepEqual(s.cloud(),snapshot(1));
    s.remote.write=write;await s.engine.flush();assert.equal(s.engine.mode,'synced');assert.deepEqual(s.cloud(),snapshot(50));
});
test('persisted upload proof repairs an interrupted response after reopening with newer local progress',async()=>{
    const s=scene({dirty:true});await s.engine.initialize();const write=s.remote.write,read=s.remote.read;
    s.remote.write=async(...args)=>{await write(...args);s.local.write(snapshot(50));s.engine.markDirty();throw new Error('response lost');};
    s.remote.read=async()=>{throw new Error('offline');};await s.engine.flush();assert.equal(s.engine.mode,'offline');
    assert.ok(s.local.meta().pendingUpload?.hash);assert.equal(s.local.meta().etag,'v1');
    s.remote.read=read;s.remote.write=write;
    const reopened=new CloudSync({local:s.local,remote:s.remote});await reopened.initialize();
    assert.equal(reopened.mode,'pending');assert.equal(s.local.meta().etag,'v2');assert.deepEqual(s.local.read(),snapshot(50));
    await reopened.flush();assert.equal(reopened.mode,'synced');assert.deepEqual(s.cloud(),snapshot(50));
});
test('identical cloud and local snapshots recover even without a pending proof from older clients',async()=>{
    const s=scene({dirty:true,serverEtag:'v2'});await s.engine.initialize();
    assert.equal(s.engine.mode,'synced');assert.equal(s.local.meta().etag,'v2');assert.equal(s.writes.length,0);
});
test('another cloud branch cannot be accepted just because a prior upload lost its response',async()=>{
    const s=scene({dirty:true});await s.engine.initialize();const write=s.remote.write,read=s.remote.read;
    s.remote.write=async(...args)=>{await write(...args);throw new Error('lost');};
    s.remote.read=async()=>{throw new Error('offline');};await s.engine.flush();
    s.remote.read=read;s.server(snapshot(9),'v3');
    const reopened=new CloudSync({local:s.local,remote:s.remote});await reopened.initialize();
    assert.equal(reopened.mode,'conflict');assert.equal(reopened.canWrite(),false);
    assert.equal(s.local.meta().etag,'v1');assert.deepEqual(s.local.read(),snapshot(1));assert.deepEqual(s.cloud(),snapshot(9));
});
test('a matching snapshot never silently takes over another device lease',async()=>{
    const s=scene({dirty:true,serverEtag:'v2'});s.other();await s.engine.initialize();
    assert.equal(s.engine.mode,'other-device');assert.equal(s.local.meta().etag,'v1');assert.equal(s.writes.length,0);
});
test('failure to persist upload proof prevents PUT and leaves progress queued',async()=>{
    const s=scene({cached:snapshot(3),dirty:true});await s.engine.initialize();const setMeta=s.local.setMeta;
    s.local.setMeta=m=>{if(m.pendingUpload)throw new Error('quota');setMeta(m);};
    await s.engine.flush();assert.equal(s.engine.mode,'offline');assert.equal(s.local.meta().dirty,true);
    assert.equal(s.writes.length,0);assert.deepEqual(s.local.read(),snapshot(3));assert.deepEqual(s.cloud(),snapshot(1));
});
test('retry after offline recovery checks the earlier upload before replacing its proof',async()=>{
    const s=scene({dirty:true});await s.engine.initialize();const write=s.remote.write,read=s.remote.read;
    s.remote.write=async(...args)=>{await write(...args);s.local.write(snapshot(50));s.engine.markDirty();throw new Error('lost');};
    s.remote.read=async()=>{throw new Error('offline');};await s.engine.flush();
    s.remote.read=read;s.remote.write=write;await s.engine.flush();
    assert.equal(s.engine.mode,'pending');assert.equal(s.local.meta().etag,'v2');assert.equal(s.writes.length,1);
    await s.engine.flush();assert.equal(s.engine.mode,'synced');assert.deepEqual(s.cloud(),snapshot(50));
});
test('a timed-out PUT committing during the retry still matches its retained proof',async()=>{
    const s=scene({dirty:true});await s.engine.initialize();const write=s.remote.write;let first;
    s.remote.write=async(...args)=>{first=args;throw new Error('timeout while still processing');};
    await s.engine.flush();s.local.write(snapshot(50));s.engine.markDirty();
    s.remote.write=async(...args)=>{await write(...first);return write(...args);};
    await s.engine.flush();assert.equal(s.engine.mode,'pending');assert.equal(s.local.meta().etag,'v2');
    assert.deepEqual(s.local.read(),snapshot(50));assert.deepEqual(s.cloud(),snapshot(1));
    s.remote.write=write;await s.engine.flush();assert.equal(s.engine.mode,'synced');assert.deepEqual(s.cloud(),snapshot(50));
});
