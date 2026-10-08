import http from 'node:http';
import { readFile, realpath } from 'node:fs/promises';
import { resolve, extname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import stripJsonComments from 'strip-json-comments';
import { createAssetCache, freshness } from './asset-cache.mjs';
import { createFurnitureDataSource, fetchCameraData } from './furniture-data.mjs';
import { allowedBrowserRequest, authorized, buildEffectCatalogue, cameraEffectAsset, containedPath, JOB_DEADLINE_MS, MAX_ACTIVE, MAX_BODY, MAX_QUEUE, mediaPath, nitroAssetRelative, pageConfiguration, PREPARE_DEADLINE_MS, QUEUE_WAIT_MS, SMALL_PNG_SIZE, validPng, validateJob, validatePreparation } from './security.mjs';

const directory = fileURLToPath(new URL('.', import.meta.url));
const secret = process.env.CAMERA_RENDERER_SECRET;
if (!secret || secret.length < 32) throw new Error('CAMERA_RENDERER_SECRET must contain at least 32 characters');
const host = process.env.CAMERA_RENDERER_HOST ?? '127.0.0.1';
const port = Number(process.env.CAMERA_RENDERER_PORT ?? 3921);
const origin = process.env.CAMERA_RENDERER_ORIGIN ?? `http://${host}:${port}`;
const rooted = (env, fallback) => env ? resolve(env) : resolve(directory, fallback);
const assets = rooted(process.env.CAMERA_ASSET_ROOT, '../../nitro-assets');
const images = rooted(process.env.CAMERA_IMAGES_ROOT, '../../../retro-hotel-files/flash/c_images');
const media = rooted(process.env.CAMERA_MEDIA_ROOT, '../camera');
const assetOrigin = process.env.CAMERA_ASSET_ORIGIN ? new URL(process.env.CAMERA_ASSET_ORIGIN) : null;
if (assetOrigin && (assetOrigin.protocol !== 'https:' || assetOrigin.username || assetOrigin.password || assetOrigin.pathname !== '/' || assetOrigin.search || assetOrigin.hash)) throw new Error('CAMERA_ASSET_ORIGIN must be an HTTPS origin');
const furnitureData = createFurnitureDataSource({ url: process.env.CAMERA_FURNIDATA_URL, filename: resolve(assets, 'furniture/json/FurnitureData.json') });
const originAsset = createAssetCache({
    load: async (path, signal) => {
        const response = await fetchCameraData(new URL(path, assetOrigin), {}, signal);
        return { body: response.body, freshMs: freshness(response.headers) };
    },
    maxBytes: 256 * 1024 * 1024,
    maxDownloads: 8
});
let catalogueVersion;
// A new catalogue can come with new revisions of the same libraries, so their copies go with the old catalogue.
async function currentCatalogue() {
    const furniture = await furnitureData();
    if (catalogueVersion !== furniture.version) {
        if (catalogueVersion) originAsset.clear();
        catalogueVersion = furniture.version;
    }
    return furniture;
}
const configuration = {};
for (const filename of [process.env.CAMERA_RENDERER_CONFIG, process.env.CAMERA_UI_CONFIG].filter(Boolean)) Object.assign(configuration, JSON.parse(stripJsonComments(await readFile(filename, 'utf8'))));
Object.assign(configuration, {
    'socket.url': '', 'image.library.url': `${origin}/c_images/`,
    'furnidata.url': `${origin}/gamedata/FurnitureData.json`,
    'productdata.url': `${origin}/gamedata/ProductData.json`,
    'avatar.actions.url': `${origin}/gamedata/HabboAvatarActions.json`,
    'avatar.figuredata.url': `${origin}/gamedata/FigureData.json`,
    'avatar.figuremap.url': `${origin}/gamedata/FigureMap.json`,
    'avatar.effectmap.url': `${origin}/gamedata/EffectMap.json`,
    'avatar.asset.url': `${origin}/assets/figure/%libname%.nitro`,
    'avatar.asset.effect.url': `${origin}/assets/effect/%libname%.nitro`,
    'furni.asset.url': `${origin}/assets/furniture/%libname%.nitro`,
    'pet.asset.url': `${origin}/assets/pet/%libname%.nitro`,
    'generic.asset.url': `${origin}/assets/generic/%libname%.nitro`,
    'room.color.skip.transition': true, 'system.log.packets': false
});
const catalogue = buildEffectCatalogue(configuration['camera.available.effects']);
const pageConfig = pageConfiguration(configuration, secret);
const gamedata = {
    'FurnitureData.json': 'furniture/json/FurnitureData.json', 'ProductData.json': 'furniture/json/ProductData.json',
    'FigureData.json': 'clothes/json/FigureData.json', 'FigureMap.json': 'clothes/json/FigureMap.json',
    'EffectMap.json': 'effects/json/EffectMap.json', 'HabboAvatarActions.json': 'gamedata/json/HabboAvatarActions.json'
};
const mime = {'.html':'text/html','.js':'text/javascript','.css':'text/css','.json':'application/json','.png':'image/png','.nitro':'application/octet-stream'};
let browser;
let shuttingDown = false;
let active = 0;
const queue = [];
const pool = [];
const POOL_SIZE = 2;
let warming = null;
let preparation = null;
let borrowing = 0;
let gate = Promise.resolve();
function withGate(task) {
    const run = gate.then(task);
    gate = run.then(() => {}, () => {});
    return run;
}
async function acquire(abort) {
    if (abort?.done) throw new Error('Camera render timed out');
    if (active < MAX_ACTIVE) { active++; abort.acquired = true; return; }
    if (queue.length >= MAX_QUEUE) throw new Error('Camera render queue is full');
    await new Promise((accept, reject) => {
        const entry = {accept, timer:setTimeout(() => {queue.splice(queue.indexOf(entry),1); reject(new Error('Camera queue timed out'));}, QUEUE_WAIT_MS)};
        abort.entry = entry;
        queue.push(entry);
        if (abort.done) { clearTimeout(entry.timer); queue.splice(queue.indexOf(entry),1); reject(new Error('Camera render timed out')); }
    });
    abort.acquired = true;
    if (abort.done) throw new Error('Camera render timed out');
}
function release() {
    const next = queue.shift();
    if (next) { clearTimeout(next.timer); next.accept(); }
    else active--;
}
async function safeFile(root, name) {
    if (typeof name !== 'string' || name.includes('\0') || name.includes('..')) throw new Error('Invalid asset path');
    const base = await realpath(root);
    const target = await realpath(resolve(base, name));
    if (!containedPath(base, target)) throw new Error('Invalid asset path');
    return readFile(target);
}
async function createPage(abort, furniture) {
    furniture ??= await currentCatalogue();
    // A background page and a photo may both start the first page; they share one browser.
    browser ??= chromium.launch({executablePath:process.env.CAMERA_CHROMIUM_PATH || undefined, headless:true, chromiumSandbox:true, args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']}).then(launched => {
        launched.once('disconnected', () => {
            if (shuttingDown) return;
            console.error('Trusted camera browser disconnected; restarting renderer');
            process.exit(1);
        });
        return launched;
    }, error => { browser = undefined; throw error; });
    if (abort?.done) throw new Error('Camera render timed out');
    const context = await (await browser).newContext({viewport:{width:2048,height:2048},deviceScaleFactor:1,extraHTTPHeaders:{Authorization:`Bearer ${secret}`}});
    if (abort) abort.context = context;
    if (abort?.done) throw new Error('Camera render timed out');
    const slot = { context, page: null, busy: true, preparing: null, roomId: 0, usedAt: 0, failures: [], furnitureVersion: furniture.version };
    try {
        await context.route('**/*', async route => {
            const request = route.request();
            const href = request.url();
            if (!allowedBrowserRequest(request.method(), href, origin, catalogue)) { slot.failures.push('Blocked asset request: '+href); console.error('Blocked camera asset: '+href); await route.abort(); return; }
            // Pin one catalogue to the page for its entire lifetime, including concurrent jobs.
            if (new URL(href).pathname === '/gamedata/FurnitureData.json') { await route.fulfill({ contentType: 'application/json', body: furniture.body }); return; }
            await route.continue();
        });
        await context.addInitScript(config => { window.cameraConfiguration = config; window.WebSocket = class {constructor(){throw new Error('Game sockets are disabled in trusted rendering');}}; }, pageConfig);
        const page = await context.newPage();
        slot.page = page;
        page.on('pageerror', error => console.error('Camera page error: '+error.message));
        page.on('console', message => { if(message.type()==='error') console.error('Camera page: '+message.text()); });
        // A missing wall photo stays an empty frame, as it does in the client.
        page.on('response', response => { const path = new URL(response.url()).pathname; if (response.status() >= 400 && !mediaPath.test(path)) { slot.failures.push('Missing trusted asset: '+path); console.error('Missing camera asset: '+path); } });
        page.on('requestfailed', request => slot.failures.push('Failed trusted asset: '+request.url()));
        await page.goto(`${origin}/page/index.html`, {waitUntil:'load',timeout:15000});
        await page.waitForFunction(() => window.cameraReady === true, {timeout:15000});
        if (slot.failures.length) throw new Error(slot.failures[0]);
        return slot;
    } catch (error) {
        await context.close().catch(() => {});
        throw error;
    }
}
function selectIdlePage(idle, roomId) {
    return idle.filter(entry => entry.roomId === roomId).sort((a, b) => b.usedAt - a.usedAt)[0] ?? idle.sort((a, b) => a.usedAt - b.usedAt)[0];
}
// A free page that last prepared or photographed the room already holds its libraries. A preparing page is never
// lent: its failures would belong to the photo. Waits for a preparation or a booting page happen outside the gate.
async function borrowPage(abort, roomId) {
    borrowing++;
    try {
        return await borrowIdlePage(abort, roomId);
    } finally {
        borrowing--;
    }
}
async function borrowIdlePage(abort, roomId) {
    for (;;) {
        const next = await withGate(async () => {
            if (abort?.done) throw new Error('Camera render timed out');
            const furniture = await currentCatalogue();
            if (abort?.done) throw new Error('Camera render timed out');
            for (const entry of [...pool]) if (!entry.busy && entry.furnitureVersion !== furniture.version) await discardPage(entry);
            const current = pool.filter(entry => !entry.busy && entry.furnitureVersion === furniture.version);
            const preparing = current.filter(entry => entry.preparing);
            const idle = current.filter(entry => !entry.preparing);
            // The page that last prepared or photographed this room, else the one used least recently.
            const slot = selectIdlePage(idle, roomId);
            if (slot) { slot.busy = true; slot.roomId = roomId; slot.usedAt = performance.now(); slot.failures.length = 0; return { slot }; }
            // A preparation may still be loading furniture since removed, so it is waited for only without a free page.
            const same = preparing.find(entry => entry.roomId === roomId);
            if (same) return { wait: same.preparing };
            if (preparing.length) return { wait: Promise.race(preparing.map(entry => entry.preparing)) };
            if (warming) return { wait: warming };
            if (pool.length >= POOL_SIZE) throw new Error('Camera page pool is exhausted');
            const created = await createPage(abort, furniture);
            if (abort?.done) throw new Error('Camera render timed out');
            created.roomId = roomId;
            created.usedAt = performance.now();
            pool.push(created);
            if (abort?.done) { pool.splice(pool.indexOf(created), 1); throw new Error('Camera render timed out'); }
            return { slot: created };
        });
        if (next.slot) return next.slot;
        await next.wait;
    }
}
async function discardPage(slot) {
    const index = pool.indexOf(slot);
    if (index >= 0) pool.splice(index, 1);
    await slot?.context.close().catch(() => {});
    refill();
}
// Pages boot in seconds, so replacements start as soon as one is missing rather than when a photo needs it.
function refill() {
    if (shuttingDown || warming || pool.length >= POOL_SIZE) return;
    warming = (async () => {
        const furniture = await currentCatalogue();
        const slot = await createPage(null, furniture);
        // A catalogue that changed while the page booted makes it stale before its first photo; the next refill replaces it.
        if (shuttingDown || pool.length >= POOL_SIZE || slot.furnitureVersion !== catalogueVersion) { await slot.context.close().catch(() => {}); return; }
        slot.busy = false;
        pool.push(slot);
        console.log('Trusted camera page ready');
    })().then(() => { warming = null; refill(); }, error => {
        warming = null;
        console.error('Trusted camera page failed: '+error.message);
        setTimeout(refill, 5000).unref();
    });
}
// A catalogue change replaces idle pages in the background, before the next photo has to.
async function revalidateCatalogue() {
    await withGate(async () => {
        const furniture = await currentCatalogue();
        for (const entry of [...pool]) if (!entry.busy && entry.furnitureVersion !== furniture.version) await discardPage(entry);
    }).catch(error => console.error('Camera catalogue check failed: '+error.message));
}
// Loads a room's libraries into an idle page while its photographer frames the shot. One at a time, only while no
// photo waits, and the photo itself still renders the room as it is at the shutter.
// A photo waiting for capacity or a page always comes first.
const photoWaiting = () => queue.length > 0 || borrowing > 0;
async function prepare(scene) {
    if (preparation || photoWaiting()) return false;
    preparation = (async () => {
        const started = await withGate(async () => {
            if (photoWaiting()) return null;
            const furniture = await currentCatalogue();
            const idle = pool.filter(entry => !entry.busy && !entry.preparing && entry.furnitureVersion === furniture.version);
            // Otherwise take the page used least recently, leaving the other warm for its room.
            const slot = selectIdlePage(idle, scene.roomId);
            if (!slot) return null;
            slot.roomId = scene.roomId;
            slot.usedAt = performance.now();
            slot.failures.length = 0;
            const loading = slot.page.evaluate(async body => {
                if (typeof window.cameraPrepare !== 'function') throw new Error('Camera preparation is unavailable');
                await window.cameraPrepare(JSON.parse(body));
            }, JSON.stringify({ scene }));
            // The page stays out of reach until the load really ends, even after this request gives up on it, and its
            // failures end with it. A page whose preparation failed keeps that library pending for good, so it goes.
            // slot.preparing never rejects.
            slot.preparing = loading.then(() => {}, error => {
                console.error('Camera preparation failed: '+error.message);
                return discardPage(slot);
            }).finally(() => {
                slot.preparing = null;
                slot.failures.length = 0;
            });
            return { slot, loading };
        });
        if (!started) return false;
        // The page gives up after its own budget; a preparation that outlives this deadline takes its page with it.
        let timer;
        const deadline = new Promise((_, reject) => { timer = setTimeout(() => {
            void discardPage(started.slot);
            reject(new Error('Camera preparation timed out'));
        }, PREPARE_DEADLINE_MS); });
        try {
            await Promise.race([started.loading, deadline]);
            return true;
        } finally {
            clearTimeout(timer);
        }
    })();
    try {
        return await preparation;
    } finally {
        preparation = null;
    }
}
async function readJson(request) {
    if (request.headers['content-type'] !== 'application/json' || Number(request.headers['content-length'] ?? 0) > MAX_BODY) return null;
    const chunks = [];let length=0;
    for await (const chunk of request) {length+=chunk.length; if(length>MAX_BODY) throw new Error('Render body is too large'); chunks.push(chunk);}
    return JSON.parse(Buffer.concat(chunks).toString('utf8'));
}
async function render(job) {
    const abort = { done: false, acquired: false, released: false, entry: null, context: null };
    let slot;
    let timer;
    const task = (async () => {
        try {
        await acquire(abort);
        slot = await borrowPage(abort, job.scene.roomId);
        if (abort.done) throw new Error('Camera render timed out');
        // A string crosses into the page far faster than Playwright's object serialization of a large scene.
        const result = await slot.page.evaluate(async ({ body, smallSize }) => {
            const job = JSON.parse(body);
            const png = await window.cameraRender(job);
            const image = new Image();
            image.src = `data:image/png;base64,${png}`;
            await image.decode();
            if (image.naturalWidth !== job.viewport.cropWidth || image.naturalHeight !== job.viewport.cropHeight) throw new Error('Cropped snapshot does not match the viewport');
            const canvas = document.createElement('canvas');
            canvas.width = canvas.height = smallSize;
            const context = canvas.getContext('2d');
            context.imageSmoothingEnabled = true;
            context.drawImage(image, 0, 0, smallSize, smallSize);
            return { png, smallPng: canvas.toDataURL('image/png').split(',')[1] };
        }, { body: JSON.stringify(job), smallSize: SMALL_PNG_SIZE });
        if (abort.done) throw new Error('Camera render timed out');
        if (slot.failures.length) throw new Error(slot.failures[0]);
        validPng(result.png, job.viewport.cropWidth);
        validPng(result.smallPng, SMALL_PNG_SIZE);
        return result;
        } finally {
            if (abort.done && abort.acquired && !abort.released) { abort.released = true; release(); }
        }
    })();
    const timeout = new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('Camera render timed out')), JOB_DEADLINE_MS); });
    try {
        return await Promise.race([task, timeout]);
    } catch (error) {
        abort.done = true;
        if (abort.entry) {
            clearTimeout(abort.entry.timer);
            const index = queue.indexOf(abort.entry);
            if (index >= 0) queue.splice(index, 1);
        }
        if (slot) await discardPage(slot);
        else if (abort.context) await abort.context.close().catch(() => {});
        throw error;
    } finally {
        clearTimeout(timer);
        task.catch(() => {});
        if (slot && !abort.done) slot.busy = false;
        if (abort.acquired && !abort.released) { abort.released = true; release(); }
    }
}
const server = http.createServer(async (request, response) => {
    response.setHeader('X-Content-Type-Options','nosniff');
    response.setHeader('Cache-Control','no-store');
    if (!authorized(request.headers.authorization,secret)) {response.writeHead(401).end(); return;}
    // A closed page drops its requests; their downloads then stop unless another request still needs them.
    const lifetime = new AbortController();
    response.once('close', () => { if (!response.writableFinished) lifetime.abort(new Error('Camera asset request closed')); });
    try {
        const url = new URL(request.url,origin);
        if (url.pathname === '/effects' && request.method === 'GET') {response.setHeader('Content-Type','application/json'); response.end(JSON.stringify(catalogue)); return;}
        if (url.pathname === '/render' && request.method === 'POST') {
            const body = await readJson(request);
            if (!body) {response.writeHead(413).end(); return;}
            const result=await render(validateJob(body,catalogue));
            response.setHeader('Content-Type','application/json'); response.end(JSON.stringify(result)); return;
        }
        // 204 once the libraries are loaded, 409 when no page is free to prepare.
        if (url.pathname === '/prepare' && request.method === 'POST') {
            const body = await readJson(request);
            if (!body) {response.writeHead(413).end(); return;}
            response.writeHead(await prepare(validatePreparation(body).scene) ? 204 : 409).end(); return;
        }
        if (request.method !== 'GET') {response.writeHead(405).end(); return;}
        const path = decodeURIComponent(url.pathname);
        if (path.includes('\0') || path.includes('..') || path.includes('\\')) throw new Error('Invalid path');
        let data;
        if (path.startsWith('/page/')) data=await safeFile(resolve(directory,'dist'),path.slice(6));
        else if (path.startsWith('/gamedata/')) {
            const relative=gamedata[path.slice(10)];if(!relative) throw new Error('Unknown gamedata');
            if (path.endsWith('/FurnitureData.json')) data=(await currentCatalogue()).body;
            else data=assetOrigin ? await originAsset(path, lifetime.signal) : await safeFile(assets,relative);
        } else if (path.startsWith('/c_images/Habbo-Stories/')) data=await safeFile(images, cameraEffectAsset(path, catalogue));
        else if (mediaPath.test(path)) data=await safeFile(media,path.slice(8));
        else {
            const relative = nitroAssetRelative(path);
            if (!relative) throw new Error('Unknown trusted asset');
            data=assetOrigin ? await originAsset(path, lifetime.signal) : await safeFile(assets, relative);
        }
        response.setHeader('Content-Type',mime[extname(path)] ?? 'application/octet-stream'); response.end(data);
    } catch (error) {
        // Nobody waits for the answer to a closed request.
        if (!lifetime.signal.aborted) console.error(`Camera request failed: ${error.message}`);
        if (!response.headersSent) response.writeHead(422);
        response.end();
    }
});
server.headersTimeout=5000;
server.requestTimeout=JOB_DEADLINE_MS;
refill();
setInterval(revalidateCatalogue, 15000).unref();
server.listen(port,host,() => console.log(`Trusted camera renderer listening on ${host}:${port}`));
for (const signal of ['SIGTERM','SIGINT']) process.on(signal,async () => {shuttingDown = true; server.close(); await (await browser?.catch(() => null))?.close(); process.exit(0);});
