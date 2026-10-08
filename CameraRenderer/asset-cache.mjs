// Pages route every request, which bypasses the browser's HTTP cache, so each new page would download every
// library again. Keep fetched origin assets in memory for as long as the origin allows, least recently used first
// out, and share one download between concurrent requests. A few downloads at a time reuse the origin's open
// connections instead of opening one per library. Failures are never kept.
//
// Each caller may pass the AbortSignal of its own request. A download that every caller has abandoned, such as the
// libraries of a closed page, leaves the queue or stops, so it never holds a slot a later photo needs.
export function createAssetCache({ load, maxBytes, maxDownloads, now = Date.now }) {
    const entries = new Map();
    const pending = new Map();
    const waiting = new Set();
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

    // A freed slot passes straight to the longest waiting download; an abandoned one leaves the queue.
    const slot = signal => new Promise((resolve, reject) => {
        if (downloads < maxDownloads) { downloads++; resolve(); return; }
        const waiter = { resolve: () => { signal.removeEventListener('abort', leave); resolve(); } };
        const leave = () => { waiting.delete(waiter); reject(signal.reason); };
        waiting.add(waiter);
        signal.addEventListener('abort', leave, { once: true });
    });

    const release = () => {
        const [next] = waiting;
        if (!next) { downloads--; return; }
        waiting.delete(next);
        next.resolve();
    };

    const start = key => {
        const started = generation;
        const controller = new AbortController();
        const download = { callers: 0, controller };
        download.promise = slot(controller.signal).then(async () => {
            try {
                controller.signal.throwIfAborted();
                return await load(key, controller.signal);
            } finally {
                release();
            }
        }).then(({ body, freshMs }) => {
            if (started === generation) store(key, body, freshMs);
            return body;
        }).finally(() => {
            if (pending.get(key) === download) pending.delete(key);
        });
        download.promise.catch(() => {});
        return download;
    };

    // The last caller to leave stops the download, and a later caller starts a new one.
    const follow = (key, download, signal) => new Promise((resolve, reject) => {
        let left = false;
        const leave = () => {
            if (left) return;
            left = true;
            download.callers--;
            signal?.removeEventListener('abort', abandon);
        };
        const abandon = () => {
            leave();
            if (download.callers === 0) {
                if (pending.get(key) === download) pending.delete(key);
                download.controller.abort(signal.reason);
            }
            reject(signal.reason);
        };
        download.callers++;
        signal?.addEventListener('abort', abandon, { once: true });
        download.promise.then(body => { leave(); resolve(body); }, error => { leave(); reject(error); });
    });

    const cache = (key, signal) => {
        if (signal?.aborted) return Promise.reject(signal.reason);
        const entry = entries.get(key);
        if (entry && now() < entry.expires) {
            entries.delete(key);
            entries.set(key, entry);
            return Promise.resolve(entry.body);
        }
        let download = pending.get(key);
        if (!download) {
            download = start(key);
            pending.set(key, download);
        }
        return follow(key, download, signal);
    };
    // Downloads that started before a clear still answer their callers but are not kept.
    cache.clear = () => {
        generation++;
        entries.clear();
        pending.clear();
        bytes = 0;
    };
    cache.size = () => ({ entries: entries.size, bytes, downloads, queued: waiting.size, pending: pending.size });
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
