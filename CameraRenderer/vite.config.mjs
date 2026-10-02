import { defineConfig } from 'vite';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const renderer = resolve(fileURLToPath(new URL('../../Octane-Renderer', import.meta.url)));

export default defineConfig({
    // server.mjs serves the built page from dist at /page/.
    base: '/page/',
    resolve: {
        alias: [
            { find: '@octane/renderer', replacement: resolve(renderer, 'index.ts') },
            { find: /^@octane\/(.*)$/, replacement: `${renderer}/packages/$1/src/index.ts` },
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
