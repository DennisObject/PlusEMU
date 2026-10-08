import { timingSafeEqual } from 'node:crypto';
import { isAbsolute, relative, sep } from 'node:path';

export const MAX_BODY = 1024 * 1024;
export const MAX_PNG = 2 * 1024 * 1024;
export const MAX_ACTIVE = 2;
export const MAX_QUEUE = 4;
export const QUEUE_WAIT_MS = 5000;
export const JOB_DEADLINE_MS = 20000;
export const PREPARE_DEADLINE_MS = 12000;
export const CROP_MAIN = 320;
export const CROP_THUMBNAIL = 110;
export const SMALL_PNG_SIZE = 110;
export const mediaPath = /^\/camera\/(?:[a-f0-9]{32}|[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})(?:_small)?\.png$/;
export const GAMEDATA_FILES = Object.freeze(['FurnitureData.json', 'ProductData.json', 'FigureData.json', 'FigureMap.json', 'EffectMap.json', 'HabboAvatarActions.json']);
const NITRO_FOLDERS = { furniture: 'furniture/nitro', figure: 'clothes/nitro', effect: 'effects/nitro', pet: 'pets' };

export function nitroAssetRelative(pathname) {
    const match = /^\/assets\/(furniture|figure|effect|pet)\/([A-Za-z0-9_.-]+\.(?:nitro|hab))$/.exec(pathname);
    if (!match || match[2].includes('..')) return null;
    return `${NITRO_FOLDERS[match[1]]}/${match[2]}`;
}

export function containedPath(base, target) {
    if (typeof base !== 'string' || typeof target !== 'string' || !base || !target) return false;
    const rel = relative(base, target);
    return rel !== '' && !isAbsolute(rel) && !rel.startsWith(`..${sep}`) && !rel.split(sep).includes('..');
}

export function pageConfiguration(configuration, secret) {
    const copy = {};
    for (const [key, value] of Object.entries(configuration ?? {})) {
        if (/bearer|secret|password|token|authorization/i.test(key)) continue;
        if (typeof value === 'string' && secret && value.includes(secret)) continue;
        copy[key] = value;
    }
    return copy;
}
const EFFECT_NAME = /^[A-Za-z][A-Za-z0-9_]*$/;
const FRAME_EFFECTS = new Set(['frame_gold', 'frame_gray_4', 'frame_black_2', 'frame_wood_2', 'finger_nrm']);

export function isCameraEffectName(name) {
    return typeof name === 'string' && EFFECT_NAME.test(name);
}

// The allowlist is the configured camera catalogue. Asset filenames and paths never qualify.
export function buildEffectCatalogue(entries) {
    if (!Array.isArray(entries)) return [];
    const catalogue = [];
    const names = new Set();
    for (const effect of entries) {
        if (!effect || effect.enabled !== true || !isCameraEffectName(effect.name) || names.has(effect.name)) continue;
        if (!Number.isInteger(effect.minLevel) || effect.minLevel < 0 || effect.minLevel > 999) continue;
        const type = effect.type ?? (Array.isArray(effect.colorMatrix) && effect.colorMatrix.length ? 'colormatrix' : (FRAME_EFFECTS.has(effect.name) ? 'frame' : 'composite'));
        if (type !== 'colormatrix' && type !== 'composite' && type !== 'frame') continue;
        names.add(effect.name);
        catalogue.push({ name: effect.name, minLevel: effect.minLevel, type });
    }
    return catalogue;
}

// Browser traffic may only GET this origin's page, known gamedata, furniture/figure/effect/pet nitro, catalogue effect PNGs, and minted camera files.
export function allowedBrowserRequest(method, href, serviceOrigin, catalogue) {
    let url;
    try { url = new URL(href); }
    catch { return false; }
    if (method !== 'GET' || url.origin !== serviceOrigin) return false;
    let path;
    try { path = decodeURIComponent(url.pathname); }
    catch { return false; }
    if (path.includes('\0') || path.includes('..') || path.includes('\\')) return false;
    if (path.startsWith('/page/')) return path.length > '/page/'.length;
    if (path.startsWith('/gamedata/')) return GAMEDATA_FILES.includes(path.slice(10));
    if (path.startsWith('/c_images/')) {
        try { cameraEffectAsset(path, catalogue); return true; }
        catch { return false; }
    }
    if (path.startsWith('/camera/')) return mediaPath.test(path);
    return nitroAssetRelative(path) !== null;
}

// Serves Habbo-Stories/<catalogue effect>.png only. Any other trusted asset filename is refused.
export function cameraEffectAsset(pathname, catalogue) {
    const match = /^\/c_images\/Habbo-Stories\/([A-Za-z][A-Za-z0-9_]*)\.png$/.exec(pathname);
    const effect = match && catalogue.find(entry => entry.name === match[1] && entry.type !== 'colormatrix');
    if (!effect) throw new Error('Camera effect asset is not a catalogue effect');
    return `Habbo-Stories/${effect.name}.png`;
}

