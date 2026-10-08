import assert from 'node:assert/strict';
import test from 'node:test';
import http from 'node:http';
import { once } from 'node:events';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createFurnitureDataSource, fetchCameraData } from './furniture-data.mjs';

const catalogue = id => ({ roomitemtypes: { furnitype: [{ id, classname: 'chair', adurl: 'https://ad.invalid', adUrl: 'https://ad.invalid' }] }, wallitemtypes: { furnitype: [] } });

test('revalidates the hotel catalogue and replaces its version without altering an in-flight snapshot', async t => {
    let version = 1;
    let broken = false;
    const tags = [];
    const server = http.createServer((request, response) => {
        tags.push(request.headers['if-none-match']);
        if (broken) { response.writeHead(503).end(); return; }
        if (request.headers['if-none-match'] === `"${version}"`) { response.writeHead(304).end(); return; }
        response.setHeader('ETag', `"${version}"`);
        response.end(JSON.stringify(catalogue(version)));
    }).listen(0, '127.0.0.1');
    t.after(() => server.close());
    await once(server, 'listening');
    const source = createFurnitureDataSource({ url: `http://127.0.0.1:${server.address().port}/furnidata` });
    const old = await source();
    assert.deepEqual(JSON.parse(old.body).roomitemtypes.furnitype, [{ id: 1, classname: 'chair' }]);
    assert.equal(await source(), old);
    version = 2;
    const current = await source();
    assert.notEqual(current.version, old.version);
    assert.equal(JSON.parse(current.body).roomitemtypes.furnitype[0].id, 2);
    assert.equal(JSON.parse(old.body).roomitemtypes.furnitype[0].id, 1);
    assert.deepEqual(tags, [undefined, '"1"', '"1"']);
    broken = true;
    await assert.rejects(source(), /503/);
    broken = false;
    assert.equal(await source(), current);
});

test('concurrent callers share one catalogue refresh', async t => {
    let requests = 0;
    const server = http.createServer((request, response) => {
        requests++;
        response.setHeader('ETag', '"1"');
        setTimeout(() => response.end(JSON.stringify(catalogue(1))), 20);
    }).listen(0, '127.0.0.1');
    t.after(() => server.close());
    await once(server, 'listening');
    const source = createFurnitureDataSource({ url: `http://127.0.0.1:${server.address().port}/furnidata` });
    const [first, second] = await Promise.all([source(), source()]);
    assert.equal(first, second);
    assert.equal(requests, 1);
});

test('local catalogues refresh after replacement and invalid data does not become a snapshot', async t => {
    const directory = await mkdtemp(join(tmpdir(), 'camera-furnidata-'));
    t.after(() => rm(directory, { recursive: true, force: true }));
    const filename = join(directory, 'FurnitureData.json');
    const source = createFurnitureDataSource({ filename });
    await writeFile(filename, JSON.stringify(catalogue(1)));
    const old = await source();
    await writeFile(filename, JSON.stringify(catalogue(2)));
    assert.notEqual((await source()).version, old.version);
    await writeFile(filename, '{}');
    await assert.rejects(source(), /Invalid camera furniture catalogue/);
});

test('a camera data fetch stops when its caller leaves', async t => {
    const server = http.createServer(() => {}).listen(0, '127.0.0.1');
    t.after(() => server.close());
    await once(server, 'listening');
    const caller = new AbortController();
    const fetching = fetchCameraData(`http://127.0.0.1:${server.address().port}/asset`, {}, caller.signal);
    setTimeout(() => caller.abort(new Error('page closed')), 20);
    await assert.rejects(fetching, /page closed/);
    server.closeAllConnections();
});

test('camera data fetches refuse redirects to another source', async t => {
    const server = http.createServer((request, response) => response.writeHead(302, { Location: 'https://untrusted.invalid/' }).end()).listen(0, '127.0.0.1');
    t.after(() => server.close());
    await once(server, 'listening');
    await assert.rejects(fetchCameraData(`http://127.0.0.1:${server.address().port}/asset`));
});
