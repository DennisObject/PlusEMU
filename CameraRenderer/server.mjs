import http from 'node:http';
import { readFile, realpath } from 'node:fs/promises';
import { resolve, extname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import stripJsonComments from 'strip-json-comments';
import { allowedBrowserRequest, authorized, buildEffectCatalogue, cameraEffectAsset, containedPath, JOB_DEADLINE_MS, MAX_ACTIVE, MAX_BODY, MAX_QUEUE, mediaPath, nitroAssetRelative, pageConfiguration, QUEUE_WAIT_MS, SMALL_PNG_SIZE, validPng, validateJob } from './security.mjs';

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
async function createPage(abort) {
    if (!browser) {
        browser = await chromium.launch({executablePath:process.env.CAMERA_CHROMIUM_PATH || undefined, headless:true, chromiumSandbox:true, args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']});
        browser.once('disconnected', () => {
            if (shuttingDown) return;
            console.error('Trusted camera browser disconnected; restarting renderer');
            process.exit(1);
        });
    }
    if (abort?.done) throw new Error('Camera render timed out');
    const context = await browser.newContext({viewport:{width:2048,height:2048},deviceScaleFactor:1,extraHTTPHeaders:{Authorization:`Bearer ${secret}`}});
    if (abort) abort.context = context;
    if (abort?.done) throw new Error('Camera render timed out');
    const slot = { context, page: null, busy: true, failures: [] };
    try {
        await context.route('**/*', async route => {
            const request = route.request();
            const href = request.url();
            if (!allowedBrowserRequest(request.method(), href, origin, catalogue)) { slot.failures.push('Blocked asset request: '+href); console.error('Blocked camera asset: '+href); await route.abort(); return; }
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
function borrowPage(abort) {
    return withGate(async () => {
        if (abort?.done) throw new Error('Camera render timed out');
        const idle = pool.find(entry => !entry.busy);
        if (idle) { idle.busy = true; idle.failures.length = 0; return idle; }
        if (pool.length >= 2) throw new Error('Camera page pool is exhausted');
        const slot = await createPage(abort);
        if (abort?.done) throw new Error('Camera render timed out');
        pool.push(slot);
        if (abort?.done) { pool.splice(pool.indexOf(slot), 1); throw new Error('Camera render timed out'); }
        return slot;
    });
}
async function discardPage(slot) {
    const index = pool.indexOf(slot);
    if (index >= 0) pool.splice(index, 1);
    await slot?.context.close().catch(() => {});
}
async function render(job) {
    const abort = { done: false, acquired: false, released: false, entry: null, context: null };
    let slot;
    let timer;
    const task = (async () => {
        try {
        await acquire(abort);
        slot = await borrowPage(abort);
        if (abort.done) throw new Error('Camera render timed out');
        const result = await slot.page.evaluate(async ({ job, smallSize }) => {
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
        }, { job, smallSize: SMALL_PNG_SIZE });
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
    try {
        const url = new URL(request.url,origin);
        if (url.pathname === '/effects' && request.method === 'GET') {response.setHeader('Content-Type','application/json'); response.end(JSON.stringify(catalogue)); return;}
        if (url.pathname === '/render' && request.method === 'POST') {
            if (request.headers['content-type'] !== 'application/json' || Number(request.headers['content-length'] ?? 0) > MAX_BODY) {response.writeHead(413).end(); return;}
            const chunks = [];let length=0;
            for await (const chunk of request) {length+=chunk.length; if(length>MAX_BODY) throw new Error('Render body is too large'); chunks.push(chunk);}
            const job=validateJob(JSON.parse(Buffer.concat(chunks).toString('utf8')),catalogue);
            const result=await render(job);
            response.setHeader('Content-Type','application/json'); response.end(JSON.stringify(result)); return;
        }
        if (request.method !== 'GET') {response.writeHead(405).end(); return;}
        const path = decodeURIComponent(url.pathname);
        if (path.includes('\0') || path.includes('..') || path.includes('\\')) throw new Error('Invalid path');
        let data;
        if (path.startsWith('/page/')) data=await safeFile(resolve(directory,'dist'),path.slice(6));
        else if (path.startsWith('/gamedata/')) {
            const relative=gamedata[path.slice(10)];if(!relative) throw new Error('Unknown gamedata');
            data=await safeFile(assets,relative);
            if (path.endsWith('/FurnitureData.json')) {
                const json=JSON.parse(data);
                for(const collection of [json.roomitemtypes?.furnitype,json.wallitemtypes?.furnitype]) for(const item of collection ?? []) {delete item.adurl; delete item.adUrl;}
                data=Buffer.from(JSON.stringify(json));
            }
        } else if (path.startsWith('/c_images/Habbo-Stories/')) data=await safeFile(images, cameraEffectAsset(path, catalogue));
        else if (mediaPath.test(path)) data=await safeFile(media,path.slice(8));
        else {
            const relative = nitroAssetRelative(path);
            if (!relative) throw new Error('Unknown trusted asset');
            data=await safeFile(assets, relative);
        }
        response.setHeader('Content-Type',mime[extname(path)] ?? 'application/octet-stream'); response.end(data);
    } catch (error) {
        console.error(`Camera request failed: ${error.message}`);
        if (!response.headersSent) response.writeHead(422);
        response.end();
    }
});
server.headersTimeout=5000;
server.requestTimeout=JOB_DEADLINE_MS;
void withGate(async () => {
    const slot = await createPage();
    slot.busy = false;
    pool.push(slot);
    console.log('Trusted camera page ready');
}).catch(error => console.error('Trusted camera page failed: '+error.message));
server.listen(port,host,() => console.log(`Trusted camera renderer listening on ${host}:${port}`));
for (const signal of ['SIGTERM','SIGINT']) process.on(signal,async () => {shuttingDown = true; server.close(); await browser?.close(); process.exit(0);});
