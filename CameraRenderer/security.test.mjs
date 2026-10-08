import assert from 'node:assert/strict';
import test from 'node:test';
import { allowedBrowserRequest, buildEffectCatalogue, cameraEffectAsset, containedPath, JOB_DEADLINE_MS, MAX_ACTIVE, MAX_BODY, MAX_QUEUE, mediaPath, nitroAssetRelative, pageConfiguration, QUEUE_WAIT_MS, renderedPngSize, SMALL_PNG_SIZE, validateJob, validatePreparation } from './security.mjs';

const configured = [
    { name: 'dark_sepia', colorMatrix: [1, 0, 0, 0, 0], minLevel: 0, enabled: true },
    { name: 'frame_gold', minLevel: 2, enabled: true },
    { name: 'shadow_multiply_02', colorMatrix: [], minLevel: 0, blendMode: 2, enabled: true },
    { name: 'frame_gold.png', minLevel: 0, enabled: true },
    { name: 'Habbo-Stories/frame_gold', minLevel: 0, enabled: true },
    { name: 'shelves_norja.nitro', minLevel: 0, enabled: true },
    { name: '../badge', minLevel: 0, enabled: true }
];

const catalogue = buildEffectCatalogue(configured);

test('trusted bundle routes support HAB and Nitro without allowing arbitrary paths', () => {
    for (const extension of ['hab', 'nitro']) {
        const path = `/assets/furniture/hc26_3.${extension}`;
        assert.equal(nitroAssetRelative(path), `furniture/nitro/hc26_3.${extension}`);
        assert.equal(allowedBrowserRequest('GET', `http://camera.local${path}`, 'http://camera.local', catalogue), true);
    }
    for (const path of ['/assets/furniture/../secret.hab', '/assets/furniture/a.js', '/assets/furniture/a.hab/extra']) assert.equal(nitroAssetRelative(path), null);
});

const viewport = (cropWidth, cropHeight) => ({
    width: 1280, height: 900, offsetX: 0, offsetY: 0, x: 10, y: 20,
    cropWidth, cropHeight, scale: 1, locationX: 1, locationY: 2, locationZ: 0
});

const job = (cropWidth, cropHeight, effects = []) => ({
    scene: { roomId: 1, heightmap: 'x', items: [], users: [] },
    viewport: viewport(cropWidth, cropHeight),
    effects,
    zoom: false,
    level: 6
});

test('catalogue exposes name, minLevel, and type and drops asset filenames', () => {
    assert.deepEqual(catalogue, [
        { name: 'dark_sepia', minLevel: 0, type: 'colormatrix' },
        { name: 'frame_gold', minLevel: 2, type: 'frame' },
        { name: 'shadow_multiply_02', minLevel: 0, type: 'composite' }
    ]);
});

test('thumbnail output stays 110 and zoom true still returns a 320 photo', () => {
    assert.equal(SMALL_PNG_SIZE, 110);
    assert.equal(renderedPngSize(320, 320), 320);
    assert.equal(renderedPngSize(110, 110), 110);
    const zoomed = validateJob({ ...job(320, 320), zoom: true }, catalogue);
    assert.equal(zoomed.zoom, true);
    assert.equal(zoomed.viewport.cropWidth, 320);
    assert.equal(validateJob(job(110, 110), catalogue).viewport.cropWidth, 110);
    assert.equal(validateJob({ ...job(110, 110), zoom: false }, catalogue).zoom, false);
    assert.throws(() => validateJob({ ...job(110, 110), zoom: true }, catalogue), /Invalid zoom/);
    for (const zoom of [0, 1, 2, 1.5, '2', null]) assert.throws(() => validateJob({ ...job(320, 320), zoom }, catalogue), /Invalid zoom/);
    assert.throws(() => renderedPngSize(110, 320), /Invalid viewport crop/);
});

