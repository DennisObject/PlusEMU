import { OctaneAdjustmentFilter, OctaneContainer, OctaneSprite, OctaneTexture } from '@octane/renderer';

const MAX_FRAME_PIXELS = 2 * 1024 * 1024;
const MAX_READ_PIXELS = 4 * 1024 * 1024;
const MAX_MASK_PIXELS = 8 * 1024 * 1024;
const MAX_CACHED_BYTES = 16 * 1024 * 1024;

let cachedPixelBytes = 0;
// A worker's cache stays bounded even across many catalogue generations.
const pixelsByImage = new WeakMap<ImageBitmap, Map<string, Uint8Array>>();

// Only omit pixels hidden by proven opaque texels; Pixi composites every survivor.
export function cullOpaqueSprites(
    display: OctaneContainer,
    viewport: { x: number; y: number; cropWidth: number; cropHeight: number }
): () => void {
    const skipped: OctaneSprite[] = [];
    const restore = () => {
        for (const sprite of skipped) sprite.renderable = true;
    };
    // Children are already in RoomSpriteCanvas painter order.
    // Unknown spatial filters could read pixels outside their sprite and invalidate local coverage.
    if (
        !['normal', 'inherit'].includes(display.blendMode) ||
        !display.parent ||
        !['normal', 'inherit'].includes(display.parent.blendMode) ||
        display.sortableChildren ||
        display.parent.filters?.some((filter) => !(filter instanceof OctaneAdjustmentFilter)) ||
        display.filters?.length ||
        display.children.some((s) => s.filters?.length || s.mask || s.children?.length)
    )
        return restore;
    const master = display.parent?.getGlobalTransform();
    if (!master || master.a !== 1 || master.d !== 1 || master.b || master.c || master.tx || master.ty) return restore;
    let readPixels = 0;
    let maskPixels = 0;
    const stride = Math.ceil(viewport.cropWidth / 32);
    const covered = new Uint32Array(stride * viewport.cropHeight);
    const sources = new Map<OctaneTexture, Uint8Array>();
    // Repeated sprites share clipped support/opaque bitsets for this capture only.
    const placements = new Map<string, number[]>();
    const ids = new Map<OctaneTexture, number>();
    try {
        for (let index = display.children.length - 1; index >= 0; index--) {
            const sprite = display.children[index];
            if (
                !(sprite instanceof OctaneSprite) ||
                !sprite.visible ||
                !sprite.renderable ||
                !sprite.texture ||
                !['normal', 'inherit'].includes(sprite.blendMode)
            )
                continue;
            const texture = sprite.texture,
                source = texture.source,
                resource = source?.resource;
            const m = sprite.getGlobalTransform(),
                f = texture.frame;
            if (
                !(resource instanceof ImageBitmap) ||
                source.scaleMode !== 'nearest' ||
                source.resolution !== 1 ||
                texture.rotate ||
                texture.trim ||
                sprite.anchor.x ||
                sprite.anchor.y ||
                Math.abs(m.a) !== 1 ||
                Math.abs(m.d) !== 1 ||
                m.b !== 0 ||
                m.c !== 0 ||
                !Number.isInteger(m.tx) ||
                !Number.isInteger(m.ty) ||
                !Number.isInteger(f.x) ||
                !Number.isInteger(f.y) ||
                !Number.isInteger(f.width) ||
                !Number.isInteger(f.height) ||
                f.x < 0 ||
                f.y < 0 ||
                f.width <= 0 ||
                f.height <= 0 ||
                f.right > resource.width ||
                f.bottom > resource.height ||
                texture.orig.width !== f.width ||
                texture.orig.height !== f.height ||
                resource.width !== source.width ||
                resource.height !== source.height
            )
                continue;
            if (!ids.has(texture)) ids.set(texture, ids.size);
            const x = m.tx - viewport.x,
                y = m.ty - viewport.y;
            if (!Number.isInteger(x) || !Number.isInteger(y)) continue;
            // Reject invisible or already covered bounds before decoding any frame.
            const minX = Math.max(0, x + Math.min(0, m.a * f.width)),
                maxX = Math.min(viewport.cropWidth, x + Math.max(0, m.a * f.width));
            const minY = Math.max(0, y + Math.min(0, m.d * f.height)),
                maxY = Math.min(viewport.cropHeight, y + Math.max(0, m.d * f.height));
            if (minX >= maxX || minY >= maxY) {
                sprite.renderable = false;
                skipped.push(sprite);
                continue;
            }
            let boxCovered = true;
            for (let row = minY; row < maxY && boxCovered; row++)
                for (let col = minX; col < maxX; ) {
                    const last = Math.min(maxX - 1, col | 31);
                    const bits = (0xffffffff << (col & 31)) & (0xffffffff >>> (31 - (last & 31)));
                    if ((bits & ~covered[row * stride + (col >>> 5)]) !== 0) {
                        boxCovered = false;
                        break;
                    }
                    col = last + 1;
                }
            if (boxCovered) {
                sprite.renderable = false;
                skipped.push(sprite);
                continue;
            }
            const key = `${ids.get(texture)},${x},${y},${m.a},${m.d}`;
            let words = placements.get(key);
            if (!words) {
                const count = (maxX - minX) * (maxY - minY);
                if (maskPixels + count > MAX_MASK_PIXELS) continue;
                maskPixels += count;
                let alpha = sources.get(texture);
                if (!alpha) {
                    const frameKey = `${f.x},${f.y},${f.width},${f.height}`;
                    let cachedFrames = pixelsByImage.get(resource);
                    alpha = cachedFrames?.get(frameKey);
                    if (!alpha) {
                        const size = f.width * f.height;
                        if (size > MAX_FRAME_PIXELS || readPixels + size > MAX_READ_PIXELS) continue;
                        readPixels += size;
                        const canvas = new OffscreenCanvas(f.width, f.height);
                        const context = canvas.getContext('2d', { willReadFrequently: true });
                        if (!context) continue;
                        context.imageSmoothingEnabled = false;
                        context.drawImage(resource, f.x, f.y, f.width, f.height, 0, 0, f.width, f.height);
                        const rgba = context.getImageData(0, 0, f.width, f.height).data;
                        alpha = new Uint8Array(size);
                        for (let pixel = 0; pixel < size; pixel++) alpha[pixel] = rgba[pixel * 4 + 3];
                        // Immutable bitmap + frame coordinates avoid stale alpha after asset replacement.
                        if (cachedPixelBytes + size <= MAX_CACHED_BYTES) {
                            if (!cachedFrames) pixelsByImage.set(resource, (cachedFrames = new Map()));
                            cachedFrames.set(frameKey, alpha);
                            cachedPixelBytes += size;
                        }
                    }
                    sources.set(texture, alpha);
                }
                const support = new Uint32Array(covered.length),
                    opaque = new Uint32Array(covered.length);
                // A flipped integer pixel spans [translation - pixel - 1, translation - pixel).
                for (let ty = minY; ty < maxY; ty++) {
                    const sy = m.d === 1 ? ty - y : y - ty - 1;
                    const offset = sy * f.width;
                    for (let tx = minX; tx < maxX; tx++) {
                        const sx = m.a === 1 ? tx - x : x - tx - 1;
                        const value = alpha[offset + sx];
                        if (!value) continue;
                        const at = ty * stride + (tx >>> 5),
                            bit = 1 << (tx & 31);
                        support[at] |= bit;
                        if (value === 255) opaque[at] |= bit;
                    }
                }
                words = [];
                for (let at = minY * stride; at < maxY * stride; at++) if (support[at]) words.push(at, support[at], opaque[at]);
                placements.set(key, words);
            }
            let hidden = true;
            for (let at = 0; at < words.length; at += 3)
                if ((words[at + 1] & ~covered[words[at]]) !== 0) {
                    hidden = false;
                    break;
                }
            if (hidden) {
                sprite.renderable = false;
                skipped.push(sprite);
                continue;
            }
            // Alpha/tint/blend stay with Pixi. Only an opaque normal texel hides everything behind it.
            if (sprite.getGlobalAlpha() === 1) for (let at = 0; at < words.length; at += 3) covered[words[at]] |= words[at + 2];
        }
    } catch {
        // A closed bitmap or unavailable readback must not leave a partially culled photo.
        restore();
    }
    return restore;
}
