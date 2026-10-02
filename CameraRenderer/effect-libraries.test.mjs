import assert from 'node:assert/strict';
import test from 'node:test';
import { avatarEffectsReady, buildAvatarEffectLibraries } from './effect-libraries.mjs';

test('nonzero effects require a nonempty mapping; no-effect avatars need none', () => {
    const libraries = buildAvatarEffectLibraries([{ id: 1, lib: '' }]);
    assert.equal(avatarEffectsReady([{ effect: 0 }], libraries, () => true), true);
    assert.throws(() => avatarEffectsReady([{ effect: 1 }], libraries, () => true), /Missing avatar effect mapping 1/);
    assert.throws(() => avatarEffectsReady([{ effect: 99 }], libraries, () => true), /Missing avatar effect mapping 99/);
});

test('every library for repeated IDs and shared-library IDs must be ready', () => {
    const libraries = buildAvatarEffectLibraries([
        { id: 1, lib: 'shared' }, { id: 2, lib: 'shared' },
        { id: 1, lib: 'second' }, { id: 1, lib: 'shared' }
    ]);
    assert.deepEqual(libraries.get('1'), ['shared', 'second']);
    assert.deepEqual(libraries.get('2'), ['shared']);
    assert.equal(avatarEffectsReady([{ effect: 1 }, { effect: 2 }], libraries, lib => lib === 'shared'), false);
    assert.equal(avatarEffectsReady([{ effect: 2 }], libraries, () => false), false);
    assert.equal(avatarEffectsReady([{ effect: 1 }, { effect: 2 }], libraries, () => true), true);
});