test('main PNG matches the 320 or 110 crop and the wall preview is 110', () => {
    assert.equal(SMALL_PNG_SIZE, 110);
    assert.equal(renderedPngSize(320, 320), 320);
    assert.equal(renderedPngSize(110, 110), 110);
    assert.equal(validateJob(job(320, 320), catalogue).viewport.cropWidth, 320);
    assert.equal(validateJob(job(110, 110), catalogue).viewport.cropHeight, 110);
    assert.throws(() => validateJob(job(320, 110), catalogue), /Invalid viewport crop/);
    assert.throws(() => validateJob(job(110, 320), catalogue), /Invalid viewport crop/);
    assert.throws(() => validateJob(job(100, 100), catalogue), /Invalid viewport crop/);
    assert.throws(() => renderedPngSize(2048, 2048), /Invalid viewport crop/);
});

test('effect selection uses the catalogue name, not a trusted asset filename', () => {
    assert.equal(validateJob(job(320, 320, [{ name: 'dark_sepia', strength: 0.5 }]), catalogue).effects[0].name, 'dark_sepia');
    assert.throws(() => validateJob(job(320, 320, [{ name: 'frame_gold.png', strength: 1 }]), catalogue), /Invalid effect selection/);
    assert.throws(() => validateJob(job(320, 320, [{ name: 'shelves_norja', strength: 1 }]), catalogue), /Effect is unavailable/);
    assert.throws(() => validateJob(job(320, 320, [{ name: 'hh_human_body.nitro', strength: 1 }]), catalogue), /Invalid effect selection/);
    assert.throws(() => validateJob(job(110, 110, [{ name: 'frame_gold.png', strength: 1 }]), [{ name: 'frame_gold.png', minLevel: 0, type: 'composite' }]), /Invalid effect selection/);
    assert.equal(cameraEffectAsset('/c_images/Habbo-Stories/frame_gold.png', catalogue), 'Habbo-Stories/frame_gold.png');
    assert.equal(cameraEffectAsset('/c_images/Habbo-Stories/shadow_multiply_02.png', catalogue), 'Habbo-Stories/shadow_multiply_02.png');
    assert.throws(() => cameraEffectAsset('/c_images/Habbo-Stories/dark_sepia.png', catalogue), /not a catalogue effect/);
    assert.throws(() => cameraEffectAsset('/c_images/Habbo-Stories/shelves_norja.png', catalogue), /not a catalogue effect/);
    assert.throws(() => cameraEffectAsset('/c_images/Habbo-Stories/frame_gold.png.png', catalogue), /not a catalogue effect/);
    assert.throws(() => cameraEffectAsset('/c_images/Habbo-Stories/../../badge.png', catalogue), /not a catalogue effect/);
});

const origin = 'http://127.0.0.1:3921';
const minted = '0123456789abcdef0123456789abcdef';
const mintedGuid = '01234567-89ab-cdef-0123-456789abcdef';

