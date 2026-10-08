import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFile } from 'node:fs/promises';
const source=await readFile(new URL('../wwwroot/startup.js',import.meta.url),'utf8');
function setup(initial=''){
    let nativeProgress=initial;
    const observers=[];
    const progress={value:0,isConnected:true,attributes:{},setAttribute(k,v){this.attributes[k]=v;},closest(){return{classList:{add:value=>{this.failed=value;}}};}};
    const elements={'app':{},'startup-progress':progress,'startup-percentage':{},'startup-status':{}};
    const context={window:{},document:{getElementById:id=>elements[id],documentElement:{style:{getPropertyValue:()=>nativeProgress}}},MutationObserver:class{
        constructor(callback){this.callback=callback;observers.push(this);}
        observe(){}
        disconnect(){this.disconnected=true;}
    }};
    vm.createContext(context);vm.runInContext(source,context);
    return{context,elements,observers,report(value){nativeProgress=value;observers[0].callback();}};
}
test('download progress follows completed files and switches to preparation at completion',()=>{
    const state=setup();
    assert.equal(state.elements['startup-percentage'].textContent,'0%');
    state.report('43.7%');
    assert.equal(state.elements['startup-progress'].value,43);
    assert.equal(state.elements['startup-percentage'].textContent,'43%');
    state.report('100%');
    assert.equal(state.elements['startup-status'].textContent,'下載完成，正在準備卡牌與對戰…');
});
test('cached startup reaches preparation immediately without delaying the game',()=>{
    const state=setup('100%');
    assert.equal(state.elements['startup-progress'].value,100);
    state.elements['startup-progress'].isConnected=false;
    state.observers[1].callback();
    assert.ok(state.observers.every(observer=>observer.disconnected));
    state.report('10%');
    assert.equal(state.elements['startup-progress'].value,100);
});
test('failure stops updates and leaves a readable error with the completed percentage',()=>{
    const state=setup('26%');
    state.context.window.wcgStartup.fail();state.report('100%');
    assert.equal(state.elements['startup-progress'].value,26);
    assert.equal(state.elements['startup-progress'].attributes['aria-valuetext'],'載入失敗');
    assert.match(state.elements['startup-status'].textContent,/重新整理/);
    assert.ok(state.observers.every(observer=>observer.disconnected));
});
test('missing or invalid framework percentages keep a valid progress value',()=>{
    const state=setup('NaN');
    assert.equal(state.elements['startup-progress'].value,0);
    state.report('-5%');assert.equal(state.elements['startup-progress'].value,0);
    state.report('101%');assert.equal(state.elements['startup-progress'].value,100);
});
