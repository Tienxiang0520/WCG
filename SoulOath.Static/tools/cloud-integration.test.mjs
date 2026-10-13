import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import {build} from '../../WcgWeb/Client/node_modules/esbuild/lib/main.js';
import {browserLeaseId} from '../wwwroot/cloud-device.js';

const compiled=await build({entryPoints:[new URL('../wwwroot/cloud.js',import.meta.url).pathname],bundle:true,write:false,format:'iife',platform:'browser',plugins:[{name:'test-auth',setup(b){b.onResolve({filter:/cloud-auth\.js$/},()=>({path:'auth',namespace:'test'}));b.onLoad({filter:/.*/,namespace:'test'},()=>({contents:'export async function createAuth(){return window.testAuth;}',loader:'js'}));}}]});
const code=compiled.outputFiles[0].text;
const device='aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa', otherDevice='bbbbbbbb-bbbb-4bbb-bbbb-bbbbbbbbbbbb';
const account={homeAccountId:'account-A',name:'Test Player'};
const accountKey='wcg.player.account.account-A',metaKey='wcg.cloud.meta.account-A';
const snapshot={data:{ranked:'{"Stars":14}'},preferences:{'wcg.confirmEnergy':'true'}};
const root=()=>new Map([[accountKey,JSON.stringify({version:1,data:snapshot.data})],[metaKey,JSON.stringify({etag:'v1',dirty:false,preferences:snapshot.preferences})],['soul-oath.player.v1',JSON.stringify({version:1,data:{profile:'{"Guest":true}'}})]]);
const storage=map=>({getItem:key=>map.get(key)??null,setItem:(key,value)=>map.set(key,value),removeItem:key=>map.delete(key)});
async function open({map=root(),session=new Map(),writer=true,server={lease:null},switchDuringToken=false}={}){
    const events=new Map(),requests=[];let selected=account;
    const context={window:{soulOathStorage:{ready:Promise.resolve(),isWriter:()=>writer},addEventListener:(type,fn)=>events.set(type,fn),dispatchEvent(){}},localStorage:storage(map),sessionStorage:storage(session),location:{origin:'http://localhost:5186',replace(){context.replaced=true;}},crypto:{randomUUID:()=>otherDevice},CustomEvent:class{},AbortSignal,clearInterval(){},clearTimeout(){},setInterval(){return 1;},setTimeout(){return 1;},console,URL,Blob};
    context.window.testAuth={getActiveAccount:()=>selected,acquireTokenSilent:async()=>{if(switchDuringToken)selected={homeAccountId:'account-B'};return {accessToken:'test-only-not-a-microsoft-token'};},clearCache:async()=>{selected=null;}};
    context.fetch=async(url,options={})=>{
        if(url==='cloud-config.json')return {ok:true,json:async()=>({clientId:'test',apiUrl:'https://test.invalid/api',scope:'test'})};
        requests.push({url,method:options.method,body:options.body&&JSON.parse(options.body)});
        if(url.endsWith('/save/lease')){
            const body=JSON.parse(options.body);
            if(body.action==='release'){assert.equal(body.leaseId,server.lease);server.lease=null;}
            else if(server.lease&&server.lease!==body.leaseId)return {status:409,ok:false,json:async()=>({code:'other_device'})};
            else server.lease=body.leaseId;
        }
        return {status:200,ok:true,json:async()=>({exists:true,save:snapshot,etag:'v1',updatedAt:'2026-10-13T00:00:00Z'})};
    };
    vm.createContext(context);vm.runInContext(code,context);await context.window.wcgCloud.initialize();
    return {cloud:context.window.wcgCloud,map,session,requests,server,context,dispatch:type=>events.get(type)?.(),close:()=>events.get('pagehide')?.()};
}

test('closing and reopening a writer tab reuses the existing browser lease and preserves the guest',async()=>{
    const map=root(),server={lease:device};const before=map.get('soul-oath.player.v1');
    const first=await open({map,server,session:new Map([['wcg.cloud.device',device]])});
    assert.equal(first.cloud.status().mode,'synced');first.close();
    const reopened=await open({map,server});
    assert.equal(reopened.cloud.status().mode,'synced');assert.equal(reopened.cloud.canWrite(),true);
    assert.equal(reopened.session.get('wcg.cloud.device'),device);
    assert.equal(map.get('soul-oath.player.v1'),before);
    assert.ok(reopened.requests.filter(r=>r.body).every(r=>r.body.leaseId===device));
});
test('a second tab never reads, rewrites or renews the first tab cloud save',async()=>{
    const map=root();map.set('wcg.cloud.device',device);
    const meta=JSON.parse(map.get(metaKey));meta.dirty=true;map.set(metaKey,JSON.stringify(meta));
    const before=[...map];const second=await open({map,writer:false,server:{lease:device}});
    assert.equal(second.cloud.status().mode,'other-tab');assert.equal(second.cloud.canWrite(),false);
    await second.cloud.sync();
    second.dispatch('wcg-preference-saved');
    await assert.rejects(second.cloud.useCloud(),error=>error.kind==='other-tab');
    assert.equal(second.cloud.status().mode,'other-tab');assert.deepEqual([...map],before);
    assert.equal(second.requests.length,0);assert.equal(second.session.size,0);
});
test('signing out of a read-only tab cannot release another tab lease or rewrite preferences',async()=>{
    const map=root();map.set('wcg.confirmEnergy','true');map.set('wcg.guest-preferences.v1',JSON.stringify({'wcg.confirmEnergy':'false'}));
    const before=[...map];const server={lease:device};const second=await open({map,writer:false,server});
    await second.cloud.logout();assert.equal(server.lease,device);assert.equal(second.requests.length,0);assert.equal(second.context.replaced,true);
    assert.deepEqual([...map],before);
});
test('account changes during silent authentication stop requests before any player data is sent',async()=>{
    const map=root();const before=map.get(accountKey);const page=await open({map,switchDuringToken:true});
    assert.equal(page.cloud.status().mode,'reauth');assert.equal(page.requests.length,0);assert.equal(map.get(accountKey),before);
});
test('another browser stays a distinct device and cannot silently acquire the old browser lease',async()=>{
    const page=await open({server:{lease:device}});
    assert.equal(page.cloud.status().mode,'other-device');assert.equal(page.cloud.canWrite(),false);assert.equal(page.map.get('wcg.cloud.device'),otherDevice);
});
test('invalid persisted device ids are replaced without resetting any player data',()=>{
    const map=root();map.set('wcg.cloud.device','broken');const session=new Map();
    assert.equal(browserLeaseId(storage(map),storage(session),()=>device),device);assert.equal(map.get(accountKey),JSON.stringify({version:1,data:snapshot.data}));
});