test('browser requests stay on minted media and the local asset allowlist', () => {
    const allow = (href, method = 'GET') => allowedBrowserRequest(method, href, origin, catalogue);
    assert.equal(allow(`${origin}/page/index.html`), true);
    assert.equal(allow(`${origin}/page/assets/index-abc.js`), true);
    assert.equal(allow(`${origin}/gamedata/FurnitureData.json`), true);
    assert.equal(allow(`${origin}/gamedata/FigureMap.json`), true);
    assert.equal(allow(`${origin}/assets/furniture/shelves_norja.nitro`), true);
    assert.equal(allow(`${origin}/assets/figure/hh_human_body.nitro`), true);
    assert.equal(allow(`${origin}/assets/effect/dance.nitro`), true);
    assert.equal(allow(`${origin}/assets/pet/dog.nitro`), true);
    assert.equal(allow(`${origin}/c_images/Habbo-Stories/frame_gold.png`), true);
    assert.equal(allow(`${origin}/c_images/Habbo-Stories/shadow_multiply_02.png`), true);
    assert.equal(allow(`${origin}/camera/${minted}.png`), true);
    assert.equal(allow(`${origin}/camera/${minted}_small.png`), true);
    assert.equal(allow(`${origin}/camera/${mintedGuid}.png`), true);
    assert.equal(allow(`${origin}/camera/${mintedGuid}_small.png`), true);

    assert.equal(allow(`${origin}/page/index.html`, 'POST'), false);
    assert.equal(allow('https://evil.example/page/index.html'), false);
    assert.equal(allow('http://127.0.0.1:5078/page/index.html'), false);
    assert.equal(allow(`${origin}/assets/generic/room.nitro`), false);
    assert.equal(allow(`${origin}/assets/furniture/../room.nitro`), false);
    assert.equal(allow(`${origin}/page/%2e%2e/server.mjs`), false);
    assert.equal(allow(`${origin}/gamedata/Secret.json`), false);
    assert.equal(allow(`${origin}/c_images/Habbo-Stories/dark_sepia.png`), false);
    assert.equal(allow(`${origin}/c_images/Habbo-Stories/frame_gold.png.png`), false);
    assert.equal(allow(`${origin}/c_images/catalogue/frame_gold.png`), false);
    assert.equal(allow(`${origin}/camera/frame_gold.png`), false);
    assert.equal(allow(`${origin}/camera/${minted}.jpg`), false);
    assert.equal(allow(`${origin}/camera/${minted.toUpperCase()}.png`), false);
    assert.equal(allow(`${origin}/camera/${minted}.png/extra`), false);
    assert.equal(mediaPath.test(`/camera/${minted}.png`), true);
    assert.equal(mediaPath.test(`/camera/${mintedGuid}_small.png`), true);
    assert.equal(mediaPath.test('/camera/../badge.png'), false);
    assert.equal(mediaPath.test(`/camera/${minted}_small.png.png`), false);
});

test('scene strings cannot carry a remote image URL', () => {
    const remote = (value) => validateJob({ ...job(320, 320), scene: { ...job(320, 320).scene, floor: value } }, catalogue);
    assert.throws(() => remote('http://evil.example/a.png'), /URL in scene/);
    assert.throws(() => remote('https://evil.example/a.png'), /URL in scene/);
    assert.throws(() => remote('data:image/png;base64,aaaa'), /URL in scene/);
    assert.throws(() => remote('blob:http://127.0.0.1/uuid'), /URL in scene/);
    assert.throws(() => remote('javascript:alert(1)'), /URL in scene/);
    assert.throws(() => remote('file:///etc/passwd'), /URL in scene/);
    assert.throws(() => remote('//evil.example/a.png'), /URL in scene/);
    const linked = job(320, 320);
    linked.scene = { ...linked.scene, items: [{ extraData: 'https://evil.example/photo.png' }] };
    assert.throws(() => validateJob(linked, catalogue), /URL in scene/);
    const mintedScene = job(320, 320);
    mintedScene.scene = { ...mintedScene.scene, items: [{ extraData: `/camera/${minted}.png` }] };
    assert.equal(validateJob(mintedScene, catalogue).scene.items[0].extraData, `/camera/${minted}.png`);
});

test('an empty effect catalogue still accepts a capture with no effects', () => {
    assert.deepEqual(buildEffectCatalogue(undefined), []);
    assert.deepEqual(buildEffectCatalogue([]), []);
    const thumbnail = validateJob(job(110, 110, []), []);
    const photo = validateJob(job(320, 320, []), []);
    assert.deepEqual(thumbnail.effects, []);
    assert.equal(renderedPngSize(photo.viewport.cropWidth, photo.viewport.cropHeight), 320);
    assert.equal(renderedPngSize(thumbnail.viewport.cropWidth, thumbnail.viewport.cropHeight), 110);
    assert.throws(() => validateJob(job(320, 320, [{ name: 'dark_sepia', strength: 1 }]), []), /Effect is unavailable/);
});

