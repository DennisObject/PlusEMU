import
{
    AvatarAction,
    FloorHeightMapMessageParser,
    FurnitureStackingHeightMap,
    GetAssetManager,
    GetAvatarRenderManager,
    GetConfiguration,
    GetRenderer,
    GetRoomCameraWidgetManager,
    GetRoomContentLoader,
    GetRoomEngine,
    GetRoomManager,
    GetSessionDataManager,
    GetTicker,
    GetEventDispatcher,
    LegacyDataType,
    LegacyWallGeometry,
    OctaneRectangle,
    OctaneSprite,
    OctaneTexture,
    PrepareRenderer,
    RoomCameraWidgetSelectedEffect,
    RoomContentLoadedEvent,
    RoomContentLoader,
    RoomGeometry,
    RoomInstance,
    RoomObjectCategory,
    RoomObjectUserType,
    RoomObjectVariable,
    RoomPlaneParser,
    Vector3d,
    loadGamedata
} from '@octane/renderer';

interface CameraViewport
{
    width: number;
    height: number;
    offsetX: number;
    offsetY: number;
    x: number;
    y: number;
    cropWidth: number;
    cropHeight: number;
    scale: number;
    locationX: number;
    locationY: number;
    locationZ: number;
}

interface CameraEffectSelection
{
    name: string;
    strength: number;
}

interface CameraSceneItem
{
    id: number;
    spriteId: number;
    type: 's' | 'i';
    x: number;
    y: number;
    z: number;
    direction: number;
    state: number;
    wallPosition: string;
    extraData: string;
}

interface CameraSceneUser
{
    id: number;
    roomIndex: number;
    figure: string;
    gender: string;
    type: number;
    x: number;
    y: number;
    z: number;
    direction: number;
    headDirection: number;
    posture: string;
    postureParameter: string;
    gesture: number | string;
    effect: number;
    handItem: number;
    dance: number;
}

interface CameraScene
{
    roomId: number;
    heightmap: string;
    door: { x: number, y: number, z: number, direction: number };
    wallHeight: number;
    wallThickness: number;
    floorThickness: number;
    hideWalls: boolean;
    floor: string;
    wallpaper: string;
    landscape: string;
    backgroundColor: number;
    items: CameraSceneItem[];
    users: CameraSceneUser[];
}

interface CameraJob
{
    scene: CameraScene;
    viewport: CameraViewport;
    effects: CameraEffectSelection[];
    zoom: boolean;
    level: number;
}

interface CameraCatalogueEntry
{
    name: string;
    minLevel: number;
    type: string;
}

const CANVAS_ID = 1;
const READY_BUDGET_MS = 14000;
const MEDIA_SCHEME = /(?:https?:|data:|blob:|javascript:|file:|\/\/)/i;
const WALL_POSITION = /^:w=(-?\d+),(-?\d+)\s+l=(-?\d+),(-?\d+)\s+([lr])$/i;
const REQUIRED_CONFIGURATION = [
    'furnidata.url',
    'productdata.url',
    'avatar.actions.url',
    'avatar.figuredata.url',
    'avatar.figuremap.url',
    'avatar.effectmap.url',
    'avatar.asset.url',
    'avatar.asset.effect.url',
    'furni.asset.url',
    'image.library.url'
];

let startupError: Error = null;
let effectLibraries: Map<string, string[]> = new Map();
let renderQueue: Promise<void> = Promise.resolve();

declare global
{
    interface Window
    {
        cameraConfiguration?: { [index: string]: unknown };
        cameraReady: boolean;
        cameraEffects: readonly CameraCatalogueEntry[];
        cameraRender: (job: CameraJob) => Promise<string>;
    }
}

window.cameraReady = false;
window.cameraEffects = Object.freeze([]);
window.cameraRender = async () =>
{
    throw startupError ?? new Error('Camera renderer is not ready');
};

function fail(message: string): never
{
    throw new Error(message);
}

