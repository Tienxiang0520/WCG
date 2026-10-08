import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFile } from 'node:fs/promises';
const source=await readFile(new URL('../wwwroot/startup.js',import.meta.url),'utf8');
function setup(){
    let report,finish;
    const progress={value:0,isConnected:true,attributes:{},setAttribute(k,v){this.attributes[k]=v;},closest(){return{classList:{add:value=>{this.failed=value;}}};}};
    const elements={'startup-progress':progress,'startup-percentage':{},'startup-status':{},'startup-detail':{}};
    const download=new Promise(resolve=>finish=resolve);
    const context={window:{},URL,document:{baseURI:'https://game.test/',getElementById:id=>elements[id]},loadModule:()=>Promise.resolve({prepareGame(callback){report=callback;return download;}})};
    vm.createContext(context);vm.runInContext(source.replace("import(new URL('asset-preload.js', document.baseURI).href)",'loadModule()'),context);
    return{context,elements,async report(state){await Promise.resolve();report(state);},finish};
}
test('full asset progress includes bytes and files; 100 is reserved for verified completion',async()=>{
    const state=setup();await state.report({bytes:43,total:100,completed:2,count:10});
    assert.equal(state.elements['startup-progress'].value,43);
    await state.report({bytes:100,total:100,completed:9,count:10});assert.equal(state.elements['startup-progress'].value,99);
    await state.report({bytes:100,total:100,completed:10,count:10});assert.equal(state.elements['startup-percentage'].textContent,'100%');
    state.finish();await state.context.window.wcgStartup.ready;
    assert.equal(state.elements['startup-status'].textContent,'下載完成，正在啟動遊戲…');
});
test('failure preserves the progress and explains retry; subsequent events cannot hide it',async()=>{
    const state=setup();await state.report({bytes:26,total:100,completed:2,count:10});
    state.context.window.wcgStartup.fail(new Error('請重新整理接續下載。'));
    await state.report({bytes:100,total:100,completed:10,count:10});
    assert.equal(state.elements['startup-progress'].value,26);
    assert.equal(state.elements['startup-progress'].attributes['aria-valuetext'],'載入失敗');
    assert.match(state.elements['startup-status'].textContent,/接續下載/);
    state.finish();await state.context.window.wcgStartup.ready;
    assert.match(state.elements['startup-status'].textContent,/接續下載/);
});