test('a render job is capped at 20 seconds and nitro paths stay inside the asset root', () => {
    assert.equal(JOB_DEADLINE_MS, 20000);
    assert.ok(JOB_DEADLINE_MS < 30000);
    assert.equal(MAX_BODY, 1024 * 1024);
    assert.equal(MAX_ACTIVE, 2);
    assert.equal(MAX_QUEUE, 4);
    assert.equal(QUEUE_WAIT_MS, 5000);
    assert.equal(nitroAssetRelative('/assets/furniture/drinks.nitro'), 'furniture/nitro/drinks.nitro');
    assert.equal(nitroAssetRelative('/assets/figure/hh_human_body.nitro'), 'clothes/nitro/hh_human_body.nitro');
    assert.equal(nitroAssetRelative('/assets/effect/Dance1.nitro'), 'effects/nitro/Dance1.nitro');
    assert.equal(nitroAssetRelative('/assets/pet/dog.nitro'), 'pets/dog.nitro');
    assert.equal(nitroAssetRelative('/assets/generic/room.nitro'), null);
    assert.equal(nitroAssetRelative('/assets/furniture/../../etc/passwd.nitro'), null);
    assert.equal(nitroAssetRelative('/assets/furniture/....nitro'), null);
    assert.equal(allowedBrowserRequest('GET', 'http://127.0.0.1:3921/assets/furniture/....nitro', 'http://127.0.0.1:3921', catalogue), false);
    assert.equal(containedPath('/roots/assets', '/roots/assets/furniture/nitro/drinks.nitro'), true);
    assert.equal(containedPath('/roots/assets', '/roots/assets-evil/drinks.nitro'), false);
    assert.equal(containedPath('/roots/assets', '/etc/passwd'), false);
    assert.equal(containedPath('/roots/assets', '/roots/assets'), false);
    const secret = 'camera-renderer-secret-value-0123456789';
    const exposed = pageConfiguration({ 'socket.url': '', Bearer: 'hidden', note: `token ${secret}`, 'camera.available.effects': [] }, secret);
    assert.equal(exposed.Bearer, undefined);
    assert.equal(exposed.note, undefined);
    assert.equal(exposed['socket.url'], '');
    assert.deepEqual(exposed['camera.available.effects'], []);
});

test('gesture 0 is no gesture and a job without level is rejected', () => {
    const sample = job(320, 320);
    sample.scene.users = [{ gesture: '0', type: 1, figure: 'hd-180-1' }];
    assert.equal(validateJob(sample, catalogue).scene.users[0].gesture, '');
    const posted = job(320, 320);
    delete posted.level;
    assert.throws(() => validateJob(posted, catalogue), /Invalid effects/);
});

test('a frame keeps its name and drops strength, and an unknown or locked effect fails closed', () => {
    const named = validateJob(job(320, 320, [{ name: 'frame_gold' }]), catalogue);
    assert.equal(named.effects[0].name, 'frame_gold');
    assert.equal(named.effects[0].strength, 1);
    const ignored = validateJob(job(320, 320, [{ name: 'frame_gold', strength: 0 }]), catalogue);
    assert.equal(ignored.effects[0].strength, 1);
    assert.throws(() => validateJob(job(320, 320, [{ name: 'missing_effect', strength: 1 }]), catalogue), /Effect is unavailable/);
    assert.throws(() => validateJob(job(320, 320, [{ name: 'frame_gold', strength: 1 }]), catalogue.map(entry => entry.name === 'frame_gold' ? { ...entry, minLevel: 9 } : entry)), /Effect is unavailable/);
    assert.throws(() => validateJob(job(320, 320, [{ name: 'dark_sepia' }]), catalogue), /Invalid effect selection/);
});

test('a preparation carries a validated scene and nothing else', () => {
    const scene = () => ({ ...job(320, 320).scene, users: [{ gesture: '0' }] });
    assert.equal(validatePreparation({ scene: scene() }).scene.users[0].gesture, '');
    assert.throws(() => validatePreparation({ scene: scene(), viewport: viewport(320, 320) }), /Invalid preparation/);
    assert.throws(() => validatePreparation(null), /Invalid preparation/);
    assert.throws(() => validatePreparation({}), /Invalid server scene/);
    assert.throws(() => validatePreparation({ scene: { ...scene(), roomId: 0 } }), /Invalid server scene/);
    assert.throws(() => validatePreparation({ scene: { ...scene(), items: Array(5001).fill({}) } }), /Invalid server scene/);
    assert.throws(() => validatePreparation({ scene: { ...scene(), wallpaper: 'https://evil.example/a.png' } }), /URL in scene/);
});
