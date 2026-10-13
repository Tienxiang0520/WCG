// Save synchronization is independent of MSAL and the DOM so race/failure cases can be exercised.
const serializedSave = save => JSON.stringify({data:Object.fromEntries(Object.entries(save.data).sort()),preferences:Object.fromEntries(Object.entries(save.preferences).sort())});
async function saveHash(save) {
    const bytes = await crypto.subtle.digest('SHA-256',new TextEncoder().encode(serializedSave(save)));
    return Array.from(new Uint8Array(bytes),b=>b.toString(16).padStart(2,'0')).join('');
}
export class CloudSync {
    constructor({remote, local, changed=()=>{}}) { this.remote=remote;this.local=local;this.changed=changed;this.mode='loading';this.attached=false;this.busy=false;this.remoteSave=null;this.lastSync=null; }
    state() { return {mode:this.mode,attached:this.attached,busy:this.busy,lastSync:this.lastSync,hasCloud:!!this.remoteSave?.exists}; }
    signal(mode) { if(mode)this.mode=mode;this.changed(this.state()); }
    canWrite() { return !['conflict','other-device','other-tab'].includes(this.mode); }
    async initialize() {
        const cached=this.local.read();const meta=this.local.meta();this.attached=!!cached;this.lastSync=meta.lastSync??null;
        try {
            this.remoteSave=await this.remote.read();
            if(!cached){this.signal('migration');return;}
            if(meta.dirty&&meta.etag!==this.remoteSave.etag){
                await this.remote.lease('acquire');
                if(await this.acknowledge(this.remoteSave))return;
                this.signal('conflict');return;
            }
            if(!meta.dirty&&this.remoteSave.exists){this.local.write(this.remoteSave.save);this.local.setMeta({...meta,etag:this.remoteSave.etag,dirty:false,lastSync:this.remoteSave.updatedAt});this.lastSync=this.remoteSave.updatedAt;}
            if(!meta.dirty&&!this.remoteSave.exists){this.signal('conflict');return;}
            await this.remote.lease('acquire');this.signal(meta.dirty?'pending':'synced');
        } catch(error){this.signal(error.kind==='other-device'?'other-device':error.kind==='other-tab'?'other-tab':error.kind==='unauthorized'?'reauth':'offline');}
    }
    markDirty() {
        if(!this.attached)return;
        this.local.setMeta({...this.local.meta(),dirty:true});
        if(!['conflict','other-device','other-tab'].includes(this.mode))this.signal('pending');
    }
    async acknowledge(remoteSave) {
        if(!remoteSave.exists||!remoteSave.save||!remoteSave.etag)return false;
        const meta=this.local.meta();const remoteHash=await saveHash(remoteSave.save);
        const current=this.local.read();if(!current)return false;
        const identical=await saveHash(current)===remoteHash;
        const attempted=meta.pendingUpload;
        const uploaded=attempted?.etag===meta.etag&&(attempted.hash===remoteHash||attempted.hashes?.includes(remoteHash));
        if(!identical&&!uploaded)return false;
        // No local data is replaced. An acknowledged older upload keeps newer actions queued.
        this.lastSync=remoteSave.updatedAt;this.remoteSave=remoteSave;
        // An unchanged ETag may have been read before a timed-out PUT commits; retain its proof.
        this.local.setMeta({...this.local.meta(),etag:remoteSave.etag,dirty:serializedSave(this.local.read())!==serializedSave(remoteSave.save),lastSync:remoteSave.updatedAt,pendingUpload:remoteSave.etag===meta.etag?meta.pendingUpload:null});
        this.signal(this.local.meta().dirty?'pending':'synced');return true;
    }
    async choose(useLocal, guest) {
        if(this.busy)return;
        this.busy=true;this.signal();
        try {
            await this.remote.lease('acquire');
            const current=await this.remote.read();this.remoteSave=current;
            const save=useLocal?(this.local.read()??guest):(current.save??{data:{},preferences:{}});
            this.local.write(save);this.attached=true;
            this.local.setMeta({etag:current.etag,dirty:useLocal||!current.exists,lastSync:current.updatedAt??null,pendingUpload:null});
            this.lastSync=current.updatedAt??null;this.signal(this.local.meta().dirty?'pending':'synced');
        } catch(error){this.signal(error.kind==='other-device'?'other-device':error.kind==='unauthorized'?'reauth':'offline');throw error;}
        finally{this.busy=false;this.signal();}
        if(this.local.meta().dirty)await this.flush();
    }
    async flush() {
        if(this.busy||!this.attached||['migration','conflict','other-device','other-tab'].includes(this.mode)||!this.local.meta().dirty)return;
        this.busy=true;this.signal('syncing');
        const save=this.local.read();const serialized=JSON.stringify(save);const meta=this.local.meta();
        try {
            await this.remote.lease('acquire');
            if(meta.pendingUpload){
                this.remoteSave=await this.remote.read();
                if(this.remoteSave.etag!==meta.etag){
                    if(await this.acknowledge(this.remoteSave))return;
                    this.signal('conflict');return;
                }
            }
            // Persist the attempted payload's hash before PUT: a server commit may outlive its response or this tab.
            const hash=await saveHash(save);const previous=meta.pendingUpload?.etag===meta.etag?meta.pendingUpload:null;
            const hashes=[...new Set([...(previous?.hashes??(previous?.hash?[previous.hash]:[])),hash])];
            this.local.setMeta({...this.local.meta(),pendingUpload:{etag:meta.etag,hash,hashes}});
            const result=await this.remote.write(save,meta.etag);
            const newer=JSON.stringify(this.local.read())!==serialized;
            this.lastSync=result.updatedAt;
            this.local.setMeta({...this.local.meta(),etag:result.etag,dirty:newer,lastSync:result.updatedAt,pendingUpload:null});
            this.signal(newer?'pending':'synced');
        } catch(error) {
            // Neither local progress nor the cloud snapshot is overwritten on conflict/failure.
            if(!['other-device','other-tab','unauthorized','invalid-save'].includes(error.kind)){
                try {
                    this.remoteSave=await this.remote.read();
                    await this.remote.lease('acquire');
                    if(await this.acknowledge(this.remoteSave))return;
                }catch(readError){if(['other-device','other-tab','unauthorized'].includes(readError.kind))error=readError;}
            }
            this.signal(error.kind==='conflict'?'conflict':error.kind==='other-device'?'other-device':error.kind==='other-tab'?'other-tab':error.kind==='unauthorized'?'reauth':error.kind==='invalid-save'?'invalid-save':'offline');
        } finally{this.busy=false;this.signal();}
    }
    async renew() {
        if(!this.attached||this.busy||['migration','conflict','other-device','other-tab','reauth'].includes(this.mode))return;
        try {await this.remote.lease('renew');if(this.mode==='offline')this.signal(this.local.meta().dirty?'pending':'synced');}
        catch(error){this.signal(error.kind==='other-device'?'other-device':error.kind==='unauthorized'?'reauth':'offline');}
    }
    async takeControl() {
        if(this.busy)return;
        this.busy=true;this.signal();
        try {await this.remote.lease('takeover');this.signal('loading');}
        finally {this.busy=false;this.signal();}
        await this.initialize();
    }
}