function integer(value: unknown, label: string): number
{
    if(typeof value !== 'number' || !Number.isInteger(value)) fail(`Invalid ${ label }`);

    return value;
}

function finite(value: unknown, label: string): number
{
    if(typeof value !== 'number' || !Number.isFinite(value)) fail(`Invalid ${ label }`);

    return value;
}

function assertNoUrls(value: unknown): void
{
    if(typeof value === 'string')
    {
        if(value.includes('\0') || MEDIA_SCHEME.test(value)) fail('URL in scene');

        return;
    }

    if(Array.isArray(value))
    {
        value.forEach(assertNoUrls);

        return;
    }

    if(value && (typeof value === 'object')) Object.values(value).forEach(assertNoUrls);
}

function facing(value: number): number
{
    if(!Number.isFinite(value)) fail('Invalid direction');

    if(Number.isInteger(value) && (value >= 0) && (value <= 7)) return value * 45;

    return value;
}

function extraText(value: unknown): string
{
    if(typeof value === 'number' && Number.isFinite(value)) return String(value);

    if(typeof value !== 'string' || !value) return '';

    if(value.includes('\0') || MEDIA_SCHEME.test(value) || /THUMBNAIL_URL/i.test(value)) return '';

    return value;
}

function readJob(job: CameraJob): CameraJob
{
    if(!job || typeof job !== 'object') fail('Invalid render job');

    const viewport = job.viewport;

    if(!viewport) fail('Invalid viewport');

    const width = integer(viewport.width, 'viewport width');
    const height = integer(viewport.height, 'viewport height');
    const cropWidth = integer(viewport.cropWidth, 'crop width');
    const cropHeight = integer(viewport.cropHeight, 'crop height');

    if((viewport.scale !== 1) || (width < 320) || (height < 320) || (width > 2048) || (height > 2048) || ((cropWidth !== 110) && (cropWidth !== 320)) || (cropWidth !== cropHeight)) fail('Invalid viewport dimensions');

    const offsetX = finite(viewport.offsetX, 'offset');
    const offsetY = finite(viewport.offsetY, 'offset');
    const x = finite(viewport.x, 'crop');
    const y = finite(viewport.y, 'crop');
    const locationX = finite(viewport.locationX, 'location');
    const locationY = finite(viewport.locationY, 'location');
    const locationZ = finite(viewport.locationZ, 'location');

    if([x, y, offsetX, offsetY].some(value => Math.abs(value) > 4096) || [locationX, locationY, locationZ].some(value => Math.abs(value) > 256)) fail('Invalid viewport position');

    if((typeof job.zoom !== 'boolean') || ((cropWidth === 110) && job.zoom)) fail('Invalid zoom');

    const level = integer(job.level, 'level');

    if((level < 0) || (level > 1000) || !Array.isArray(job.effects) || (job.effects.length > 8)) fail('Invalid effects');

    const scene = job.scene;

    if(!scene || !Number.isInteger(scene.roomId) || (scene.roomId <= 0) || (typeof scene.heightmap !== 'string') || (scene.heightmap.length > 65536) || !Array.isArray(scene.items) || (scene.items.length > 5000) || !Array.isArray(scene.users) || (scene.users.length > 1000)) fail('Invalid server scene');

    assertNoUrls(scene);

    const names = new Set<string>();

    const effects = job.effects.map(selection =>
    {
        if(!selection || (typeof selection.name !== 'string') || !Number.isFinite(selection.strength) || (selection.strength < 0) || (selection.strength > 1) || names.has(selection.name)) fail('Invalid effect selection');

        names.add(selection.name);

        return { name: selection.name, strength: selection.strength };
    });

    return {
        scene,
        viewport: { width, height, offsetX, offsetY, x, y, cropWidth, cropHeight, scale: 1, locationX, locationY, locationZ },
        effects,
        zoom: job.zoom,
        level
    };
}

function userType(value: unknown): number
{
    if(typeof value === 'number' && (value >= 1) && (value <= 4)) return value;

    if(typeof value === 'string')
    {
        const type = RoomObjectUserType.getTypeNumber(value);

        if(type) return type;
    }

    fail('Invalid user type');
}

