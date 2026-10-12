import test from 'node:test';
import assert from 'node:assert/strict';
import { freeCastReady, releaseZone } from '../../WcgWeb/wwwroot/battle/drag-intent.js';

const point = { x: 500, y: 610, startY: 740, handTop: 700, board: { x: 0, y: 0, width: 1000, height: 800 } };
test('untargeted spells release outside the hand without needing a board slot or target', () => {
    assert.equal(releaseZone('free', point, null), 'cast');
    assert.equal(releaseZone('free', point, 'own'), 'cast');
    assert.equal(releaseZone('free', point, 'enemy'), 'cast');
    assert.equal(releaseZone('free', point, 'energy'), 'energy');
});
test('short pulls, returning to the hand, portraits, controls and off-board release cancel', () => {
    for (const change of [{ startY: 640 }, { y: 705 }, { blocked: true }, { x: -1 }, { y: -1 }]) {
        assert.equal(releaseZone('free', { ...point, ...change }, 'center'), null);
    }
});
test('portrait targeting and placement keep their exact destination; they never become a free cast', () => {
    for (const mode of ['target', 'place']) {
        assert.equal(releaseZone(mode, point, 'own'), 'own');
        assert.equal(releaseZone(mode, point, 'face'), 'face');
        assert.equal(releaseZone(mode, point, null), null);
    }
});
test('phone gesture has a shorter but deliberate lift threshold', () => {
    const phone = { ...point, x: 200, y: 660, startY: 720, handTop: 700, board: { x: 0, y: 0, width: 390, height: 600 } };
    assert.equal(freeCastReady(phone), true);
    assert.equal(freeCastReady({ ...phone, startY: 690 }), false);
});
