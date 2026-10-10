import { defineConfig } from 'vite';
import { existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

// Checkouts cloned before the Volt rename keep the Octane-Renderer folder name.
const [currentRenderer, previousRenderer] = ['../../Volt-Renderer', '../../Octane-Renderer']
    .map((folder) => resolve(fileURLToPath(new URL(folder, import.meta.url))));
const renderer = existsSync(currentRenderer) || !existsSync(previousRenderer) ? currentRenderer : previousRenderer;

export default defineConfig({
    // server.mjs serves the built page from dist at /page/.
    base: '/page/',
    resolve: {
        alias: [
            { find: '@volt/renderer', replacement: resolve(renderer, 'index.ts') },
            { find: /^@volt\/(.*)$/, replacement: `${renderer}/packages/$1/src/index.ts` },
            { find: 'pixi.js/advanced-blend-modes', replacement: resolve(renderer, 'node_modules/pixi.js/lib/advanced-blend-modes/init.mjs') },
            { find: 'pixi.js', replacement: resolve(renderer, 'node_modules/pixi.js') },
            { find: 'pixi-filters', replacement: resolve(renderer, 'node_modules/pixi-filters') }
        ],
        dedupe: ['pixi.js']
    },
    build: {
        target: 'es2022',
        sourcemap: false,
        outDir: 'dist',
        emptyOutDir: true
    }
});