function buildRoom(scene: CameraScene): { floorItems: CameraSceneItem[], wallItems: CameraSceneItem[] }
{
    const engine = GetRoomEngine();
    const model = scene.heightmap.replace(/\r\n/g, '\r').replace(/\n/g, '\r');
    const parser = new FloorHeightMapMessageParser();

    if(!parser.parseModel(model, finite(scene.wallHeight, 'wall height'), true) || !parser.width || !parser.height) fail('Invalid heightmap');

    const wallGeometry = engine.getLegacyWallGeometry(scene.roomId);

    if(!wallGeometry) fail('Wall geometry was not created');

    const planeParser = new RoomPlaneParser();
    const width = parser.width;
    const height = parser.height;
    const door = scene.door;

    if(!door) fail('Invalid door');

    const entryX = Math.floor(finite(door.x, 'door'));
    const entryY = Math.floor(finite(door.y, 'door'));

    planeParser.initializeTileMap(width, height);

    let doorX = -1;
    let doorY = -1;
    let doorZ = 0;
    let doorDirection = 0;

    for(let y = 0; y < height; y++)
    {
        for(let x = 0; x < width; x++)
        {
            const tileHeight = parser.getHeight(x, y);
            const entryMatches = (x === entryX) && (y === entryY);

            if(((((y > 0) && (y < (height - 1))) || ((x > 0) && (x < (width - 1)))) && (tileHeight !== RoomPlaneParser.TILE_BLOCKED)) && entryMatches)
            {
                if(((parser.getHeight(x, (y - 1)) === RoomPlaneParser.TILE_BLOCKED) && (parser.getHeight((x - 1), y) === RoomPlaneParser.TILE_BLOCKED)) && (parser.getHeight(x, (y + 1)) === RoomPlaneParser.TILE_BLOCKED))
                {
                    doorX = (x + 0.5);
                    doorY = y;
                    doorZ = tileHeight;
                    doorDirection = 90;
                }

                if(((parser.getHeight(x, (y - 1)) === RoomPlaneParser.TILE_BLOCKED) && (parser.getHeight((x - 1), y) === RoomPlaneParser.TILE_BLOCKED)) && (parser.getHeight((x + 1), y) === RoomPlaneParser.TILE_BLOCKED))
                {
                    doorX = x;
                    doorY = (y + 0.5);
                    doorZ = tileHeight;
                    doorDirection = 180;
                }
            }

            planeParser.setTileHeight(x, y, tileHeight);
        }
    }

    if(doorX < 0)
    {
        const tileHeight = parser.getHeight(entryX, entryY);

        if(tileHeight === RoomPlaneParser.TILE_BLOCKED) fail('Door is not on a room tile');

        const rotation = finite(door.direction, 'door direction');

        doorZ = tileHeight;

        if((rotation === 4) || (rotation === 180))
        {
            doorDirection = 180;
            doorX = entryX;
            doorY = (entryY + 0.5);
        }
        else
        {
            doorDirection = 90;
            doorX = (entryX + 0.5);
            doorY = entryY;
        }
    }

    planeParser.setTileHeight(Math.floor(doorX), Math.floor(doorY), doorZ);

    if(!planeParser.initializeFromTileData(parser.wallHeight)) fail('Room planes could not be built');

    planeParser.setTileHeight(Math.floor(doorX), Math.floor(doorY), (doorZ + planeParser.wallHeight));

    wallGeometry.scale = LegacyWallGeometry.DEFAULT_SCALE;
    wallGeometry.initialize(width, height, planeParser.floorHeight);

    for(let y = (height - 1); y >= 0; y--)
    {
        for(let x = (width - 1); x >= 0; x--) wallGeometry.setHeight(x, y, planeParser.getTileHeight(x, y));
    }

    const roomMap = planeParser.getMapData();

    roomMap.doors.push({ x: doorX, y: doorY, z: doorZ, dir: doorDirection });

    const stacking = new FurnitureStackingHeightMap(width, height);

    for(let y = 0; y < height; y++)
    {
        for(let x = 0; x < width; x++)
        {
            const tileHeight = parser.getHeight(x, y);
            const isRoomTile = tileHeight !== RoomPlaneParser.TILE_BLOCKED;

            stacking.setTileHeight(x, y, isRoomTile ? tileHeight : 0);
            stacking.setStackingBlocked(x, y, false);
            stacking.setIsRoomTile(x, y, isRoomTile);
        }
    }

    engine.setFurnitureStackingHeightMap(scene.roomId, stacking);
    engine.updateRoomInstancePlaneType(scene.roomId, scene.floor || null, scene.wallpaper || null, scene.landscape || null);

    const floorItems: CameraSceneItem[] = [];
    const wallItems: CameraSceneItem[] = [];
    const session = GetSessionDataManager();
    const content = GetRoomContentLoader();

    for(const item of scene.items)
    {
        const id = integer(item?.id, 'item id');
        const spriteId = integer(item?.spriteId, 'sprite id');

        if(id < 1) fail('Invalid item id');

        const wall = item.type === 'i';

        if(!wall && (item.type !== 's')) fail('Invalid item type');

        const furniture = wall ? session.getWallItemData(spriteId) : session.getFloorItemData(spriteId);

        if(!furniture) fail(`Missing furniture data ${ spriteId }`);

        // Camera photos are external-image wall items. Omit them until server-minted media is trusted.
        if(furniture.isExternalImage || furniture.className.includes('external_image')) continue;

        const extra = extraText(item.extraData);
        const typeName = wall ? content.getFurnitureWallNameForTypeId(spriteId, extra) : content.getFurnitureFloorNameForTypeId(spriteId);

        if(!typeName) fail(`Missing furniture library ${ spriteId }`);

        if(wall) wallItems.push({ ...item, id, spriteId, extraData: extra, wallPosition: item.wallPosition });
        else floorItems.push({ ...item, id, spriteId, extraData: extra, wallPosition: item.wallPosition });
    }

    engine.createRoomInstance(scene.roomId, roomMap);

    if(!engine.getRoomInstance(scene.roomId)) fail('Room instance was not created');

    engine.updateRoomInstancePlaneThickness(scene.roomId, finite(scene.wallThickness, 'wall thickness'), finite(scene.floorThickness, 'floor thickness'));
    engine.updateRoomInstancePlaneVisibility(scene.roomId, !scene.hideWalls, true);
    engine.updateObjectRoomColor(scene.roomId, finite(scene.backgroundColor, 'background'), 255, false);

    return { floorItems, wallItems };
}

