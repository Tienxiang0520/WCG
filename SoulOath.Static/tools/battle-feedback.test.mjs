import test from 'node:test';
import assert from 'node:assert/strict';
import { bindFeedback, resetFastForward } from '../../WcgWeb/wwwroot/battle/battle-feedback.js';

function harness(t) {
    const running = [], record = [];
    class El {
        constructor(tag = 'div') { this.tag = tag; this.style = {}; this.attributes = {}; this.dataset = {}; this.children = []; this.textContent = ''; this.inert = false; }
        getAttributeNames() { return ['b-test']; }
        setAttribute(k, v) { this.attributes[k] = v; }
        addEventListener() { }
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
function descendants(el) { return [el, ...el.children.flatMap(descendants)]; }

test('enemy energy stays face down and input unlocks only after snapshot commit', async t=>{
    const h=harness(t);
    await h.settle(h.feedback.present(h.state,h.state,[{type:'energy',side:'computer',instanceId:'private-card',order:0,label:'填能量'}]));
    const flying=h.record.find(r=>r.el.className.includes('event-card'));assert.match(text(flying.el),/背面卡片/);assert.doesNotMatch(text(flying.el),/private-card/);
    assert.equal(descendants(flying.el).filter(el=>el.tag==='img').length,0,'hidden energy must not request card artwork');
    assert.equal(h.root.inert,true);h.feedback.clear();assert.equal(h.root.inert,false);assert.equal(h.body.children.length,0);
});
test('public AI play carries artwork, values and opponent-facing arrows together', async t=>{
    const h=harness(t),card={cardId:'WCG-147',name:'聖堂仲裁護衛',pp:1500,dp:1,arrows:['left','up']};
    await h.settle(h.feedback.present(h.state,h.state,[{type:'play',side:'computer',instanceId:'guard',order:0,card}]));
    const moving=h.record.find(r=>r.el.className.includes('event-card')).el;
    assert.match(moving.className,/enemy-card/);
    assert.deepEqual(descendants(moving).filter(el=>el.tag==='img').map(el=>el.src),['card-art/WCG-147.webp']);
    assert.match(text(moving),/聖堂仲裁護衛.*1500.*1/);
    assert.equal(descendants(moving).filter(el=>el.className?.includes('arrowmark')).length,2);
    h.feedback.clear();
});
test('death uses old visible field even when final snapshot has removed the card',async t=>{
    const h=harness(t);
    await h.settle(h.feedback.present(h.state,h.state,[{type:'death',side:'computer',instanceId:'dead',order:0,label:'交戰'}]));
    assert.ok(h.record.some(r=>r.el.dataset.wcgEffect==='death'));assert.equal(h.unit.style.visibility,'hidden');
    h.feedback.clear();assert.equal(h.unit.style.visibility,undefined);
});
test('taunt frame follows public current status and disappears after silence', async t=>{
    const h=harness(t),card={instanceId:'guard',cardId:'WCG-073',name:'榮耀護衛官',pp:2300,dp:1,text:'嘲諷',arrows:['left','right']};
    const next=status=>({player:{field:[]},computer:{field:[{card,slot:0,status}]}});
    const event={type:'play',side:'computer',instanceId:'guard',order:0,card};
    await h.settle(h.feedback.present(h.state,next(['嘲諷','直立']),[event]));
    const first=h.record.find(r=>r.el.className.includes('event-card')).el;
    assert.match(first.className,/has-taunt/);
    assert.equal(descendants(first).filter(el=>el.src==='battle/taunt-frame.svg').length,1);
    h.feedback.clear();h.record.length=0;
    await h.settle(h.feedback.present(h.state,next(['沉默','直立']),[event]));
    const silenced=h.record.find(r=>r.el.className.includes('event-card')).el;
    assert.doesNotMatch(silenced.className,/has-taunt/);
    assert.equal(descendants(silenced).filter(el=>el.src==='battle/taunt-frame.svg').length,0);
    h.feedback.clear();
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
test('a spell is showcased large with its text, then its effect travels from the showcase to the target', async t=>{
    resetFastForward();
    const h=harness(t);h.unit.getBoundingClientRect=()=>({x:420,y:300,width:100,height:140});const spell={instanceId:'bolt',cardId:'WCG-012',name:'裁決之光',type:'法術',text:'消滅 1 隻敵怪。',arrows:[]};
    await h.settle(h.feedback.present({player:{field:[]},computer:{field:[{card:{instanceId:'dead',name:'x'},slot:0,status:[]}]}},h.state,[
        {type:'play',side:'player',instanceId:'bolt',card:spell,order:0},{type:'death',side:'computer',instanceId:'dead',label:'裁決之光',order:1}]));
    const effects=h.record.map(r=>r.el.dataset.wcgEffect).filter(Boolean);
    assert.deepEqual([...new Set(effects)],['showcase','projectile','death']);
    const show=h.record.find(r=>r.el.dataset.wcgEffect==='showcase').el;
    assert.match(text(show),/裁決之光.*消滅 1 隻敵怪/);
    h.feedback.clear();assert.equal(h.body.children.length,0);
});
test('the spell showcase is the full physical card face: cost medallion, will·type band, arrows as triangles, set line', async t=>{
    resetFastForward();
    const h=harness(t),spell={instanceId:'ward',cardId:'WCG-061',name:'銀色誓言衛',type:'法術',will:'秩序',cost:2,text:'【箭頭：→】右側相鄰格的己方怪物具有聖盾。',arrows:['right']};
    await h.settle(h.feedback.present(h.state,h.state,[{type:'play',side:'player',instanceId:'ward',card:spell,order:0}]));
    const show=h.record.find(r=>r.el.dataset.wcgEffect==='showcase').el,all=descendants(show),cls=c=>all.filter(el=>String(el.className).split(' ').includes(c));
    assert.match(show.className,/wcg-face/);assert.equal(show.dataset.will,'order');
    assert.match(text(cls('face-cost')[0]),/2.*費/);assert.match(text(cls('face-band')[0]),/秩序.*法術/);
    assert.equal(cls('face-arrow').length,1);assert.match(cls('face-arrow')[0].className,/right/);
    assert.match(text(cls('face-text')[0]),/^ *右側相鄰格/,'the arrow prefix is drawn, not printed');
    assert.match(text(cls('face-foot')[0]),/魂誓.*WCG-061/);
    h.feedback.clear();
});
test('fast-forward shortens the remaining animations and is offered as a button and a board-wide tap', async t=>{
    resetFastForward();
    const h=harness(t),events=[{type:'damage',side:'player',amount:1,order:0},{type:'heal',side:'player',amount:1,order:1}];
    await h.settle(h.feedback.present(h.state,h.state,events));
    const normal=h.record.find(r=>r.el.dataset.wcgEffect==='damage').options.duration;
    const layer=h.body.children[0];
    assert.ok(descendants(layer).some(el=>el.className==='event-ff'),'fast-forward button');
    assert.ok(descendants(layer).some(el=>el.className==='event-skip'),'tap anywhere on the board');
    h.feedback.clear();h.record.length=0;h.feedback.fastForward();
    await h.settle(h.feedback.present(h.state,h.state,events));
    const fast=h.record.find(r=>r.el.dataset.wcgEffect==='damage').options.duration;
    assert.ok(fast<=normal/3+1,`${fast} vs ${normal}`);
    h.feedback.clear();resetFastForward();
});
test('a deploy ability badges its source before its result, and keyword statuses from the snapshot are badged', async t=>{
    resetFastForward();
    const h=harness(t);
    const before={player:{field:[{card:{instanceId:'dead',name:'m'},slot:0,status:['直立'],pp:1000}]},computer:{field:[]}};
    const after={player:{field:[{card:{instanceId:'dead',name:'m'},slot:0,status:['聖盾','直立'],pp:1500}]},computer:{field:[]}};
    await h.settle(h.feedback.present(before,after,[{type:'effect',side:'player',instanceId:'dead',label:'進場能力',order:0},{type:'heal',side:'player',amount:1,order:1}]));
    const effects=h.record.map(r=>r.el.dataset.wcgEffect).filter(Boolean);
    assert.ok(effects.indexOf('badge')<effects.indexOf('heal'),effects.join());
    const badges=h.record.filter(r=>r.el.dataset.wcgEffect==='badge').map(r=>r.el.textContent);
    assert.deepEqual(badges,['進場能力','聖盾']);
    assert.ok(h.record.some(r=>r.el.dataset.wcgEffect==='stat'&&r.el.textContent==='+500 PP'));
    h.feedback.clear();
});
