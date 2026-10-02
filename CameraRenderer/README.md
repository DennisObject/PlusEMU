# Trusted room camera

The emulator supplies a room snapshot. The browser receives only a viewpoint and effect selections from the player; it never submits PNG bytes or room objects. A shutter snapshot stays immutable for editor renders. Checkout accepts only a media GUID minted by that session while it is still in the original room.

Apply `Resources/SQLs/Updates/15_TrustedCamera.sql` once before enabling the camera. It is safe to rerun and preserves existing settings. Configure `Camera` in `Config/config.json`; keep its case, especially bearer and effect names. An empty bearer rejects captures. Prices use credits and duckets; other point types fail closed. Competition defaults to disabled and fails closed if email verification is required because Plus has no verified-email flag.

Build with the sibling `Octane-Renderer` checkout and its dependencies installed:

```sh
cd CameraRenderer
npm ci
npm run build
npx playwright install chromium
CAMERA_RENDERER_SECRET='<a private random secret of at least 32 characters>' node server.mjs
```

Keep the service private. It defaults to `127.0.0.1:3921`; use a private Compose hostname for containers. `Camera.RendererUrl` must be an HTTP private endpoint ending in `/render`. Redirects and public DNS addresses are refused. The emulator bearer must match `CAMERA_RENDERER_SECRET` and must never enter client configuration.

Set `CAMERA_RENDERER_CONFIG` and `CAMERA_UI_CONFIG` to renderer and UI configuration files. The catalogue comes from `camera.available.effects`; names must also appear in the emulator's `Camera.Effects` allowlist. Empty catalogues permit no-effect captures. The service replaces all asset URLs with its private routes and blocks game sockets and outside requests. Missing scene assets reject the render.

Optional environment variables: `CAMERA_ASSET_ROOT`, `CAMERA_IMAGES_ROOT`, `CAMERA_MEDIA_ROOT`, `CAMERA_CHROMIUM_PATH`, `CAMERA_RENDERER_HOST`, `CAMERA_RENDERER_PORT`, and `CAMERA_RENDERER_ORIGIN`. Asset defaults match the sibling hotel assets. Point `CAMERA_MEDIA_ROOT` at the emulator's `Camera.OutputDirectory` if minted media is shared.

Serve that output directory at `/camera/` with GET/HEAD only, no directory listing and `X-Content-Type-Options: nosniff`. Photos are `<guid>.png` (320×320) and `<guid>_small.png` (110×110). Room thumbnails are `thumbnail/<room-id>.png` (110×110); configure the client's `thumbnails.url` as `/camera/thumbnail/%thumbnail%.png`. Thumbnail responses must allow refresh after replacement. Public image origins and paths cannot be chosen by camera packets.

Checks: `npm test`, `npm run build`, `dotnet test Plus.Tests`. Camera database facts require `PLUS_CAMERA_TEST_CONNECTION_STRING` pointing at a disposable `task_camera_tests_` schema containing the camera migration and minimal user/item tables. They verify purchases, rollback, publication/competition idempotency and persistent quotas.