function mountDisplay(scene: CameraScene, viewport: CameraViewport): void
{
    const engine = GetRoomEngine();
    const renderer = GetRenderer();

    if(renderer) renderer.resize(viewport.width, viewport.height, 1);

    const master = engine.getRoomInstanceDisplay(scene.roomId, CANVAS_ID, viewport.width, viewport.height, RoomGeometry.SCALE_ZOOMED_IN);

    if(!master) fail('Room display was not created');

    engine.initializeRoomInstanceRenderingCanvas(scene.roomId, CANVAS_ID, viewport.width, viewport.height);

    const geometry = engine.getRoomInstanceGeometry(scene.roomId, CANVAS_ID) as RoomGeometry;

    if(!geometry) fail('Room geometry was not created');

    geometry.location = new Vector3d(viewport.locationX, viewport.locationY, viewport.locationZ);
    engine.setRoomInstanceRenderingCanvasOffset(scene.roomId, CANVAS_ID, { x: viewport.offsetX, y: viewport.offsetY } as never);

    const background = new OctaneSprite(OctaneTexture.WHITE);

    background.tint = 0;
    background.width = viewport.width;
    background.height = viewport.height;
    master.addChildAt(background, 0);
    engine.setActiveRoomId(scene.roomId);
}

function placeFurniture(scene: CameraScene, floorItems: CameraSceneItem[], wallItems: CameraSceneItem[]): void
{
    const engine = GetRoomEngine();
    const wallGeometry = engine.getLegacyWallGeometry(scene.roomId);

    if(!wallGeometry) fail('Wall geometry is missing');

    for(const item of floorItems)
    {
        const data = new LegacyDataType();

        data.setString(item.extraData);

        if(!engine.addFurnitureFloor(scene.roomId, item.id, item.spriteId, new Vector3d(finite(item.x, 'item'), finite(item.y, 'item'), finite(item.z, 'item')), new Vector3d(facing(item.direction)), Number.isFinite(item.state) ? Math.trunc(item.state) : 0, data)) fail(`Could not place floor item ${ item.id }`);
    }

    for(const item of wallItems)
    {
        const match = WALL_POSITION.exec((item.wallPosition || '').trim());

        if(!match) fail(`Invalid wall position for item ${ item.id }`);

        const location = wallGeometry.getLocation(Number(match[1]), Number(match[2]), Number(match[3]), Number(match[4]), match[5].toLowerCase());

        if(!location) fail(`Invalid wall location for item ${ item.id }`);

        if(!engine.addFurnitureWall(scene.roomId, item.id, item.spriteId, location, new Vector3d(wallGeometry.getDirection(match[5].toLowerCase())), Number.isFinite(item.state) ? Math.trunc(item.state) : 0, item.extraData)) fail(`Could not place wall item ${ item.id }`);
    }
}

