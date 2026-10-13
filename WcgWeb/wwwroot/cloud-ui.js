export function status(){return window.wcgCloud?.status()??{available:false};}
const listeners=new Map();
export function attach(receiver){
    const change=()=>receiver.invokeMethodAsync('CloudChanged',status()).catch(()=>{});
    window.addEventListener('wcg-cloud-status',change);
    const id=crypto.randomUUID();listeners.set(id,change);return id;
}
export function detach(id){const listener=listeners.get(id);if(listener)window.removeEventListener('wcg-cloud-status',listener);listeners.delete(id);}
export async function act(action){
    if(!['login','logout','sync','useLocal','useCloud','takeControl','exportRecovery'].includes(action))throw new Error('Unsupported cloud action');
    await window.wcgCloud?.[action]();return status();
}
