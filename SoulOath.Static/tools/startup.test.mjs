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
test('loading screen follows the saved English choice, and stays zh-Hant without one',async()=>{
    for(const [storage,languages,expected] of [[{getItem:()=> 'en'},['zh-TW'],'Downloading all game assets…'],[{getItem:()=>null},['en-US'],'Downloading all game assets…'],[{getItem:()=>null},['ja','en'],'正在下載完整遊戲資源…'],[undefined,undefined,'正在下載完整遊戲資源…']]){
        let report;
        const status={textContent:'正在檢查遊戲資源…'},detail={textContent:''},h1={textContent:'魂誓'};
        const progress={value:0,isConnected:true,attributes:{},setAttribute(k,v){this.attributes[k]=v;},closest(){return null;}};
        const elements={'startup-progress':progress,'startup-percentage':{},'startup-status':status,'startup-detail':detail};
        const documentElement={lang:'zh-Hant'};
        const context={window:{},URL,localStorage:storage,navigator:languages?{languages}:undefined,document:{baseURI:'https://game.test/',documentElement,title:'魂誓',querySelector:()=>h1,getElementById:id=>elements[id]},loadModule:()=>Promise.resolve({prepareGame(callback){report=callback;return new Promise(()=>{});}})};
        vm.createContext(context);vm.runInContext(source.replace("import(new URL('asset-preload.js', document.baseURI).href)",'loadModule()'),context);
        await Promise.resolve();await Promise.resolve();report({bytes:1,total:10,completed:0,count:3});
        assert.equal(status.textContent,expected);
        assert.equal(documentElement.lang,expected.startsWith('Down')?'en':'zh-Hant');
    }
});