function placeUsers(scene: CameraScene): CameraSceneUser[]
{
    const engine = GetRoomEngine();
    const users: CameraSceneUser[] = [];

    for(const user of scene.users)
    {
        const roomIndex = integer(user?.roomIndex, 'room index');

        if(roomIndex < 0) fail('Invalid room index');

        const type = userType(user.type);
        const figure = typeof user.figure === 'string' ? user.figure : '';
        const placed: CameraSceneUser = {
            ...user,
            roomIndex,
            type,
            figure,
            gender: typeof user.gender === 'string' ? user.gender : '',
            posture: typeof user.posture === 'string' ? user.posture : '',
            postureParameter: typeof user.postureParameter === 'string' ? user.postureParameter : '',
            effect: Number.isFinite(user.effect) ? user.effect : 0,
            handItem: Number.isFinite(user.handItem) ? user.handItem : 0,
            dance: Number.isFinite(user.dance) ? user.dance : 0
        };

        if(!engine.addRoomObjectUser(scene.roomId, roomIndex, new Vector3d(finite(user.x, 'user'), finite(user.y, 'user'), finite(user.z, 'user')), new Vector3d(facing(user.direction)), facing(user.headDirection), type, figure)) fail(`Could not place user ${ roomIndex }`);

        if(figure) engine.updateRoomObjectUserFigure(scene.roomId, roomIndex, figure, placed.gender || null);

        if(placed.posture) engine.updateRoomObjectUserPosture(scene.roomId, roomIndex, placed.posture, placed.postureParameter);

        if(placed.effect) engine.updateRoomObjectUserEffect(scene.roomId, roomIndex, placed.effect, 0);

        if(placed.dance) engine.updateRoomObjectUserAction(scene.roomId, roomIndex, RoomObjectVariable.FIGURE_DANCE, placed.dance);

        if(placed.handItem) engine.updateRoomObjectUserAction(scene.roomId, roomIndex, RoomObjectVariable.FIGURE_CARRY_OBJECT, placed.handItem);

        users.push(placed);
    }

    return users;
}

