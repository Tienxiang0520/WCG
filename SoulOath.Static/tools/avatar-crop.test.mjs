import test from 'node:test';
import assert from 'node:assert/strict';
import { sniff, checkFile, checkSize, clampView, sourceRect, zoomAt, MAX_FILE_BYTES } from '../../WcgWeb/wwwroot/battle/avatar-crop.js';
const bytes = (...parts) => new Uint8Array(parts.flatMap(p => typeof p === 'string' ? [...p].map(c => c.charCodeAt(0)) : p));
const png = bytes([0x89], 'PNG', [0x0d, 0x0a, 0x1a, 0x0a], [0, 0, 0, 13]);
const jpeg = bytes([0xff, 0xd8, 0xff, 0xe0], 'JFIF');
const gif = bytes('GIF89a', [1, 0, 1, 0]);
const webp = bytes('RIFF', [4, 0, 0, 0], 'WEBPVP8 ');
test('image formats are recognised from their bytes, not their names', () => {
    assert.equal(sniff(png), 'image/png'); assert.equal(sniff(jpeg), 'image/jpeg');
    assert.equal(sniff(gif), 'image/gif'); assert.equal(sniff(webp), 'image/webp');
    assert.equal(sniff(bytes('<svg xmlns="http://www.w3.org/2000/svg">')), null);
    assert.equal(sniff(bytes('%PDF-1.7')), null); assert.equal(sniff(new Uint8Array()), null);
});
test('uploads are checked for size, type and mismatched extensions', () => {
    assert.equal(checkFile({ size: 1000, type: 'image/png' }, png), null);
    assert.equal(checkFile({ size: 1000, type: 'image/jpg' }, jpeg), null);
    assert.equal(checkFile({ size: 1000, type: '' }, gif), null);
    assert.match(checkFile(null, png), /沒有選擇/);
    assert.match(checkFile({ size: 0, type: 'image/png' }, png), /空的/);
    assert.match(checkFile({ size: MAX_FILE_BYTES + 1, type: 'image/png' }, png), /8 MB/);
    assert.match(checkFile({ size: 1000, type: 'image/svg+xml' }, bytes('<svg>')), /只支援/);
    assert.match(checkFile({ size: 1000, type: 'image/png' }, jpeg), /不符/);
    assert.equal(checkSize(640, 480), null); assert.match(checkSize(8, 8), /太小/); assert.match(checkSize(9000, 100), /太大/);
    assert.match(checkSize(NaN, 100), /太小/);
});
test('the crop always covers the circle and maps to a square source area', () => {
    const box = 200;
    // Wide picture at zoom 1: the full height is used and it can only slide sideways.
    assert.deepEqual(clampView({ zoom: 1, x: 999, y: 50 }, 400, 200, box), { zoom: 1, x: 100, y: 0 });
    assert.deepEqual(sourceRect({ zoom: 1, x: 0, y: 0 }, 400, 200, box), { sx: 100, sy: 0, sw: 200, sh: 200 });
    assert.deepEqual(sourceRect({ zoom: 1, x: 100, y: 0 }, 400, 200, box), { sx: 0, sy: 0, sw: 200, sh: 200 });
    // Zoom is bounded and a 2x zoom shows half of the short side.
    assert.equal(clampView({ zoom: 10, x: 0, y: 0 }, 400, 200, box).zoom, 4);
    assert.equal(clampView({ zoom: .2, x: 0, y: 0 }, 400, 200, box).zoom, 1);
    assert.deepEqual(sourceRect({ zoom: 2, x: 0, y: 0 }, 300, 300, box), { sx: 75, sy: 75, sw: 150, sh: 150 });
    for (const view of [{ zoom: 3, x: -500, y: 500 }, { zoom: 1.5, x: 40, y: -20 }, { zoom: NaN, x: NaN, y: 1 }]) {
        const r = sourceRect(view, 640, 480, box);
        assert.ok(r.sx >= -1e-9 && r.sy >= -1e-9 && r.sx + r.sw <= 640 + 1e-9 && r.sy + r.sh <= 480 + 1e-9, JSON.stringify(r));
    }
});
test('pinch/wheel zoom keeps the point under the fingers in place', () => {
    const box = 200, w = 300, h = 300;
    const before = sourceRect({ zoom: 1.5, x: 0, y: 0 }, w, h, box);
    const view = zoomAt({ zoom: 1.5, x: 0, y: 0 }, 2.5, 150, 120, w, h, box), after = sourceRect(view, w, h, box);
    const at = (r, px, py) => [r.sx + px / box * r.sw, r.sy + py / box * r.sh];
    at(before, 150, 120).forEach((value, i) => assert.ok(Math.abs(value - at(after, 150, 120)[i]) < 1e-9));
    assert.equal(Math.abs(zoomAt({ zoom: 2, x: 30, y: 30 }, 1, 0, 0, w, h, box).x), 0);
});
