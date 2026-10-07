import test from 'node:test';
import assert from 'node:assert/strict';
import { bindFeedback } from '../../WcgWeb/wwwroot/battle/battle-feedback.js';

function harness(t) {
    const running = [], record = [];
    class El {
        constructor(tag = 'div') { this.tag = tag; this.style = {}; this.attributes = {}; this.dataset = {}; this.children = []; this.textContent = ''; this.inert = false; }
        getAttributeNames() { return ['b-test']; }
        setAttribute(k, v) { this.attributes[k] = v; }
        append(...els) { for (const el of els) { el.parent = this; this.children.push(el); } }
        remove() { if (this.parent) this.parent.children = this.parent.children.filter(x => x !== this); }
        getBoundingClientRect() { return { x: 10, y: 20, width: 160, height: 200 }; }
        cloneNode() { const el = new El(); el.textContent = this.textContent; el.style = {...this.style}; return el; }
        querySelector(selector) { return this.selectors?.[selector] ?? null; }
        querySelectorAll(selector) { return this.lists?.[selector] ?? []; }
        animate(frames, options) {
            let resolve, reject;
            const animation = { finished: new Promise((a,b)=>{resolve=a;reject=b;}), done:false,
                finish() { if(!this.done) {this.done=true;resolve();} }, cancel() {if(!this.done){this.done=true;reject(new Error('cancelled'));}} };
            running.push(animation); record.push({el:this, frames, options}); return animation;
        }
    }
    const body = new El(), root = new El(), own = new El(), enemy = new El(), field = new El(), status = new El(), unit = new El(), zone = new El();
    unit.textContent = '公開怪物'; zone.dataset.target = 'dead'; zone.selectors = {'.field-card':unit};
    root.selectors = {'.player.hero':own, '.opponent.hero':enemy, '.battle-message':status, '.battlefield':field, '.energy-zone':own};
    root.lists = {'[data-target]':[zone], '.v06-hand-card':[]};
    const oldDocument = globalThis.document, oldWindow = globalThis.window;
    globalThis.document = {body,createElement:tag=>new El(tag)};
    globalThis.window = {matchMedia:()=>({matches:false})};
    t.after(()=>{globalThis.document=oldDocument;globalThis.window=oldWindow;});
    const state = {player:{field:[]},computer:{field:[]}};
    const feedback = bindFeedback(root);
    async function settle(promise) {
        let done=false, error; promise.then(()=>done=true,e=>{done=true;error=e;});
        for(let i=0;i<100&&!done;i++){for(const a of running)a.finish();await new Promise(setImmediate);}
        assert.ok(done,'presentation must finish');if(error)throw error;
    }
    return {feedback,root,body,record,state,unit,settle};
}
function text(el) {return [el.textContent,...el.children.map(text)].join(' ');}

test('enemy energy stays face down and input unlocks only after snapshot commit', async t=>{
    const h=harness(t);
    await h.settle(h.feedback.present(h.state,h.state,[{type:'energy',side:'computer',instanceId:'private-card',order:0,label:'填能量'}]));
    const flying=h.record.find(r=>r.el.className.includes('event-card'));assert.match(text(flying.el),/背面卡片/);assert.doesNotMatch(text(flying.el),/private-card/);
    assert.equal(h.root.inert,true);h.feedback.clear();assert.equal(h.root.inert,false);assert.equal(h.body.children.length,0);
});
test('death uses old visible field even when final snapshot has removed the card',async t=>{
    const h=harness(t);
    await h.settle(h.feedback.present(h.state,h.state,[{type:'death',side:'computer',instanceId:'dead',order:0,label:'交戰'}]));
    assert.ok(h.record.some(r=>r.el.dataset.wcgEffect==='death'));assert.equal(h.unit.style.visibility,'hidden');
    h.feedback.clear();assert.equal(h.unit.style.visibility,undefined);
});
test('ordered damage then healing completes sequentially and uses separate feedback',async t=>{
    const h=harness(t);
    await h.settle(h.feedback.present(h.state,h.state,[{type:'heal',side:'player',amount:2,order:2,label:'回復'},{type:'damage',side:'player',amount:1,order:1,label:'傷害'}]));
    assert.deepEqual(h.record.filter(r=>r.el.dataset.wcgEffect).map(r=>r.el.textContent),['−1','+2']);h.feedback.clear();
});
test('disposing during presentation cancels animation and restores an existing input lock',async t=>{
    const h=harness(t);h.root.inert=true;
    const pending=h.feedback.present(h.state,h.state,[{type:'damage',side:'player',amount:1,order:0}]);
    h.feedback.dispose();await pending;assert.equal(h.root.inert,true);assert.equal(h.body.children.length,0);
});
