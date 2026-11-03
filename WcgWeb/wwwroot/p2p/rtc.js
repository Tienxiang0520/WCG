// 好友對戰 (P2P PvP) transport: one RTCPeerConnection with one negotiated, ordered, reliable data channel.
// Non-trickle ICE: a description is returned only after candidate gathering finishes (or a 6 s cap), so the whole
// offer/answer fits in one copy-paste code. Free public STUN only; no TURN relay (some networks cannot connect).
// Events go back to Blazor through a DotNetObjectReference: OnRtcOpen(), OnRtcMessage(text), OnRtcState(state).
const ICE = [{ urls: 'stun:stun.cloudflare.com:3478' }, { urls: 'stun:stun.l.google.com:19302' }];
const peers = new Map();
let nextId = 1;

function gathered(pc) {
    if (pc.iceGatheringState === 'complete') return Promise.resolve();
    return new Promise(resolve => {
        const done = () => { if (pc.iceGatheringState === 'complete') { pc.removeEventListener('icegatheringstatechange', done); resolve(); } };
        pc.addEventListener('icegatheringstatechange', done);
        setTimeout(resolve, 6000);
    });
}

export function create(dotnet) {
    const id = nextId++;
    const pc = new RTCPeerConnection({ iceServers: ICE });
    const dc = pc.createDataChannel('wcg', { negotiated: true, id: 0, ordered: true });
    const entry = { pc, dc, dotnet, closed: false, queue: [] };
    const call = (name, ...args) => { if (!entry.closed || name === 'OnRtcState') dotnet.invokeMethodAsync(name, ...args).catch(() => { }); };
    dc.onopen = () => { for (const t of entry.queue.splice(0)) dc.send(t); call('OnRtcOpen'); };
    dc.onmessage = e => call('OnRtcMessage', String(e.data));
    dc.onclose = () => call('OnRtcState', 'closed');
    pc.onconnectionstatechange = () => call('OnRtcState', pc.connectionState);
    peers.set(id, entry);
    return id;
}

export async function createOffer(id) {
    const { pc } = peers.get(id);
    await pc.setLocalDescription(await pc.createOffer());
    await gathered(pc);
    return pc.localDescription.sdp;
}

export async function acceptOffer(id, sdp) {
    const { pc } = peers.get(id);
    await pc.setRemoteDescription({ type: 'offer', sdp });
    await pc.setLocalDescription(await pc.createAnswer());
    await gathered(pc);
    return pc.localDescription.sdp;
}

export async function acceptAnswer(id, sdp) {
    const { pc } = peers.get(id);
    await pc.setRemoteDescription({ type: 'answer', sdp });
}

export function send(id, text) {
    const e = peers.get(id);
    if (!e) return false;
    // Sent before this side saw the open event (the peer can open first): delivered in order once open.
    if (e.dc.readyState === 'connecting') { e.queue.push(text); return true; }
    if (e.dc.readyState !== 'open') return false;
    e.dc.send(text);
    return true;
}

export function close(id) {
    const e = peers.get(id);
    if (!e) return;
    e.closed = true;
    try { e.dc.close(); } catch { }
    try { e.pc.close(); } catch { }
    peers.delete(id);
}
