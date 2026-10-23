import test from 'node:test';
import assert from 'node:assert/strict';
import { bindFeedback, resetFastForward } from '../../WcgWeb/wwwroot/battle/battle-feedback.js';

function harness(t) {
    const running = [], record = [];
    class El {
        constructor(tag = 'div') { this.tag = tag; this.style = {}; this.attributes = {}; this.dataset = {}; this.children = []; this.textContent = ''; this.inert = false; }
        getAttributeNames() { return ['b-test']; }
        setAttribute(k, v) { this.attributes[k] = v; }
        addEventListener(type, callback) { (this.listeners ??= {})[type] = callback; }
        removeEventListener(type, callback) { if (this.listeners?.[type] === callback) delete this.listeners[type]; }
        click() { assert.notEqual(this.style.pointerEvents, 'none', 'control must accept pointer input'); this.listeners?.click?.({stopPropagation(){}}); }
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

test('enemy energy stays face down; playback marks the board (actions blocked in C#) without making it inert', async t=>{
    const h=harness(t);
    await h.settle(h.feedback.present(h.state,h.state,[{type:'energy',side:'computer',instanceId:'private-card',order:0,label:'填能量'}]));
    const flying=h.record.find(r=>r.el.className.includes('event-card'));assert.match(text(flying.el),/背面卡片/);assert.doesNotMatch(text(flying.el),/private-card/);
    assert.equal(descendants(flying.el).filter(el=>el.tag==='img').length,0,'hidden energy must not request card artwork');
    assert.equal(h.root.inert,false,'card zoom stays clickable');assert.equal(h.root.dataset.wcgPlayback,'1');h.feedback.clear();assert.equal(h.root.dataset.wcgPlayback,undefined);assert.equal(h.body.children.length,0);
});
test('draws start at the right deck edge; opponent draws never expose artwork', async t=>{
    const h=harness(t);
    h.root.selectors['.battlefield'].getBoundingClientRect=()=>({x:100,y:120,width:900,height:400});
    await h.settle(h.feedback.present(h.state,h.state,[{type:'draw',side:'player',order:0,card:{cardId:'WCG-121',name:'冒險者行囊'}},{type:'draw',side:'computer',order:1}]));
    const draws=h.record.filter(r=>r.el.dataset.wcgDrawOrigin==='right-deck');
    assert.equal(draws.length,2);
    assert.equal(draws[0].el.style.left,'910px');
    assert.equal(draws[0].el.style.top,'344px');
    assert.equal(draws[1].el.style.left,'910px');
    assert.equal(draws[1].el.style.top,'148px');
    assert.match(text(draws[1].el),/背面卡片/);
    assert.equal(descendants(draws[1].el).filter(el=>el.tag==='img').length,0);
    h.feedback.clear();
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
    const h=harness(t),spell={instanceId:'ward',cardId:'WCG-061',name:'銀色誓言衛',type:'法術',will:'秩序',cost:2,text:'【箭頭：→】右側相鄰格的己方怪物具有能量護盾。',arrows:['right']};
    await h.settle(h.feedback.present(h.state,h.state,[{type:'play',side:'player',instanceId:'ward',card:spell,order:0}]));
    const show=h.record.find(r=>r.el.dataset.wcgEffect==='showcase').el,all=descendants(show),cls=c=>all.filter(el=>String(el.className).split(' ').includes(c));
    assert.match(show.className,/wcg-face/);assert.equal(show.dataset.will,'order');
    assert.match(text(cls('face-cost')[0]),/2.*費/);assert.match(text(cls('face-band')[0]),/秩序.*法術/);
    assert.equal(cls('face-arrow').length,1);assert.match(cls('face-arrow')[0].className,/right/);
    assert.match(text(cls('face-text')[0]),/^ *右側相鄰格/,'the arrow prefix is drawn, not printed');
    assert.match(text(cls('face-foot')[0]),/魂誓.*WCG-061/);
    h.feedback.clear();
});
test('vanilla text (無。 / None.) leaves the effect box blank but keeps the will emblem', async t=>{
    for(const plain of ['無。','None.']){
        resetFastForward();
        const h=harness(t),spell={instanceId:'v',cardId:'WCG-101',name:'白板',type:'法術',will:'中立',cost:1,text:plain,arrows:[]};
        await h.settle(h.feedback.present(h.state,h.state,[{type:'play',side:'player',instanceId:'v',card:spell,order:0}]));
        const show=h.record.find(r=>r.el.dataset.wcgEffect==='showcase').el,all=descendants(show),box=all.filter(el=>String(el.className).split(' ').includes('face-text'))[0];
        assert.ok(box,'the box itself stays');assert.equal(text(box).trim(),'');
        assert.ok(descendants(box).some(el=>String(el.className?.baseVal??el.className).includes('face-emblem')));
        h.feedback.clear();
    }
});
test('fast-forward shortens the remaining animations: the button, or a tap on empty board (cards still zoom)', async t=>{
    resetFastForward();
    const h=harness(t),events=[{type:'damage',side:'player',amount:1,order:0},{type:'heal',side:'player',amount:1,order:1}];
    await h.settle(h.feedback.present(h.state,h.state,events));
    const normal=h.record.find(r=>r.el.dataset.wcgEffect==='damage').options.duration;
    const layer=h.body.children[0];
    assert.ok(descendants(layer).some(el=>el.className==='event-ff'),'fast-forward button');
    assert.equal(typeof h.root.listeners.click,'function','tap on empty board');
    const normalHeal=h.record.find(r=>r.el.dataset.wcgEffect==='heal').options.duration;
    h.feedback.clear();h.record.length=0;
    const pending=h.feedback.present(h.state,h.state,events),fastLayer=h.body.children[0];
    const button=descendants(fastLayer).find(el=>el.className==='event-ff');
    button.click();assert.equal(fastLayer.attributes['data-fast'],'3');
    h.root.listeners.click({target:{closest:()=>({})},stopPropagation(){}});assert.equal(fastLayer.attributes['data-fast'],'3','a click on a card zooms instead');
    h.root.listeners.click({target:{closest:()=>null},stopPropagation(){}});assert.equal(fastLayer.attributes['data-fast'],'12');
    await h.settle(pending);
    const fast=h.record.find(r=>r.el.dataset.wcgEffect==='heal').options.duration;
    assert.ok(fast<=normalHeal/12+1,`${fast} vs ${normalHeal}`);
    h.feedback.clear();resetFastForward();
});
test('a freshly summoned unit\'s deploy ability glows without a text badge', async t=>{
    resetFastForward();
    const h=harness(t);
    const after={player:{field:[{card:{instanceId:'mage',name:'m'},slot:0,status:['劇毒','直立'],pp:1000}]},computer:{field:[]}};
    await h.settle(h.feedback.present({player:{field:[]},computer:{field:[]}},after,[{type:'summon',side:'player',instanceId:'mage',card:{instanceId:'mage',name:'m'},order:0},{type:'effect',side:'player',instanceId:'mage',label:'進場能力',order:1},{type:'heal',side:'player',amount:1,order:2}]));
    assert.deepEqual(h.record.filter(r=>r.el.dataset.wcgEffect==='badge').map(r=>r.el.textContent),[]);
    h.feedback.clear();
});
test('a deploy ability badges its source before its result; a keyword gained mid-game pops its icon, not text', async t=>{
    resetFastForward();
    const h=harness(t);
    const before={player:{field:[{card:{instanceId:'dead',name:'m'},slot:0,status:['直立'],pp:1000}]},computer:{field:[]}};
    const after={player:{field:[{card:{instanceId:'dead',name:'m'},slot:0,status:['能量護盾','直立'],pp:1500}]},computer:{field:[]}};
    await h.settle(h.feedback.present(before,after,[{type:'effect',side:'player',instanceId:'dead',label:'進場能力',order:0},{type:'heal',side:'player',amount:1,order:1}]));
    const effects=h.record.map(r=>r.el.dataset.wcgEffect).filter(Boolean);
    assert.ok(effects.indexOf('badge')<effects.indexOf('heal'),effects.join());
    const badges=h.record.filter(r=>r.el.dataset.wcgEffect==='badge').map(r=>r.el.textContent);
    assert.deepEqual(badges,['進場能力']);
    assert.ok(h.record.some(r=>r.el.dataset.wcgEffect==='icon'&&r.el.dataset.keyword==='能量護盾'));
    assert.ok(h.record.some(r=>r.el.dataset.wcgEffect==='stat'&&r.el.textContent==='+500 PP'));
    h.feedback.clear();
});
test('effect taps and attachments show no text; banners never show raw event types', async t=>{
    resetFastForward();
    const h=harness(t);
    const before={player:{field:[{card:{instanceId:'dead',name:'m'},slot:0,status:['直立'],pp:1000,tapped:false}]},computer:{field:[]}};
    const after={player:{field:[{card:{instanceId:'dead',name:'m'},slot:0,status:['直立'],pp:1000,tapped:true}]},computer:{field:[]}};
    const banners=[];
    const pending=h.feedback.present(before,after,[{type:'pay',side:'player',order:0},{type:'attach',side:'player',targetId:'dead',order:1}]);
    const banner=h.body.children[0].children.find(el=>el.className==='event-banner');
    await h.settle(pending.then(()=>{}));
    assert.equal(h.record.filter(r=>r.el.dataset.wcgEffect==='badge').length,0);
    assert.doesNotMatch(banner.textContent,/pay|attach/);
    assert.match(banner.textContent,/附著/);
    h.feedback.clear();
});
test('a shield that breaks in combat shatters its icon', async t=>{
    resetFastForward();
    const h=harness(t);
    const before={player:{field:[{card:{instanceId:'dead',name:'m'},slot:0,status:['聖盾指示物'],pp:1000}]},computer:{field:[]}};
    const after={player:{field:[{card:{instanceId:'dead',name:'m'},slot:0,status:[],pp:1000}]},computer:{field:[]}};
    await h.settle(h.feedback.present(before,after,[{type:'attack',side:'computer',instanceId:'dead',order:0}]));
    assert.ok(h.record.some(r=>r.el.dataset.wcgEffect==='shield-break'));
    h.feedback.clear();
});
test('幸運幣 skips the showcase and only pops the gold energy diamond; own spells are shown briefly', async t=>{
    resetFastForward();
    const h=harness(t),coin={instanceId:'c',cardId:'WCG-263',name:'幸運幣',type:'法術'};
    await h.settle(h.feedback.present(h.state,h.state,[{type:'play',side:'player',instanceId:'c',card:coin,order:0},{type:'energy',side:'player',instanceId:'c',card:coin,label:'臨時能量',order:1}]));
    assert.equal(h.record.filter(r=>r.el.dataset.wcgEffect==='showcase').length,0);
    assert.ok(h.record.some(r=>r.el.dataset.wcgEffect==='coin-energy'));
    h.feedback.clear();h.record.length=0;
    const bolt={instanceId:'b',cardId:'WCG-101',name:'b',type:'法術'};
    await h.settle(h.feedback.present(h.state,h.state,[{type:'play',side:'player',instanceId:'b',card:bolt,order:0}]));
    const own=h.record.filter(r=>r.el.dataset.wcgEffect==='showcase').reduce((n,r)=>n+r.options.duration,0);
    h.feedback.clear();h.record.length=0;
    await h.settle(h.feedback.present(h.state,h.state,[{type:'play',side:'computer',instanceId:'b',card:bolt,order:0}]));
    const enemy=h.record.filter(r=>r.el.dataset.wcgEffect==='showcase').reduce((n,r)=>n+r.options.duration,0);
    assert.ok(own<=500&&own<enemy,`${own} vs ${enemy}`);
    h.feedback.clear();
});
test('DP changes get their own number; stat changes use stat sounds, not heal/damage', async t=>{
    resetFastForward();
    const h=harness(t),played=[];h.feedback.dispose();
    const fb=bindFeedback(h.root,{play:n=>{played.push(n);}});
    const before={player:{field:[{card:{instanceId:'dead',name:'m'},slot:0,status:[],pp:1000,dp:1}]},computer:{field:[]}};
    const after={player:{field:[{card:{instanceId:'dead',name:'m'},slot:0,status:[],pp:1300,dp:2}]},computer:{field:[]}};
    await h.settle(fb.present(before,after,[{type:'status',side:'player',instanceId:'dead',label:'加成',order:0}]));
    const stats=h.record.filter(r=>r.el.dataset.wcgEffect==='stat').map(r=>r.el.textContent);
    assert.deepEqual(stats,['+300 PP','+1 DP']);
    assert.ok(played.includes('statUp')&&!played.includes('heal'),played.join());
    fb.clear();
});
test('a player attack without a drag lunge gets the same lunge as the computer; a dragged one does not repeat it', async t=>{
    resetFastForward();
    const h=harness(t);
    await h.settle(h.feedback.present(h.state,h.state,[{type:'attack',side:'player',instanceId:'dead',order:0}]));
    assert.ok(h.record.some(r=>r.el.dataset.wcgEffect==='attack'&&r.el.dataset.wcgAttackSide==='player'));
    h.feedback.clear();h.record.length=0;
    h.feedback.markLunge('dead');
    await h.settle(h.feedback.present(h.state,h.state,[{type:'attack',side:'player',instanceId:'dead',order:0}]));
    assert.ok(!h.record.some(r=>r.el.dataset.wcgEffect==='attack'));
    h.feedback.clear();
});
test('the computer\'s permanent energy flies to its energy readout', async t=>{
    resetFastForward();
    const h=harness(t),dots={getBoundingClientRect:()=>({x:500,y:40,width:80,height:16})};
    h.root.selectors['.enemy-energy']=dots;
    await h.settle(h.feedback.present(h.state,h.state,[{type:'energy',side:'computer',instanceId:'e',order:0}]));
    const flyer=h.record.find(r=>r.el.className.includes('event-card-back'));
    const last=flyer.frames.at(-1).transform;
    assert.match(last,/translate\(/);assert.doesNotMatch(last,/translate\(0px,0px\)/);
    h.feedback.clear();
});
test('an attacker that wins still spends its shield: 「聖盾交戰消耗」 shatters once with no shield pop', async t=>{
    resetFastForward();
    const h=harness(t);
    const before={player:{field:[{card:{instanceId:'a',name:'m'},slot:0,status:['聖盾指示物','直立'],pp:1000}]},computer:{field:[{card:{instanceId:'d',name:'n'},slot:0,status:['直立'],pp:500}]}};
    const after={player:{field:[{card:{instanceId:'a',name:'m'},slot:0,status:['橫置'],pp:1000}]},computer:{field:[]}};
    await h.settle(h.feedback.present(before,after,[{type:'attack',side:'player',instanceId:'a',targetId:'d',order:0},{type:'status',side:'player',instanceId:'a',label:'聖盾交戰消耗',order:1},{type:'death',side:'computer',instanceId:'d',label:'交戰',order:2}]));
    assert.equal(h.record.filter(r=>r.el.dataset.wcgEffect==='icon').length,0,'no gained-shield pop');
    assert.equal(h.record.filter(r=>r.el.dataset.wcgEffect==='shield-break').length,1,'shatters once');
    h.feedback.clear();
});
test('a shield spent in combat never pops back: 「聖盾抵擋消滅」 shatters it once and shows no shield icon', async t=>{
    resetFastForward();
    const h=harness(t);
    const before={player:{field:[{card:{instanceId:'dead',name:'m'},slot:0,status:['聖盾指示物','直立'],pp:1000}]},computer:{field:[]}};
    const after={player:{field:[{card:{instanceId:'dead',name:'m'},slot:0,status:['直立'],pp:1000}]},computer:{field:[]}};
    await h.settle(h.feedback.present(before,after,[{type:'attack',side:'computer',instanceId:'dead',order:0},{type:'status',side:'player',instanceId:'dead',label:'聖盾抵擋消滅',order:1}]));
    assert.equal(h.record.filter(r=>r.el.dataset.wcgEffect==='icon').length,0,'no gained-shield pop');
    assert.equal(h.record.filter(r=>r.el.dataset.wcgEffect==='shield-break').length,1,'shatters once');
    h.feedback.clear();h.record.length=0;
    await h.settle(h.feedback.present(before,after,[{type:'status',side:'player',instanceId:'dead',label:'聖盾抵擋消滅',order:0}]));
    assert.equal(h.record.filter(r=>r.el.dataset.wcgEffect==='icon').length,0);
    assert.equal(h.record.filter(r=>r.el.dataset.wcgEffect==='shield-break').length,1,'status alone still shows the break');
    h.feedback.clear();
});