function applyGestures(roomId: number, users: CameraSceneUser[]): void
{
    const engine = GetRoomEngine();

    for(const user of users)
    {
        if((user.gesture == null) || (user.gesture === '') || (user.gesture === 0)) continue;

        if(typeof user.gesture === 'string')
        {
            const gestureId = AvatarAction.getGestureId(user.gesture);

            if(gestureId > 0)
            {
                engine.updateRoomObjectUserGesture(roomId, user.roomIndex, gestureId);

                continue;
            }

            if(user.type === RoomObjectUserType.getTypeNumber(RoomObjectUserType.PET))
            {
                engine.updateRoomObjectUserPetGesture(roomId, user.roomIndex, user.gesture);

                continue;
            }

            fail(`Unknown gesture ${ user.gesture }`);
        }

        if(user.gesture > 0) engine.updateRoomObjectUserGesture(roomId, user.roomIndex, user.gesture);
    }
}

function stripAdvertisements(roomId: number): void
{
    const engine = GetRoomEngine();

    for(const category of [RoomObjectCategory.FLOOR, RoomObjectCategory.WALL])
    {
        for(const object of engine.getRoomObjects(roomId, category))
        {
            object?.model?.setValue(RoomObjectVariable.FURNITURE_AD_URL, '');
        }
    }
}

function missingObjects(roomId: number): string[]
{
    const engine = GetRoomEngine();
    const names: string[] = [];

    for(const category of [RoomObjectCategory.ROOM, RoomObjectCategory.FLOOR, RoomObjectCategory.WALL, RoomObjectCategory.UNIT, RoomObjectCategory.CURSOR])
    {
        for(const object of engine.getRoomObjects(roomId, category))
        {
            if(object && !object.isReady) names.push(object.type || 'unknown');
        }
    }

    return names;
}

function effectsReady(users: CameraSceneUser[]): boolean
{
    const assets = GetAssetManager();

    for(const user of users)
    {
        if(!user.effect) continue;

        for(const library of (effectLibraries.get(String(user.effect)) ?? []))
        {
            if(!assets.getCollection(library)) return false;
        }
    }

    return true;
}

async function pump(engineTime: { value: number }): Promise<void>
{
    const engine = GetRoomEngine();
    const ticker = GetTicker();

    // processPendingFurniture drops the next call after a 40ms batch, so each pump runs two updates.
    for(let step = 0; step < 2; step++)
    {
        engineTime.value = Math.max(engineTime.value, ticker.lastTime) + 50;
        ticker.update(engineTime.value);
        engine.update(ticker);
    }

    await new Promise(resolve => setTimeout(resolve, 0));
}

async function waitUntilReady(roomId: number, users: CameraSceneUser[], floorCount: number, wallCount: number, failed: string[]): Promise<void>
{
    const engine = GetRoomEngine();
    const avatars = GetAvatarRenderManager();
    const figures = users.filter(user => user.figure).map(user => avatars.createFigureContainer(user.figure));
    const engineTime = { value: 0 };
    const deadline = performance.now() + READY_BUDGET_MS;

    while(performance.now() < deadline)
    {
        if(failed.length) fail(`Missing library ${ failed[0] }`);

        await pump(engineTime);

        const instance = engine.getRoomInstance(roomId) as RoomInstance;

        if(!instance) fail('Room was destroyed');

        const figuresReady = figures.every(figure => avatars.isFigureContainerReady(figure));
        const countsMatch = (engine.getRoomObjects(roomId, RoomObjectCategory.FLOOR).length === floorCount) && (engine.getRoomObjects(roomId, RoomObjectCategory.WALL).length === wallCount);

        if(!instance.hasUninitializedObjects() && figuresReady && countsMatch && effectsReady(users)) return;
    }

    if(failed.length) fail(`Missing library ${ failed[0] }`);

    const missing = missingObjects(roomId);

    if(missing.length) fail(`Missing library ${ missing.join(', ') }`);

    if(!figures.every(figure => avatars.isFigureContainerReady(figure))) fail('Avatar figure library was not ready');

    if(!effectsReady(users)) fail('Avatar effect library was not ready');

    const floorNow = engine.getRoomObjects(roomId, RoomObjectCategory.FLOOR).length;
    const wallNow = engine.getRoomObjects(roomId, RoomObjectCategory.WALL).length;

    fail(`Room was not ready (${ floorNow }/${ floorCount } floor, ${ wallNow }/${ wallCount } wall)`);
}

