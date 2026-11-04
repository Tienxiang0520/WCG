import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { addAnalytics, analyticsTag } from './analytics-tag.mjs';
const read=path=>readFileSync(new URL(path,import.meta.url),'utf8');
const script=read('../wwwroot/analytics.js');
test('analytics tag is added once before </head>',()=>{
    const html=addAnalytics('<html><head><title>x</title></head><body></body></html>');
    assert.ok(html.indexOf(analyticsTag)<html.indexOf('</head>'));
    assert.equal(addAnalytics(html),html);
});
test('source index.html does not load analytics (dev builds stay silent)',()=>{
    assert.ok(!read('../wwwroot/index.html').includes('analytics.js'));
});
test('both web projects share the same analytics script',()=>{
    assert.equal(read('../../WcgWeb/wwwroot/analytics.js'),script);
});
test('analytics only runs on the production host and only tracks page views',()=>{
    assert.match(script,/location\.hostname !== config\.host\) return/);
    assert.match(script,/host: 'wcggametienxiang0520\.z11\.web\.core\.windows\.net'/);
    assert.match(script,/disableAjaxTracking: true/);
    assert.match(script,/disableFetchTracking: true/);
    assert.doesNotMatch(script,/trackEvent|trackTrace|trackMetric|localStorage/);
});
function run(hostname){
    const appended=[];
    const sandbox={location:{hostname},window:{},document:{createElement:()=>({}),head:{appendChild:node=>appended.push(node)}}};
    new Function('location','window','document',script)(sandbox.location,sandbox.window,sandbox.document);
    return appended;
}
test('localhost does not load the SDK',()=>{assert.equal(run('localhost').length,0);});
test('production host loads the SDK and survives a blocked script',()=>{
    const [node]=run('wcggametienxiang0520.z11.web.core.windows.net');
    assert.equal(node.src,'https://js.monitor.azure.com/scripts/b/ai.3.gbl.min.js');
    assert.doesNotThrow(()=>node.onerror());
    assert.doesNotThrow(()=>node.onload());
});
