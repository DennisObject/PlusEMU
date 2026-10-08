import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';

// Only operator-configured sources are fetched; scene data never supplies a URL.
export async function fetchCameraData(url, headers = {}) {
    const response = await fetch(url, { headers, redirect: 'error', signal: AbortSignal.timeout(10000) });
    if (response.status === 304) return { status: 304 };
    if (!response.ok) throw new Error(`Camera data source returned ${response.status}: ${url}`);
    const chunks = [];
    let length = 0;
    for await (const chunk of response.body) {
        length += chunk.length;
        if (length > 32 * 1024 * 1024) throw new Error('Camera data source is too large');
        chunks.push(chunk);
    }
    return { status: response.status, etag: response.headers.get('etag'), headers: response.headers, body: Buffer.concat(chunks) };
}

export function createFurnitureDataSource({ url, filename }) {
    let cached;
    let etag;
    let refreshing = null;
    const refresh = async () => {
        let data;
        let nextEtag;
        if (url) {
            const response = await fetchCameraData(url, etag ? { 'If-None-Match': etag } : {});
            if (response.status === 304) {
                if (!cached) throw new Error('Furniture data source returned 304 without a catalogue');
                return cached;
            }
            data = response.body;
            nextEtag = response.etag;
        } else data = await readFile(filename);

        const json = JSON.parse(data);
        for (const name of ['roomitemtypes', 'wallitemtypes']) {
            const items = json[name]?.furnitype;
            if (!Array.isArray(items)) throw new Error('Invalid camera furniture catalogue');
            for (const item of items) { delete item.adurl; delete item.adUrl; }
        }
        const body = Buffer.from(JSON.stringify(json));
        cached = { body, version: createHash('sha256').update(body).digest('hex') };
        etag = nextEtag;
        return cached;
    };
    // Concurrent callers share one refresh, so a slower older response can never replace a newer catalogue.
    return () => refreshing ??= refresh().finally(() => { refreshing = null; });
}