async function encodeCrop(roomId: number, viewport: CameraViewport, effects: CameraEffectSelection[], zoom: boolean, level: number): Promise<string>
{
    const engine = GetRoomEngine();
    const camera = GetRoomCameraWidgetManager();
    const texture = engine.createTextureFromRoom(roomId, CANVAS_ID, new OctaneRectangle(viewport.x, viewport.y, viewport.cropWidth, viewport.cropHeight));

    if(!texture) fail('Room texture was not created');

    try
    {
        const selected = effects.map(selection =>
        {
            const effect = camera.effects.get(selection.name);

            if(!effect || (effect.minLevel > level)) fail(`Unknown camera effect ${ selection.name }`);

            return new RoomCameraWidgetSelectedEffect(effect, selection.strength);
        });
        const image = await camera.applyEffects(texture, selected, zoom);

        if(!image) fail('Camera effects produced no image');

        if(typeof image.decode === 'function') await image.decode().catch(() => undefined);

        const width = image.naturalWidth || image.width;
        const height = image.naturalHeight || image.height;

        if((width !== viewport.cropWidth) || (height !== viewport.cropHeight)) fail(`Rendered image is ${ width }x${ height }, expected ${ viewport.cropWidth }x${ viewport.cropHeight }`);

        const canvas = document.createElement('canvas');
        const context = canvas.getContext('2d');

        if(!context) fail('Canvas is unavailable');

        canvas.width = viewport.cropWidth;
        canvas.height = viewport.cropHeight;
        context.drawImage(image, 0, 0);

        const encoded = canvas.toDataURL('image/png');
        const payload = encoded.slice(encoded.indexOf(',') + 1);

        if(!payload || encoded.startsWith('data:image/png;base64,') === false) fail('PNG encoding failed');

        return payload;
    }
    finally
    {
        if(!texture.destroyed) texture.destroy(true);
    }
}

async function renderRoom(job: CameraJob): Promise<string>
{
    const requested = readJob(job);
    const engine = GetRoomEngine();
    const roomId = requested.scene.roomId;
    const failed: string[] = [];
    const onFailure = (event: RoomContentLoadedEvent) =>
    {
        if(event?.contentType) failed.push(event.contentType);
    };
    const onRejection = (event: PromiseRejectionEvent) =>
    {
        const message = (event.reason instanceof Error) ? event.reason.message : String(event.reason ?? '');

        if(/download|could not load|missing library/i.test(message)) failed.push(message);
    };
    let opened = false;

    GetEventDispatcher().addEventListener<RoomContentLoadedEvent>(RoomContentLoadedEvent.RCLE_FAILURE, onFailure);
    window.addEventListener('unhandledrejection', onRejection);

    try
    {
        engine.destroyRoom(roomId);
        engine.getLegacyWallGeometry(roomId);
        opened = true;

        const placed = buildRoom(requested.scene);

        mountDisplay(requested.scene, requested.viewport);
        placeFurniture(requested.scene, placed.floorItems, placed.wallItems);

        const users = placeUsers(requested.scene);

        await waitUntilReady(roomId, users, placed.floorItems.length, placed.wallItems.length, failed);
        stripAdvertisements(roomId);
        applyGestures(roomId, users);

        const engineTime = { value: GetTicker().lastTime };

        for(let frame = 0; frame < 3; frame++) await pump(engineTime);

        if(failed.length) fail(`Missing library ${ failed[0] }`);

        return await encodeCrop(roomId, requested.viewport, requested.effects, requested.zoom, requested.level);
    }
    finally
    {
        GetEventDispatcher().removeEventListener(RoomContentLoadedEvent.RCLE_FAILURE, onFailure);
        window.removeEventListener('unhandledrejection', onRejection);

        if(opened) engine.destroyRoom(roomId);
    }
}

function enqueue(job: CameraJob): Promise<string>
{
    const run = renderQueue.then(() => renderRoom(job));

    renderQueue = run.then(() => undefined, () => undefined);

    return run;
}

