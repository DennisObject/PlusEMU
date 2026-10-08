import assert from 'node:assert/strict';
import test from 'node:test';
import { createAssetCache, freshness } from './asset-cache.mjs';

// Keys name their size; every asset is fresh for a second.
const loader = () => {
    const calls = [];
    const load = async key => { calls.push(key); return { body: Buffer.alloc(Number(key.split(':')[1])), freshMs: 1000 }; };
    return { calls, load };
};

test('shares one download between concurrent requests and serves later ones from memory', async () => {
    const { calls, load } = loader();
    const cache = createAssetCache({ load, maxBytes: 100, maxDownloads: 8 });
    const [first, second] = await Promise.all([cache('a:10'), cache('a:10')]);
    assert.equal(first, second);
    assert.equal(await cache('a:10'), first);
    assert.deepEqual(calls, ['a:10']);
});

test('evicts the least recently used assets beyond the byte budget and never keeps an oversized one', async () => {
    const { calls, load } = loader();
    const cache = createAssetCache({ load, maxBytes: 100, maxDownloads: 8 });
    await cache('a:40');
    await cache('b:40');
    await cache('a:40');
    await cache('c:40');
    assert.deepEqual(cache.size(), { entries: 2, bytes: 80 });
    await cache('a:40');
    await cache('b:40');
    await cache('d:101');
    await cache('d:101');
    assert.deepEqual(calls, ['a:40', 'b:40', 'c:40', 'b:40', 'd:101', 'd:101']);
});

test('keeps an asset only while the origin allows and never keeps failures', async () => {
    let time = 0;
    let fail = true;
    const calls = [];
    const load = async key => { calls.push(key); if (fail) throw new Error('origin down'); return { body: Buffer.from(key), freshMs: key === 'stale' ? 0 : 1000 }; };
    const cache = createAssetCache({ load, maxBytes: 100, maxDownloads: 8, now: () => time });
    await assert.rejects(cache('a'), /origin down/);
    fail = false;
    const body = await cache('a');
    time = 999;
    assert.equal(await cache('a'), body);
    time = 1000;
    await cache('a');
    await cache('stale');
    await cache('stale');
    assert.deepEqual(calls, ['a', 'a', 'a', 'stale', 'stale']);
});

test('limits concurrent downloads and hands each finished slot to the next one', async () => {
    let running = 0;
    let peak = 0;
    const load = async key => {
        peak = Math.max(peak, ++running);
        await new Promise(resolve => setTimeout(resolve, 5));
        running--;
        return { body: Buffer.from(key), freshMs: 1000 };
    };
    const cache = createAssetCache({ load, maxBytes: 100, maxDownloads: 2 });
    await Promise.all(['a', 'b', 'c', 'd', 'e'].map(cache));
    assert.equal(peak, 2);
    assert.deepEqual(cache.size(), { entries: 5, bytes: 5 });
});

test('a clear drops kept assets and a download that started before it is not kept', async () => {
    let release;
    const calls = [];
    const load = async key => {
        const label = key + calls.push(key);
        if (calls.length === 1) await new Promise(resolve => { release = resolve; });
        return { body: Buffer.from(label), freshMs: 1000 };
    };
    const cache = createAssetCache({ load, maxBytes: 100, maxDownloads: 8 });
    const old = cache('a');
    await new Promise(resolve => setImmediate(resolve));
    cache.clear();
    const current = await cache('a');
    release();
    assert.equal((await old).toString(), 'a1');
    assert.equal(current.toString(), 'a2');
    assert.equal(await cache('a'), current);
    assert.deepEqual(calls, ['a', 'a']);
});

test('freshness is max-age less age and nothing for uncacheable responses', () => {
    assert.equal(freshness(new Headers({ 'cache-control': 'public, max-age=14400', age: '1282' })), (14400 - 1282) * 1000);
    assert.equal(freshness(new Headers({ 'cache-control': 'max-age=60', age: '90' })), 0);
    assert.equal(freshness(new Headers({ 'cache-control': 'no-cache, max-age=60' })), 0);
    assert.equal(freshness(new Headers({ 'cache-control': 'max-age=60', age: 'x' })), 0);
    assert.equal(freshness(new Headers()), 0);
});
