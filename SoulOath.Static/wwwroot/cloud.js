import { createAuth } from './cloud-auth.js';
import { CloudSync } from './cloud-sync.js';
import { browserLeaseId } from './cloud-device.js';
import { checkCloudCatalog } from './cloud-version.js';

const guestKey='soul-oath.player.v1', guestPreferencesKey='wcg.guest-preferences.v1';
const prefKeys=['wcg.confirmEnergy','soul-oath.sidebar-width'];
let config,auth,account,engine,activeKey=null,metaKey=null,recoveryKey=null,leaseId=null,timer=null,queued=null,disposed=false;
let publicState={available:false,signedIn:false,mode:'guest',busy:false,name:'',lastSync:null,hasCloud:false,apiMismatch:false};
function summary(save){try{if(!save)return null;const rank=JSON.parse(save.data.ranked??'null');return {decks:JSON.parse(save.data.decks??'[]').length,stars:rank?.Stars??0,activeMatch:!!rank?.Match&&!rank.Match.Settled};}catch{return null;}}
const emit=()=>{let current;try{current=engine?.local.read()??guest();}catch{}publicState={...publicState,...engine?.state(),signedIn:!!account,name:account?.name??account?.username??'',localSummary:summary(current),cloudSummary:summary(engine?.remoteSave?.save),hasRecovery:!!recoveryKey&&!!localStorage.getItem(recoveryKey)};window.dispatchEvent(new CustomEvent('wcg-cloud-status'));};
const prefs=()=>Object.fromEntries(prefKeys.flatMap(key=>{const value=localStorage.getItem(key);return value===null?[]:[[key,value]];}));
function applyPrefs(values){for(const key of prefKeys){if(values[key]===undefined)localStorage.removeItem(key);else localStorage.setItem(key,values[key]);}}
const clone=value=>JSON.parse(JSON.stringify(value));
function guest(){return {data:JSON.parse(localStorage.getItem(guestKey)??'{"version":1,"data":{}}').data,preferences:JSON.parse(localStorage.getItem(guestPreferencesKey)??JSON.stringify(prefs()))};}
function rememberGuest(){if(!activeKey&&tabWritable())localStorage.setItem(guestPreferencesKey,JSON.stringify(prefs()));}
const error=kind=>Object.assign(new Error(kind),{kind});
const tabWritable=()=>window.soulOathStorage?.isWriter?.()!==false;
async function request(path,method='GET',body,headers={}) {
    if(disposed||!account)throw error('unauthorized');
    if(method!=='GET'&&!tabWritable())throw error('other-tab');
    const owner=account.homeAccountId;
    if(auth.getActiveAccount()?.homeAccountId!==owner)throw error('unauthorized');
    let token;
    try {token=(await auth.acquireTokenSilent({account,scopes:[config.scope]})).accessToken;}
    catch {throw error('unauthorized');}
    if(disposed||account?.homeAccountId!==owner||auth.getActiveAccount()?.homeAccountId!==owner)throw error('unauthorized');
    const response=await fetch(config.apiUrl+path,{method,headers:{Authorization:'Bearer '+token,...(body?{'Content-Type':'application/json'}:{}),...headers},body:body?JSON.stringify(body):undefined,cache:'no-store',signal:AbortSignal.timeout(body?.action==='release'?4000:20000)});
    if(response.status===401||response.status===403)throw error('unauthorized');
    if(response.status===409){const data=await response.json();throw error(data.code==='other_device'?'other-device':'conflict');}
    if(response.status===400)throw error('invalid-save');
    if(!response.ok)throw error('offline');
    return response.json();
}
function schedule(){clearTimeout(queued);queued=setTimeout(async()=>{await engine?.flush();if(engine?.mode==='pending')schedule();},1500);}
async function initialize() {
    try {
        await window.soulOathStorage?.ready;
        const response=await fetch('cloud-config.json',{cache:'no-store'});if(!response.ok)return;
        config=await response.json();
        if(!config.clientId||!config.apiUrl||!config.scope)return;
        publicState.available=true;
        auth=await createAuth(config.clientId,config.authority,location.origin+'/auth/callback');
        account=auth.getActiveAccount();
        if(!account){emit();return;}
        if(!localStorage.getItem(guestPreferencesKey))rememberGuest();
        const suffix=encodeURIComponent(account.homeAccountId);
        const accountKey='wcg.player.account.'+suffix;metaKey='wcg.cloud.meta.'+suffix;recoveryKey='wcg.cloud.recovery.'+suffix;
        // Reopening the same browser must not appear to Azure as a second device for 60 seconds.
        if(tabWritable())leaseId=browserLeaseId(localStorage,sessionStorage,()=>crypto.randomUUID());
        const local={
            read(){const root=localStorage.getItem(accountKey);if(!root)return null;return {data:JSON.parse(root).data,preferences:JSON.parse(localStorage.getItem(metaKey)??'{}').preferences??{}};},
            write(save){localStorage.setItem(accountKey,JSON.stringify({version:1,data:save.data}));const meta=this.meta();this.setMeta({...meta,preferences:save.preferences});},
            meta(){return JSON.parse(localStorage.getItem(metaKey)??'{}');},
            setMeta(meta){localStorage.setItem(metaKey,JSON.stringify({...this.meta(),...meta}));}
        };
        const remote={
            read:()=>request('/save'),
            write:(save,etag)=>request('/save','PUT',save,{'If-Match':etag,'X-WCG-Lease':leaseId}),
            lease:action=>request('/save/lease','POST',{action,leaseId})
        };
        engine=new CloudSync({local,remote,changed:()=>emit()});
        if(tabWritable())await engine.initialize();
        else {engine.attached=!!local.read();engine.signal('other-tab');}
        if(engine.attached){activeKey=accountKey;if(tabWritable())applyPrefs(local.read().preferences);}
        emit();
        if(!tabWritable())return;
        // Non-blocking, only once this writer tab is talking to the API: warn when the API validates saves
        // against a different card catalog than this tab runs (/health carries no player data or token).
        if(engine.mode!=='reauth')checkCloudCatalog(config.apiUrl).then(same=>{if(same!==false||disposed)return;publicState.apiMismatch=true;emit();});
        if(engine.mode==='pending')schedule();
        timer=setInterval(async()=>{await engine.renew();if(engine.mode==='pending')schedule();},20000);
    } catch {publicState.mode='offline';emit();}
}
function localChanged(){
    if(!tabWritable())return;
    if(!activeKey){rememberGuest();return;}
    const meta=engine.local.meta();engine.local.setMeta({...meta,preferences:prefs()});engine.markDirty();schedule();
}
function beforeWrite(values=prefs()){
    if(!activeKey||!engine?.attached)return;
    if(!tabWritable())throw error('other-tab');
    if(!engine.canWrite())throw new Error('Cloud save is read-only until its conflict or device ownership is resolved.');
    engine.local.setMeta({...engine.local.meta(),dirty:true,preferences:values});
    if(!['conflict','other-device'].includes(engine.mode))engine.signal('pending');
}
async function login(){rememberGuest();publicState.busy=true;emit();await auth.loginRedirect({scopes:[config.scope],prompt:'select_account',redirectStartPage:location.origin+'/settings'});}
async function logout(){
    try {if(tabWritable())await request('/save/lease','POST',{action:'release',leaseId});}catch{}
    disposed=true;clearInterval(timer);clearTimeout(queued);
    // Every account keeps its own pending cache; signing out never moves it into the guest slot.
    // Clear this game's account cache without redirecting or signing out other Microsoft sites.
    await auth.clearCache({account});
    activeKey=null;if(tabWritable())applyPrefs(JSON.parse(localStorage.getItem(guestPreferencesKey)??'{}'));
    location.replace('/settings');
}
async function choose(local){
    if(!tabWritable())throw error('other-tab');
    const previous=engine.local.read();
    if(previous&&engine.local.meta().dirty)localStorage.setItem(recoveryKey,JSON.stringify(previous));
    await engine.choose(local,guest());
    if(engine.attached){activeKey='wcg.player.account.'+encodeURIComponent(account.homeAccountId);applyPrefs(engine.local.read().preferences);location.replace('/settings');}
}
function exportRecovery(){const saved=JSON.parse(localStorage.getItem(recoveryKey)??'null');if(!saved)return;const url=URL.createObjectURL(new Blob([JSON.stringify({format:'soul-oath-local',version:1,data:saved.data,preferences:saved.preferences,exportedAt:new Date().toISOString()},null,2)],{type:'application/json'}));const link=document.createElement('a');link.href=url;link.download='WCG-保留的本機進度.json';document.body.append(link);link.click();link.remove();setTimeout(()=>URL.revokeObjectURL(url),1000);}
window.wcgCloud={initialize,status:()=>clone(publicState),storageKey:()=>activeKey??guestKey,canWrite:()=>tabWritable()&&(!engine?.attached||engine.canWrite()),beforeWrite,beforePreference:(key,value)=>beforeWrite({...prefs(),[key]:value}),login,logout,sync:()=>engine?.flush(),useLocal:()=>choose(true),useCloud:()=>choose(false),exportRecovery,takeControl:async()=>{await engine.takeControl();if(!['conflict','other-device'].includes(engine.mode))location.reload();}};
window.addEventListener('wcg-local-save',localChanged);
window.addEventListener('wcg-preference-saved',localChanged);
window.addEventListener('online',()=>{if(engine?.attached&&engine.mode==='offline'&&engine.local.meta().dirty){engine.signal('pending');schedule();}});
window.addEventListener('pagehide',()=>{disposed=true;clearInterval(timer);clearTimeout(queued);});
export { initialize };