export function renderedPngSize(cropWidth, cropHeight) {
    const main = cropWidth === CROP_MAIN && cropHeight === CROP_MAIN;
    const thumbnail = cropWidth === CROP_THUMBNAIL && cropHeight === CROP_THUMBNAIL;
    if (!Number.isInteger(cropWidth) || !Number.isInteger(cropHeight) || (!main && !thumbnail)) throw new Error('Invalid viewport crop');
    return cropWidth;
}

// Zoom stays a boolean. True is the centered 2x inside the 320 photo. It does not change the PNG size, and a 110 thumbnail cannot zoom.
function cameraZoom(zoom, cropWidth) {
    if (typeof zoom !== 'boolean' || (cropWidth === CROP_THUMBNAIL && zoom)) throw new Error('Invalid zoom');
    return zoom;
}

export function authorized(header, secret) {
    const actual = Buffer.from(header ?? '');
    const expected = Buffer.from(`Bearer ${secret}`);
    return actual.length === expected.length && timingSafeEqual(actual, expected);
}

export function validPng(base64, size) {
    if (typeof base64 !== 'string' || base64.length > Math.ceil(MAX_PNG / 3) * 4 || !/^[A-Za-z0-9+/]*={0,2}$/.test(base64)) throw new Error('Invalid PNG');
    const bytes = Buffer.from(base64, 'base64');
    if (bytes.length < 33 || bytes.length > MAX_PNG || !bytes.subarray(0, 8).equals(Buffer.from([137,80,78,71,13,10,26,10])) || bytes.toString('ascii', 12, 16) !== 'IHDR' || bytes.readUInt32BE(16) !== size || bytes.readUInt32BE(20) !== size) throw new Error('Invalid PNG dimensions');
    return bytes;
}

export function validateJob(job, catalogue) {
    if (!job || Object.keys(job).some(key => !['scene','viewport','effects','zoom','level'].includes(key))) throw new Error('Invalid render job');
    const view = validateViewport(job.viewport);
    job.zoom = cameraZoom(job.zoom, view.cropWidth);
    if (!Number.isInteger(job.level) || job.level < 0 || job.level > 1000 || !Array.isArray(job.effects) || job.effects.length > 8) throw new Error('Invalid effects');
    const names = new Set();
    let frames = 0;
    for (const selection of job.effects) {
        if (!selection || Object.keys(selection).some(key => !['name','strength'].includes(key)) || !isCameraEffectName(selection.name) || names.has(selection.name)) throw new Error('Invalid effect selection');
        const effect = catalogue.find(entry => entry.name === selection.name);
        if (!effect || effect.minLevel > job.level) throw new Error('Effect is unavailable');
        if (effect.type === 'frame') {
            if (++frames > 1) throw new Error('Only one frame is allowed');
            selection.strength = 1;
        } else if (!Number.isFinite(selection.strength) || selection.strength < 0 || selection.strength > 1) throw new Error('Invalid effect selection');
        names.add(selection.name);
    }
    validateScene(job.scene);
    return job;
}

// Preparation readies a scene ahead of its photo. It carries the scene and, once the camera knows it, the viewport.
export function validatePreparation(body) {
    if (!body || Object.keys(body).some(key => key !== 'scene' && key !== 'viewport')) throw new Error('Invalid preparation');
    validateScene(body.scene);
    if (body.viewport !== undefined) validateViewport(body.viewport);
    return body;
}

function validateViewport(view) {
    const numeric = ['width','height','offsetX','offsetY','x','y','cropWidth','cropHeight','scale','locationX','locationY','locationZ'];
    if (!view || Object.keys(view).length !== numeric.length || numeric.some(key => !Number.isFinite(view[key]))) throw new Error('Invalid viewport');
    if (view.scale !== 1 || !Number.isInteger(view.width) || !Number.isInteger(view.height) || view.width < 320 || view.height < 320 || view.width > 2048 || view.height > 2048) throw new Error('Invalid viewport dimensions');
    renderedPngSize(view.cropWidth, view.cropHeight);
    if (['x','y','offsetX','offsetY'].some(key => Math.abs(view[key]) > 4096) || ['locationX','locationY','locationZ'].some(key => Math.abs(view[key]) > 256)) throw new Error('Invalid viewport position');
    return view;
}

function validateScene(scene) {
    if (!scene || !Number.isInteger(scene.roomId) || scene.roomId <= 0 || typeof scene.heightmap !== 'string' || scene.heightmap.length > 65536 || !Array.isArray(scene.items) || scene.items.length > 5000 || !Array.isArray(scene.users) || scene.users.length > 1000) throw new Error('Invalid server scene');
    // URLs are never a scene instruction. Photographs refer only to previously minted files.
    const scan = value => {
        if (typeof value === 'string' && (/(?:https?:|data:|blob:|javascript:|file:|\/\/)/i.test(value) || value.includes('\0'))) throw new Error('URL in scene');
        if (Array.isArray(value)) value.forEach(scan);
        else if (value && typeof value === 'object') Object.values(value).forEach(scan);
    };
    scan(scene);
    for (const user of scene.users) if (user && typeof user === 'object' && user.gesture === '0') user.gesture = '';
}
