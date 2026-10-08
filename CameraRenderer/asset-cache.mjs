// Pages route every request, which bypasses the browser's HTTP cache, so each new page would download every
// library again. Keep fetched origin assets in memory for as long as the origin allows, least recently used first
// out, and share one download between concurrent requests. A few downloads at a time reuse the origin's open
// connections instead of opening one per library. Failures are never kept.
export function createAssetCache({ load, maxBytes, maxDownloads, now = Date.now }) {
    const entries = new Map();
    const pending = new Map();
    const waiting = [];
    let downloads = 0;
    let generation = 0;
    let bytes = 0;

    const remove = key => {
        const entry = entries.get(key);
        if (!entry) return;
        entries.delete(key);
        bytes -= entry.body.length;
    };

    const store = (key, body, freshMs) => {
        remove(key);
        if (!(freshMs > 0) || body.length > maxBytes) return;
        entries.set(key, { body, expires: now() + freshMs });
        bytes += body.length;
        for (const oldest of entries.keys()) {
            if (bytes <= maxBytes) break;
            remove(oldest);
        }
    };

    const download = async key => {
        if (downloads < maxDownloads) downloads++;
        else await new Promise(resolve => waiting.push(resolve));
        try {
            return await load(key);
        } finally {
            const next = waiting.shift();
            if (next) next();
            else downloads--;
        }
    };

    const cache = key => {
        const entry = entries.get(key);
        if (entry && now() < entry.expires) {
            entries.delete(key);
            entries.set(key, entry);
            return Promise.resolve(entry.body);
        }
        let request = pending.get(key);
        if (!request) {
            const started = generation;
            request = download(key).then(({ body, freshMs }) => {
                if (started === generation) store(key, body, freshMs);
                return body;
            }).finally(() => {
                if (pending.get(key) === request) pending.delete(key);
            });
            pending.set(key, request);
        }
        return request;
    };
    // Downloads that started before a clear still answer their callers but are not kept.
    cache.clear = () => {
        generation++;
        entries.clear();
        pending.clear();
        bytes = 0;
    };
    cache.size = () => ({ entries: entries.size, bytes });
    return cache;
}

// Remaining freshness from Cache-Control max-age less Age, the way a shared cache counts it; nothing without one.
export function freshness(headers) {
    const control = headers.get('cache-control') ?? '';
    if (/(?:^|,)\s*(?:no-store|no-cache|private)\b/i.test(control)) return 0;
    const maxAge = /(?:^|,)\s*max-age=(\d+)/i.exec(control);
    if (!maxAge) return 0;
    const age = Number(headers.get('age') ?? 0);
    return Math.max(0, Number(maxAge[1]) - (Number.isFinite(age) ? age : Number(maxAge[1]))) * 1000;
}
