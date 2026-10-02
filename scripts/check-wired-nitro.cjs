#!/usr/bin/env node
// Run the actual Octane bundle parser with a PNG decoder in place of GPU texture loading.
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { createRequire } = require('module');
const { execFileSync } = require('child_process');
const { Readable } = require('stream');

const [converter, renderer, overlay] = process.argv.slice(2);
if (!converter || !renderer || !overlay) throw new Error('Usage: node check-wired-nitro.cjs CONVERTER OCTANE_RENDERER PRIVATE_FURNITURE');
const dependency = createRequire(path.resolve(converter, 'package.json'));
const ts = dependency('typescript');
const PNGDecoder = dependency('png-stream/decoder');
const concat = dependency('concat-frames');
const hashes = {};
const cached = new Map();
const localized = new Map();

function load(file) {
    if (cached.has(file)) return cached.get(file);
    const source = fs.readFileSync(file, 'utf8');
    hashes[path.basename(file)] = crypto.createHash('sha256').update(source).digest('hex');
    const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 } }).outputText;
    const exported = { exports: {} };
    new Function('require', 'module', 'exports', compiled)(name => {
        if (name === 'pixi.js') return { Texture: { from: () => { throw new Error('GPU loading is outside this check.'); } } };
        if (name === '@octane/api') return { FurnitureType: { FLOOR: 's', WALL: 'i' } };
        if (name === '@octane/localization') return { GetLocalizationManager: () => ({ setValue: (key, value) => localized.set(key, value) }) };
        if (name === '@octane/configuration' || name === '@octane/utils') return {};
        if (name.startsWith('./')) return load(path.resolve(path.dirname(file), name + '.ts'));
        return dependency(name);
    }, exported, exported.exports);
    cached.set(file, exported.exports);
    return exported.exports;
}

function decode(bytes) {
    return new Promise((resolve, reject) => {
        const stream = new PNGDecoder();
        stream.on('error', reject);
        Readable.from([Buffer.from(bytes)]).pipe(stream).pipe(concat(frames => {
            if (frames.length !== 1 || !frames[0].pixels.length) return reject(new Error('Invalid native PNG frames.'));
            resolve({ width: frames[0].width, height: frames[0].height });
        }));
    });
}

(async () => {
    const parserPath = 'packages/utils/src/OctaneBundle.ts';
    const { OctaneBundle } = load(path.resolve(renderer, parserPath));
    const rendererCommit = execFileSync('git', ['-C', renderer, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
    const parserChanges = execFileSync('git', ['-C', renderer, 'status', '--porcelain', '--', parserPath, 'packages/utils/src/BinaryReader.ts', 'packages/session/src/furniture/FurnitureDataLoader.ts', 'packages/session/src/furniture/FurnitureData.ts'], { encoding: 'utf8' }).trim();
    const provenance = JSON.parse(fs.readFileSync(path.join(overlay, 'original-assets.json')));
    const { FurnitureDataLoader } = load(path.resolve(renderer, 'packages/session/src/furniture/FurnitureDataLoader.ts'));
    const floors = new Map(), walls = new Map();
    const loader = new FurnitureDataLoader(floors, walls);
    const furnitureData = JSON.parse(fs.readFileSync(path.join(overlay, 'json/FurnitureData.json')));
    const loaded = loader.parseFloorItems(furnitureData.roomitemtypes);
    loader.parseWallItems(furnitureData.wallitemtypes);
    let frames = 0;
    for (const entry of provenance.entries) {
        const furniture = floors.get(entry.sprite_id);
        if (!furniture || furniture.className !== entry.asset_classname || furniture.tileSizeX !== entry.model.dimensions.x || furniture.tileSizeY !== entry.model.dimensions.y || !localized.has('roomItem.name.' + entry.sprite_id)) throw new Error('Active furniture-data loader identity/geometry disagrees.');
        const bytes = fs.readFileSync(path.join(overlay, entry.nitro_path));
        if (crypto.createHash('sha256').update(bytes).digest('hex') !== entry.nitro_sha256) throw new Error('Nitro hash changed.');
        const exact = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
        const bundle = await OctaneBundle.from(exact, decode);
        if (bundle.jsonFile.name !== entry.asset_classname || JSON.stringify(bundle.jsonFile.logic.model) !== JSON.stringify({ dimensions: entry.model.dimensions, directions: entry.model.directions })) throw new Error('Native renderer identity/model disagrees.');
        const size = bundle.jsonFile.spritesheet.meta.size;
        if (bundle.texture.width !== size.w || bundle.texture.height !== size.h) throw new Error('Decoded texture size disagrees.');
        frames += Object.keys(bundle.jsonFile.spritesheet.frames).length;
        const icon = fs.readFileSync(path.join(overlay, 'icons', entry.asset_classname + '_icon.png'));
        if (crypto.createHash('sha256').update(icon).digest('hex') !== entry.icon_sha256) throw new Error('Icon hash changed.');
        await decode(icon.buffer.slice(icon.byteOffset, icon.byteOffset + icon.byteLength));
    }
    console.log(JSON.stringify({ native_bundles: provenance.entries.length, native_icons: provenance.entries.length, sprite_frames: frames, loaded_floor_entries: loaded.length, loaded_wall_entries: furnitureData.wallitemtypes.furnitype.length, renderer_commit: rendererCommit, parser_path: parserPath, parser_uncommitted_changes: parserChanges, parser_source_sha256: hashes, texture_check: 'PNG pixels decoded; browser GPU rendering requires preview validation' }));
})().catch(error => { console.error(error.message); process.exitCode = 1; });
