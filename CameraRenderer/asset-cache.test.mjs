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
    assert.deepEqual(cache.size(), { entries: 2, bytes: 80, downloads: 0, queued: 0, pending: 0 });
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
    await Promise.all(['a', 'b', 'c', 'd', 'e'].map(key => cache(key)));
    assert.equal(peak, 2);
    assert.deepEqual(cache.size(), { entries: 5, bytes: 5, downloads: 0, queued: 0, pending: 0 });
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

// Downloads that finish only when the test says so, and stop when their signal aborts.
const controlled = () => {
    const started = [];
    const stopped = [];
    const load = (key, signal) => new Promise((resolve, reject) => {
        started.push(key);
        signal.addEventListener('abort', () => { stopped.push(key); reject(signal.reason); }, { once: true });
        started[key] = () => resolve({ body: Buffer.from(key), freshMs: 1000 });
    });
    return { started, stopped, load };
};
const settle = () => new Promise(resolve => setImmediate(resolve));

test('abandoned queued downloads leave the queue so a later request gets the slot', async () => {
    const { started, stopped, load } = controlled();
    const cache = createAssetCache({ load, maxBytes: 100, maxDownloads: 2 });
    const page = new AbortController();
    const closed = ['a', 'b', 'c', 'd', 'e'].map(key => cache(key, page.signal));
    closed.forEach(request => request.catch(() => {}));
    await settle();
    assert.deepEqual(cache.size().queued, 3);
    page.abort(new Error('page closed'));
    for (const request of closed) await assert.rejects(request, /page closed/);
    await settle();
    // Both running downloads stopped; nothing queued is left to start.
    assert.deepEqual([...started], ['a', 'b']);
    assert.deepEqual(stopped, ['a', 'b']);
    const photo = cache('f');
    await settle();
    assert.deepEqual([...started], ['a', 'b', 'f']);
    started.f();
    assert.equal((await photo).toString(), 'f');
    assert.deepEqual(cache.size(), { entries: 1, bytes: 1, downloads: 0, queued: 0, pending: 0 });
});

test('a shared download keeps running for the callers that remain', async () => {
    const { started, stopped, load } = controlled();
    const cache = createAssetCache({ load, maxBytes: 100, maxDownloads: 2 });
    const closing = new AbortController();
    const abandoned = cache('a', closing.signal);
    const kept = cache('a');
    await settle();
    closing.abort(new Error('page closed'));
    await assert.rejects(abandoned, /page closed/);
    assert.deepEqual(stopped, []);
    started.a();
    assert.equal((await kept).toString(), 'a');
    assert.equal(await cache('a'), await kept);
    assert.deepEqual([...started], ['a']);
});

test('a download stopped by its last caller is not reused by the next one', async () => {
    const { started, load } = controlled();
    const cache = createAssetCache({ load, maxBytes: 100, maxDownloads: 2 });
    const closing = new AbortController();
    const abandoned = cache('a', closing.signal);
    await settle();
    closing.abort(new Error('page closed'));
    await assert.rejects(abandoned, /page closed/);
    const next = cache('a');
    await settle();
    started.a();
    assert.equal((await next).toString(), 'a');
    assert.deepEqual([...started], ['a', 'a']);
    assert.deepEqual(cache.size(), { entries: 1, bytes: 1, downloads: 0, queued: 0, pending: 0 });
});

test('an already aborted caller starts nothing', async () => {
    const { started, load } = controlled();
    const cache = createAssetCache({ load, maxBytes: 100, maxDownloads: 1 });
    await assert.rejects(cache('a', AbortSignal.abort(new Error('gone'))), /gone/);
    await settle();
    assert.deepEqual(cache.size(), { entries: 0, bytes: 0, downloads: 0, queued: 0, pending: 0 });
    assert.deepEqual([...started], []);
});

test('freshness is max-age less age and nothing for uncacheable responses', () => {
    assert.equal(freshness(new Headers({ 'cache-control': 'public, max-age=14400', age: '1282' })), (14400 - 1282) * 1000);
    assert.equal(freshness(new Headers({ 'cache-control': 'max-age=60', age: '90' })), 0);
    assert.equal(freshness(new Headers({ 'cache-control': 'no-cache, max-age=60' })), 0);
    assert.equal(freshness(new Headers({ 'cache-control': 'max-age=60', age: 'x' })), 0);
    assert.equal(freshness(new Headers()), 0);
});