async function loadEffectLibraries(): Promise<Map<string, string[]>>
{
    const url = GetConfiguration().getValue<string>('avatar.effectmap.url');
    const data = await loadGamedata<{ effects?: { id?: unknown, lib?: unknown }[] }>(url);
    const libraries = new Map<string, string[]>();
    const seen = new Set<string>();

    for(const effect of (data?.effects ?? []))
    {
        if(!effect || (effect.id == null) || (effect.lib == null)) continue;

        const library = String(effect.lib);

        if(seen.has(library)) continue;

        seen.add(library);
        libraries.set(String(effect.id), [library]);
    }

    return libraries;
}

function requireConfiguration(): void
{
    const configuration = window.cameraConfiguration;

    if(!configuration || (typeof configuration !== 'object')) fail('Trusted camera configuration is missing');

    const config = GetConfiguration();

    config.parseConfiguration(configuration, true);

    if(!Array.isArray(config.getValue('avatar.mandatory.effect.libraries'))) config.setValue('avatar.mandatory.effect.libraries', []);

    config.setValue('room.color.skip.transition', true);
    config.setValue('room.camera.follow_user', false);
    config.setValue('system.log.packets', false);

    if(!Array.isArray(config.getValue('camera.available.effects'))) config.setValue('camera.available.effects', []);

    for(const key of REQUIRED_CONFIGURATION)
    {
        const value = config.getValue(key);

        if((value == null) || (value === '')) fail(`Missing configuration ${ key }`);
    }
}

function requireMandatoryLibraries(): void
{
    const content = GetRoomContentLoader();

    for(const name of RoomContentLoader.MANDATORY_LIBRARIES)
    {
        const url = content.getAssetUrls(name)?.[0];

        if(!url) continue;

        if(!content.getCollection(name)) fail(`Missing mandatory library ${ name }`);
    }
}

async function start(): Promise<void>
{
    requireConfiguration();

    await PrepareRenderer({
        width: 320,
        height: 320,
        resolution: 1,
        backgroundAlpha: 0,
        preference: 'webgl',
        autoDensity: false
    });

    const ticker = GetTicker();

    // RoomSpriteCanvas paints from Ticker.deltaTime and draws nothing while the shared ticker is stalled at 0.
    ticker.autoStart = false;
    ticker.stop();

    await Promise.all([
        GetAvatarRenderManager().init(),
        GetSessionDataManager().init()
    ]);

    const [libraries] = await Promise.all([
        loadEffectLibraries(),
        GetRoomContentLoader().init(),
        GetRoomCameraWidgetManager().init()
    ]);

    effectLibraries = libraries;
    requireMandatoryLibraries();

    const engine = GetRoomEngine();

    await GetRoomManager().init(engine);
    GetRoomManager().addUpdateCategory(RoomObjectCategory.FLOOR);
    GetRoomManager().addUpdateCategory(RoomObjectCategory.WALL);
    GetRoomManager().addUpdateCategory(RoomObjectCategory.UNIT);
    GetRoomManager().addUpdateCategory(RoomObjectCategory.CURSOR);
    GetRoomManager().addUpdateCategory(RoomObjectCategory.ROOM);

    const catalogue: CameraCatalogueEntry[] = [];

    for(const effect of GetRoomCameraWidgetManager().effects.values())
    {
        if((effect.type !== 'colormatrix') && !effect.texture) fail(`Missing camera effect ${ effect.name }`);

        catalogue.push({ name: effect.name, minLevel: effect.minLevel ?? 0, type: effect.type });
    }

    window.cameraEffects = Object.freeze(catalogue);
    window.cameraRender = enqueue;
    window.cameraReady = true;
}

void start().catch(error =>
{
    startupError = (error instanceof Error) ? error : new Error(String(error));
    window.cameraReady = false;
    window.cameraRender = async () =>
    {
        throw startupError;
    };
    console.error(startupError);
});
