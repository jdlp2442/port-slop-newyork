#ifndef NYC_FACADE_INCLUDED
#define NYC_FACADE_INCLUDED

// ---------------------------------------------------------------------------------------------
// Facade cell + apartment interior + the two facade families.
// Port of facadeCell(), evaluateApartmentInterior(), createPreWarBuildingMaterial() and
// createCurtainWallMaterial() from js/tsl-materials.js.
//
// Vertex contract written by ManhattanMeshBuilder / export-nyc-glb.js:
//   uv0 = (metres along the wall, world Y)
//   uv1 = (wall width, box height)
//   uv2 = (wall kind, building tone)
//   uv3 = (box top Y, facade style)
//   color = (tone, style, seed, 1)
// ---------------------------------------------------------------------------------------------

#define NYC_WALL_SHOP       0
#define NYC_WALL_ENTRY      1
#define NYC_WALL_REAR       2
#define NYC_WALL_LOTLINE    3
#define NYC_WALL_BLANKSTONE 4

struct NycFacadeCell
{
    float  isRoof;
    float  uCoord;
    float  vCoord;
    float  gridV;
    float2 cellId;
    float2 cellUV;
    float2 fw;
    float  bayWidth;
    float  bayIdx;
    float  isGroundFloor;
    float  solid;
    float  floor0;
    float  back;
    float  blank;
    float  door;
    float  tone;
    float  wallS;
    float  wallW;
    float  wallH;
    float  fromTop;
    float  boxSeed;
    float  partial;
    float  coping;
    float  kind;
};

struct NycPrewarParams
{
    float3 stoneColor;
    float3 trimColor;
    float3 glassColor;
    float3 crownColor;
    float  windowWidth;
    float  floorHeight;
    float  windowRatioX;
    float  windowRatioY;
    float  litProbability;
    float  warmth;
    float  cornerPier;
    float  crownGlowHeight;
};

struct NycCurtainParams
{
    float3 glassTint;
    float3 frameTint;
    float  windowWidth;
    float  floorHeight;
    float  mullionThickness;
    float  spandrelRatio;
    float  litProbability;
    float  warmth;
    float  reflectivity;
};

struct NycSurface
{
    float3 albedo;
    float  roughness;
    float  metallic;
    float3 emission;
    float  height;     // metres, for NycBumpNormal
    float  ao;
};

// ---------------------------------------------------------------------------------------------
// Cell frame
// ---------------------------------------------------------------------------------------------

NycFacadeCell NycBuildFacadeCell(
    float3 positionWS, float3 normalWS,
    float2 uv0, float2 uv1, float2 uv2, float2 uv3, float4 vertexColor,
    float windowWidth, float floorHeight, float cornerPier)
{
    NycFacadeCell c = (NycFacadeCell)0;

    float3 an = abs(normalWS);
    c.isRoof = step(0.75, an.y);
    float alongZ = step(an.z, an.x);            // wall faces +/-X -> u runs along Z

    c.kind  = uv2.x;
    c.tone  = uv2.y;
    c.wallW = max(uv1.x, 0.01);
    c.wallH = max(uv1.y, 0.01);
    c.wallS = uv0.x;                            // metres from the wall's left corner
    c.vCoord = positionWS.y;
    c.fromTop = uv3.x - positionWS.y;           // metres below the box top

    // Deterministic per-box seed from the geometry itself (same idea as boxSeed in TSL).
    c.boxSeed = floor(NycHash1D(uv3.x * 0.37 + uv1.x * 1.7 + uv1.y * 0.11 + uv3.y * 3.1) * 997.0);

    float wallSeed = floor(c.boxSeed + dot(normalWS, float3(17.0, 0.0, 37.0)));

    float bays, gridU;
    float inPier = 0.0;

    if (cornerPier > 0.0)
    {
        float pier = min(cornerPier, c.wallW * 0.2);
        float inner = c.wallW - pier * 2.0;
        bays = max(floor(inner / windowWidth + 0.5), 1.0);
        c.bayWidth = inner / bays;
        float s = c.wallS;
        c.bayIdx = floor((s - pier) / c.bayWidth);
        gridU = (s - pier) / c.bayWidth + wallSeed;
        inPier = 1.0 - step(pier, s) * step(s, c.wallW - pier);
    }
    else
    {
        bays = max(floor(c.wallW / windowWidth + 0.5), 1.0);
        c.bayWidth = c.wallW / bays;
        c.bayIdx = floor(c.wallS / c.bayWidth);
        gridU = c.wallS / c.bayWidth + wallSeed;
    }

    c.uCoord = gridU * c.bayWidth;
    c.gridV  = c.vCoord / floorHeight;
    c.cellId = float2(floor(gridU), floor(c.gridV));
    c.cellUV = float2(frac(gridU), frac(c.gridV));
    c.fw     = float2(fwidth(gridU), fwidth(c.gridV));

    c.floor0 = 1.0 - step(1.0, c.gridV);
    float wall = 1.0 - c.isRoof;

    c.isGroundFloor = c.floor0 * (1.0 - step(NYC_WALL_SHOP + 0.5, c.kind));
    c.blank = step(NYC_WALL_LOTLINE - 0.5, c.kind) * wall;
    c.back  = step(NYC_WALL_REAR - 0.5, c.kind) * (1.0 - step(NYC_WALL_BLANKSTONE - 0.5, c.kind)) * wall;

    float isEntry = step(NYC_WALL_ENTRY - 0.5, c.kind) * (1.0 - step(NYC_WALL_ENTRY + 0.5, c.kind));
    c.door = c.floor0 * isEntry * wall
           * step(abs(c.bayIdx - min(floor(c.tone * bays), bays - 1.0)), 0.5);

    // A box that isn't a whole number of storeys ends in a solid parapet instead of slicing a
    // window row, and every wall is capped by a coping at its top.
    float top = c.vCoord + c.fromTop;
    float floorBase = floor(c.gridV) * floorHeight;
    float whole = step(top - c.wallH - 0.05, floorBase) * step(floorBase + floorHeight, top + 0.05);
    c.partial = (1.0 - whole) * wall;
    c.coping  = (1.0 - step(0.32, c.fromTop)) * wall;

    c.solid = max(max(inPier * (1.0 - c.isGroundFloor), c.blank), c.partial);
    return c;
}

// ---------------------------------------------------------------------------------------------
// 3D parallax apartment behind each window (evaluateApartmentInterior)
// Returns rgb = room colour, a = lit emission weight.
// ---------------------------------------------------------------------------------------------

float4 NycApartmentInterior(
    NycFacadeCell c, float2 winUV, float floorHeight, float seed, float warmth, float litProb,
    float3 normalWS, float3 positionWS, float3 cameraPos)
{
    float floorIdx = floor(c.vCoord / floorHeight);
    float fHash1 = frac(sin(floorIdx * 41.3 + seed) * 43758.5453);
    float fHash2 = frac(sin(floorIdx * 73.7 + seed * 1.7) * 43758.5453);
    float roomBays = lerp(2.0, 3.0, step(0.5, fHash1));
    float roomPhase = floor(fHash2 * roomBays);

    float gridU = c.uCoord / max(c.bayWidth, 0.01);
    float roomU = (gridU + roomPhase) / roomBays;
    float roomIdx = floor(roomU);
    float2 roomId = float2(roomIdx, floorIdx);

    float roomW = c.bayWidth * roomBays;
    float roomH = floorHeight * 0.92;
    const float setback = 0.12;
    float depth = roomH * 1.55;

    float originX = (frac(roomU) - 0.5) * roomW;
    float originY = (winUV.y - 0.46) * roomH * 0.62;

    // Horizontal tangent of this wall, and the view ray expressed in (across, up, depth).
    float3 tangent = normalize(cross(float3(0.0, 1.0, 0.0), normalWS) + float3(1e-5, 0.0, 0.0));
    float3 rayWorld = normalize(positionWS - cameraPos);
    float dirX = dot(rayWorld, tangent);
    float dirY = rayWorld.y;
    float dirZ = max(abs(dot(rayWorld, normalWS)), 0.05);

    float halfW = roomW * 0.5;
    float halfH = roomH * 0.5;
    float tFarX = (halfW - originX * sign(dirX)) / max(abs(dirX), 0.0001);
    float tFarY = (halfH - originY * sign(dirY)) / max(abs(dirY), 0.0001);
    float tFarZ = (setback + depth) / dirZ;
    float t = min(min(tFarX, tFarY), tFarZ);
    float3 hit = float3(originX + dirX * t, originY + dirY * t, dirZ * t);

    float3 q = saturate(float3(hit.x / roomW + 0.5, hit.y / roomH + 0.5, (hit.z - setback) / depth));

    float onBack  = step(tFarZ, tFarX) * step(tFarZ, tFarY);
    float isYPlane = step(tFarY, tFarX) * step(tFarY, tFarZ);
    float onCeil  = isYPlane * step(0.0, dirY);
    float onFloor = isYPlane * (1.0 - step(0.0, dirY));

    float seed1 = NycHash2D(roomId + float2(seed, 19.4));
    float seed2 = NycHash2D(roomId + float2(seed * 1.9, 83.1));
    float seed3 = NycHash2D(roomId + float2(63.2, seed * 0.7));

    float lod = smoothstep(0.3, 0.8, max(c.fw.x, c.fw.y));
    float litRoom = step(1.0 - litProb, seed3);
    float lit = lerp(litRoom, 1.0, lod);

    float3 warmLight = lerp(float3(1.0, 0.72, 0.27), float3(1.0, 0.89, 0.61), seed1);
    float3 coolLight = lerp(float3(0.87, 0.91, 1.0), float3(0.62, 0.71, 1.0), seed2);
    float3 lightCol = lerp(coolLight, warmLight, step(1.0 - warmth, seed2));

    float3 wallBase = lerp(lerp(float3(0.60, 0.55, 0.45), float3(0.44, 0.48, 0.51), seed1),
                           float3(0.72, 0.68, 0.59), seed2 * 0.6);
    float3 wallCol = lerp(wallBase, wallBase * 0.5, smoothstep(0.06, 0.04, q.y));

    float seam = step(0.92, frac(q.x * 7.0));
    float3 boards = lerp(float3(0.29, 0.20, 0.12), float3(0.41, 0.30, 0.19), seed1) * (1.0 - seam * 0.3);
    float3 rug = lerp(float3(0.48, 0.23, 0.20), float3(0.23, 0.34, 0.38), seed2);
    float inRug = step(abs(q.x - 0.5), 0.28) * step(abs(q.z - 0.58), 0.25);
    float3 floorCol = lerp(boards, rug, inRug * 0.88);

    float lamp = smoothstep(0.18, 0.11, length(float2(q.x - 0.5, q.z - 0.5))) * (1.0 - lod);
    float3 ceilBase = lerp(wallBase, float3(0.91, 0.89, 0.84), 0.28);
    float3 ceilCol = lerp(ceilBase, lightCol * lerp(0.85, 3.8, lit), lamp);

    float doorX = lerp(0.24, 0.76, seed1);
    float3 doorCol = lerp(float3(0.35, 0.27, 0.19), float3(0.22, 0.22, 0.24), step(0.5, seed2));
    float inDoor = step(abs(q.x - doorX), 0.09) * step(abs(q.y - 0.34), 0.34);

    float picX = lerp(0.72, 0.28, step(0.5, seed1));
    float inPicFrame  = step(abs(q.x - picX), 0.085) * step(abs(q.y - 0.56), 0.095);
    float inPicCanvas = step(abs(q.x - picX), 0.065) * step(abs(q.y - 0.56), 0.075);
    float3 picCol = lerp(float3(0.17, 0.23, 0.29), float3(0.48, 0.35, 0.23), frac(seed1 + seed2));

    float3 backCol = lerp(lerp(lerp(wallCol, doorCol, inDoor), float3(0.08, 0.07, 0.06), inPicFrame),
                          picCol, inPicCanvas);
    float3 shellCol = lerp(lerp(lerp(wallCol, floorCol, onFloor), ceilCol, onCeil), backCol, onBack);

    float aoX = smoothstep(0.0, 0.16, q.x) * smoothstep(0.0, 0.16, 1.0 - q.x);
    float aoY = smoothstep(0.0, 0.16, q.y) * smoothstep(0.0, 0.16, 1.0 - q.y);
    float aoZ = smoothstep(0.0, 0.16, q.z) * smoothstep(0.0, 0.16, 1.0 - q.z);
    float edgeAO = lerp(lerp(aoY * aoZ, aoX * aoZ, isYPlane), aoX * aoY, onBack);
    float falloff = lerp(1.0, 0.42, q.z);
    float3 shadedRoom = shellCol * lerp(0.66, 1.0, edgeAO) * falloff;

    // Curtains hung just inside the glass, parallax-shifted onto their plane.
    float cUVx = saturate(winUV.x + dirX / max(dirZ, 0.001) * 0.06);
    float wSeed1 = NycHash2D(c.cellId + float2(seed * 0.8, 41.2));
    float wSeed2 = NycHash2D(c.cellId + float2(17.9, seed * 1.3));

    float sL = smoothstep(0.18, 1.0, wSeed1);
    float sR = smoothstep(0.18, 1.0, wSeed2);
    float lw = sL * sL * 0.48;
    float rw = sR * sR * 0.48;

    float cFw = fwidth(cUVx);
    float drapeAA = cFw + 0.015;
    float inLeft  = smoothstep(lw + drapeAA, lw - drapeAA, cUVx) * step(0.03, lw);
    float inRight = smoothstep(1.0 - rw - drapeAA, 1.0 - rw + drapeAA, cUVx) * step(0.03, rw);
    float inDrape = lerp(saturate(max(inLeft, inRight)), 0.29, lod);

    float pick = frac(seed1 * 5.7) * 5.0;
    float3 sw0 = lerp(float3(0.79, 0.75, 0.65), float3(0.85, 0.80, 0.72), seed2);
    float3 sw1 = lerp(float3(0.54, 0.48, 0.39), float3(0.61, 0.55, 0.45), seed2);
    float3 sw2 = lerp(float3(0.37, 0.44, 0.47), float3(0.44, 0.51, 0.55), seed2);
    float3 sw3 = lerp(float3(0.42, 0.46, 0.35), float3(0.47, 0.51, 0.37), seed2);
    float3 sw4 = lerp(float3(0.55, 0.35, 0.27), float3(0.60, 0.42, 0.32), seed2);
    float3 fabric = lerp(lerp(lerp(lerp(sw0, sw1, step(1.0, pick)), sw2, step(2.0, pick)),
                              sw3, step(3.0, pick)), sw4, step(4.0, pick));

    float pleatWave = sin(cUVx * 48.0 + wSeed1 * 6.28) * (1.0 - smoothstep(0.6, 1.6, cFw * 48.0));
    float3 drapeCol = fabric * (pleatWave * 0.205 + 0.945) * 0.92;

    float3 bestCol = lerp(shadedRoom, drapeCol, inDrape);
    float bestEmit = lerp(1.0, 0.20, inDrape);
    float3 roomWarmth = lerp(float3(1.0, 1.0, 1.0), lightCol, lit * 0.85);
    float3 finalRoom = bestCol * roomWarmth * lerp(0.85, 1.35, lit);

    return float4(finalRoom, lerp(litRoom, litProb, lod) * bestEmit);
}

// ---------------------------------------------------------------------------------------------
// Pre-war masonry family (styles 0-2)
// ---------------------------------------------------------------------------------------------

NycSurface NycEvaluatePrewar(
    NycFacadeCell c, float3 positionWS, float3 normalWS, float3 cameraPos,
    NycPrewarParams p, float3 canyonGlow)
{
    NycSurface s = (NycSurface)0;

    float halfWinX = (1.0 - p.windowRatioX) * 0.5;
    float halfWinY = (1.0 - p.windowRatioY) * 0.5;
    float effHalfX = lerp(halfWinX, 0.11, c.isGroundFloor);
    float effHalfY = lerp(halfWinY, 0.10, c.isGroundFloor);

    float open  = (1.0 - c.solid) * (1.0 - c.door);
    float front = max(1.0 - c.back - c.blank, 0.0);

    float2 winUV = saturate(float2(
        (c.cellUV.x - effHalfX) / max(0.01, 1.0 - effHalfX * 2.0),
        (c.cellUV.y - effHalfY) / max(0.01, 1.0 - effHalfY * 2.0)));

    float seed = frac(c.tone * 13.7 + c.boxSeed * 0.017);
    float4 room = NycApartmentInterior(c, winUV, p.floorHeight, seed, p.warmth,
                                       p.litProbability * 0.62, normalWS, positionWS, cameraPos);

    float shopLit = step(0.24, NycHash2D(c.cellId + float2(seed, 19.4)));
    float3 bulbCol = lerp(float3(0.81, 0.89, 1.0), float3(1.0, 0.81, 0.53),
                          step(1.0 - p.warmth, NycHash2D(c.cellId + float2(seed * 1.9, 83.1))));

    // --- openings, belt course, sashes ------------------------------------------------
    float inX = NycFilteredPulse(c.cellUV.x, effHalfX, 1.0 - effHalfX, c.fw.x) * open;
    float inY = NycFilteredPulse(c.cellUV.y, effHalfY, 1.0 - effHalfY, c.fw.y);

    float cornicePhase = frac(c.gridV / 6.0);
    float isCornice = max(
        NycFilteredPulse(cornicePhase, 0.86, 1.0, c.fw.y / 6.0) * front,
        c.isGroundFloor * NycFilteredPulse(c.cellUV.y, 0.86, 1.0, c.fw.y));

    float beltFloor = step(5.0, fmod(floor(c.gridV), 6.0)) * front;

    float sashBarY = NycLineAA(c.cellUV.y - 0.52, 0.012) * (1.0 - c.isGroundFloor);
    float sashBarX = NycLineAA(c.cellUV.x - 0.50, 0.009) * (1.0 - c.isGroundFloor) * 0.55;
    float storeTransom = NycLineAA(c.cellUV.y - 0.70, 0.015) * c.isGroundFloor;
    float storeMullion = NycLineAA(frac(c.cellUV.x * 2.0) - 0.5, 0.018) * c.isGroundFloor;
    float sashMask = saturate(max(max(sashBarY, sashBarX), max(storeTransom, storeMullion)));

    // --- window air-conditioner unit --------------------------------------------------
    float acRand = NycHash2D(c.cellId + float2(seed * 1.3 + 7.1, 91.7));
    float hasAC = step(0.88, acRand) * (1.0 - c.isGroundFloor) * (1.0 - c.isRoof) * open * (1.0 - beltFloor);

    float acMinX = 0.31, acMaxX = 0.69;
    float acMinY = effHalfY, acMaxY = effHalfY + 0.22;
    float inACBox = NycFilteredPulse(c.cellUV.x, acMinX, acMaxX, c.fw.x)
                  * NycFilteredPulse(c.cellUV.y, acMinY, acMaxY, c.fw.y) * hasAC;
    float2 acUV = saturate(float2((c.cellUV.x - acMinX) / 0.38, (c.cellUV.y - acMinY) / 0.22));
    float acGrille = smoothstep(0.08, 0.16, acUV.x) * (1.0 - smoothstep(0.84, 0.92, acUV.x))
                   * smoothstep(0.14, 0.22, acUV.y) * (1.0 - smoothstep(0.84, 0.92, acUV.y));
    float acSlats = frac(acUV.y * 6.0);
    float3 acPlastic = lerp(float3(0.94, 0.93, 0.91), float3(0.79, 0.78, 0.74), frac(acRand * 7.3));
    float3 acColor = acPlastic * lerp(1.0, lerp(0.38, 0.75, acSlats), acGrille);
    float acUnderShadow = NycFilteredPulse(c.cellUV.x, acMinX - 0.02, acMaxX + 0.02, c.fw.x)
                        * NycFilteredPulse(c.cellUV.y, effHalfY - 0.09, effHalfY, c.fw.y)
                        * hasAC * 0.42;

    float isWindowOpening = inX * inY * (1.0 - c.isRoof) * (1.0 - isCornice);
    float isWindowGlass = isWindowOpening * (1.0 - sashMask) * (1.0 - inACBox);

    // --- running-bond brickwork / ashlar coursing -------------------------------------
    const float brickH = 0.24;
    const float brickL = 0.56;
    float rowCoord = c.vCoord / brickH;
    float courseRow = floor(rowCoord);
    float colCoord = c.uCoord / brickL + fmod(courseRow, 2.0) * 0.5;

    float3 backBrick = lerp(
        lerp(float3(0.42, 0.24, 0.19), float3(0.54, 0.31, 0.24), frac(c.tone * 7.31)),
        lerp(float3(0.60, 0.53, 0.41), float3(0.69, 0.60, 0.48), frac(c.tone * 5.17)),
        step(0.58, c.tone));

    float pargePatch = smoothstep(0.6, 0.76, NycNoise2D(float2(c.uCoord * 0.11, c.vCoord * 0.07) + c.tone * 37.0));
    float parged = max(step(0.74, frac(c.tone * 3.7)), pargePatch * 0.85) * c.blank * c.back;
    float3 backTint = lerp(backBrick, float3(0.56, 0.55, 0.52), parged);

    float mortarJoint = max(NycGridLine(colCoord * brickL, brickL, 0.016), NycGridLine(c.vCoord, brickH, 0.016))
                      * (1.0 - c.isRoof) * (1.0 - parged) * (1.0 - c.coping);

    float uFw = fwidth(c.uCoord);
    float brickVar = 1.0 - smoothstep(0.5, 1.0, max(uFw / brickL, fwidth(rowCoord)));
    float2 brickCell = float2(floor(colCoord), courseRow);
    float brickRnd = (NycHash2D(brickCell + float2(seed, 5.3)) - 0.5) * brickVar;
    float warmShift = (NycHash2D(brickCell + float2(17.1, seed)) - 0.5) * 0.12 * brickVar;
    float3 brickShift = float3(1.0 + warmShift, 1.0, 1.0 - warmShift);

    float streak = lerp(0.5, NycNoise2D(float2(c.uCoord * 1.4, c.vCoord * 0.05)),
                        1.0 - smoothstep(0.5, 1.0, uFw * 1.4));
    float soot = smoothstep(0.25, 0.78, streak) * (1.0 - smoothstep(0.0, 180.0, c.vCoord)) * 0.36;

    float lintelShadow = smoothstep(1.0 - effHalfY - 0.14, 1.0 - effHalfY, c.cellUV.y) * 0.58;
    float jambShadow = (1.0 - smoothstep(effHalfX, effHalfX + 0.08, c.cellUV.x)) * 0.42;
    float sillSpan = NycFilteredPulse(c.cellUV.x, effHalfX - 0.045, 1.0 - effHalfX + 0.045, c.fw.x)
                   * open * (1.0 - beltFloor);
    float isSillTop = NycFilteredPulse(c.cellUV.y, effHalfY - 0.032, effHalfY, c.fw.y) * sillSpan;
    float isSillUnder = NycFilteredPulse(c.cellUV.y, effHalfY - 0.075, effHalfY - 0.032, c.fw.y) * sillSpan;
    float corniceShadow = smoothstep(0.76, 0.86, cornicePhase) * 0.32 * front;

    float3 wallTint = lerp(p.stoneColor, backTint, c.back);
    float trimMix = max(isCornice * 0.62 + c.isGroundFloor * 0.28, c.coping * 0.8);
    float3 masonry = lerp(wallTint, p.trimColor, trimMix)
                   * (brickRnd * 0.14 + 1.0) * brickShift;
    masonry = lerp(masonry, masonry * 0.62, mortarJoint);
    masonry = lerp(masonry, float3(0.29, 0.26, 0.21), soot)
            * (1.0 - corniceShadow) * (1.0 - isSillUnder * 0.42) * (1.0 - acUnderShadow)
            + _NycSunColor.rgb * isSillTop * 0.24
            + canyonGlow;

    // --- glass ------------------------------------------------------------------------
    float pooledDirt = (1.0 - smoothstep(0.0, 0.32, winUV.y)) * 0.36;
    float upperGrime = saturate(0.52 + soot * 0.35 + pooledDirt);

    float3 viewDir = normalize(cameraPos - positionWS);
    float fresnel = pow(saturate(1.0 - abs(dot(viewDir, normalWS))), 2.0);
    float recessShade = 1.0 - max(lintelShadow, jambShadow);

    float3 upperGlass = lerp(room.rgb * float3(0.71, 0.78, 0.75), p.glassColor, upperGrime);
    float shopShelves = 1.0 - step(0.76, frac(winUV.y * 3.0)) * 0.34;
    float3 shopInterior = bulbCol * (smoothstep(0.05, 0.92, winUV.y) * 0.48 + 0.24) * shopShelves * shopLit;
    float3 shopGlass = lerp(p.glassColor, shopInterior, 0.62 * (1.0 - pooledDirt));
    float3 glassBase = lerp(upperGlass, shopGlass, c.isGroundFloor);
    float3 glassCol = lerp(glassBase, _NycSkyHorizonColor.rgb, fresnel * 0.52 + 0.08) * recessShade;

    float3 frameCol = masonry * 0.46 * recessShade;
    float3 roofCol = lerp(p.trimColor * 0.42, p.trimColor * 0.62, NycNoise2D(positionWS.xz * 0.35));

    float3 col = lerp(masonry, roofCol, c.isRoof);
    col = lerp(col, frameCol, isWindowOpening * sashMask);
    col = lerp(col, glassCol, isWindowGlass);
    col = lerp(col, acColor, inACBox);

    // --- residential entrance ---------------------------------------------------------
    if (c.door > 0.5)
    {
        float inDoorway = NycFilteredPulse(c.cellUV.x, 0.27, 0.73, c.fw.x)
                        * NycFilteredPulse(c.cellUV.y, 0.065, 0.8, c.fw.y);
        float surround = NycFilteredPulse(c.cellUV.x, 0.21, 0.79, c.fw.x)
                       * NycFilteredPulse(c.cellUV.y, 0.0, 0.86, c.fw.y);
        float transom = NycFilteredPulse(c.cellUV.y, 0.67, 0.78, c.fw.y);
        float doorRnd = frac(c.tone * 11.3);
        float3 doorPaint = lerp(
            lerp(float3(0.11, 0.10, 0.10), float3(0.30, 0.13, 0.09), step(0.35, doorRnd)),
            lerp(float3(0.13, 0.21, 0.16), float3(0.42, 0.27, 0.16), step(0.8, doorRnd)),
            step(0.6, doorRnd));
        float doorPanels = max(NycFilteredPulse(c.cellUV.x, 0.494, 0.506, c.fw.x),
                               NycFilteredPulse(c.cellUV.y, 0.39, 0.41, c.fw.y));
        float3 doorCol = lerp(doorPaint * (1.0 - doorPanels * 0.5),
                              lerp(p.glassColor, _NycSkyHorizonColor.rgb, 0.18), transom);
        col = lerp(lerp(col, p.trimColor * 0.85, surround), doorCol, inDoorway);
    }

    s.albedo = col;

    // --- roughness --------------------------------------------------------------------
    float winY = saturate((c.cellUV.y - halfWinY) / max(0.01, 1.0 - 2.0 * halfWinY));
    float pooledRough = (1.0 - smoothstep(0.0, 0.32, winY)) * 0.38;
    float glassRough = 0.14 + pooledRough;
    float stoneGrain = NycNoise2D(float2(c.uCoord * 8.0, c.vCoord * 8.0)) * 0.12;
    float isWindow = NycFilteredPulse(c.cellUV.x, halfWinX, 1.0 - halfWinX, c.fw.x) * open
                   * (1.0 - NycFilteredPulse(c.gridV / 6.0, 0.86, 1.0, c.fw.y / 6.0) * front)
                   * NycFilteredPulse(c.cellUV.y, halfWinY, 1.0 - halfWinY, c.fw.y)
                   * (1.0 - c.isRoof);
    s.roughness = lerp(0.78 + stoneGrain, glassRough, isWindow);
    s.metallic = 0.06;

    // --- height field for bump ---------------------------------------------------------
    float footprint = max(fwidth(c.uCoord), fwidth(c.vCoord));
    float distU = (0.5 - abs(frac(colCoord) - 0.5)) * brickL;
    float distV = (0.5 - abs(frac(rowCoord) - 0.5)) * brickH;
    float brickDome = smoothstep(0.0, 0.018, distU) * smoothstep(0.0, 0.018, distV) * 0.0045
                    * (1.0 - smoothstep(0.006, 0.012, footprint));
    float sillLedge = NycFilteredPulse(c.cellUV.y, effHalfY - 0.032, effHalfY, c.fw.y) * inX * 0.016;
    float corniceLedge = NycFilteredPulse(c.gridV / 6.0, 0.86, 1.0, c.fw.y / 6.0) * front * 0.022;
    float acLouver = frac((c.cellUV.y - effHalfY) / 0.22 * 6.0) * 0.012
                   * (1.0 - smoothstep(0.012, 0.025, footprint)) * inACBox;
    float wallHeight = brickDome * (1.0 - c.coping) + sillLedge + corniceLedge + c.coping * 0.024;
    s.height = lerp(wallHeight, acLouver, isWindow);

    // --- emission ----------------------------------------------------------------------
    float glowHalfX = lerp(halfWinX + 0.015, 0.12, c.isGroundFloor);
    float glowHalfY = lerp(halfWinY + 0.015, 0.11, c.isGroundFloor);
    float gx = NycFilteredPulse(c.cellUV.x, glowHalfX, 1.0 - glowHalfX, c.fw.x) * open;
    float gy = NycFilteredPulse(c.cellUV.y, glowHalfY, 1.0 - glowHalfY, c.fw.y);
    float glowCornice = max(
        NycFilteredPulse(frac(c.gridV / 6.0), 0.86, 1.0, c.fw.y / 6.0) * front,
        c.isGroundFloor * NycFilteredPulse(c.cellUV.y, 0.86, 1.0, c.fw.y));
    float glowSash = saturate(max(max(NycLineAA(c.cellUV.y - 0.52, 0.014) * (1.0 - c.isGroundFloor),
                                     NycLineAA(c.cellUV.x - 0.50, 0.010) * (1.0 - c.isGroundFloor) * 0.55),
                                 max(NycLineAA(c.cellUV.y - 0.70, 0.016) * c.isGroundFloor,
                                     NycLineAA(frac(c.cellUV.x * 2.0) - 0.5, 0.020) * c.isGroundFloor)));
    float glowAC = NycFilteredPulse(c.cellUV.x, 0.31, 0.69, c.fw.x)
                 * NycFilteredPulse(c.cellUV.y, glowHalfY, glowHalfY + 0.22, c.fw.y) * hasAC;

    float isGlowWindow = gx * gy * (1.0 - c.isRoof) * (1.0 - glowCornice)
                       * (1.0 - glowSash) * (1.0 - glowAC);

    float shopShelvesE = 1.0 - step(0.76, frac(winUV.y * 3.0)) * 0.32;
    float vertGrad = smoothstep(0.08, 0.92, winUV.y) * 0.62 + 0.28;
    float3 shopEmit = bulbCol * shopLit * vertGrad * shopShelvesE * 0.58;

    float transmission = 1.0 - pow(saturate(1.0 - abs(dot(viewDir, normalWS))), 1.8) * 0.82;
    float3 winEmit = lerp(room.rgb * room.a * 0.76, shopEmit, c.isGroundFloor)
                   * transmission * isGlowWindow * _NycInteriorGlow;

    float crownMask = smoothstep(p.crownGlowHeight, p.crownGlowHeight + 26.0, c.vCoord)
                    * (1.0 - c.isRoof * 0.5);
    float3 crownFlood = p.crownColor * crownMask * _NycInteriorGlow * 0.55;

    float transomGlow = c.door * NycFilteredPulse(c.cellUV.x, 0.27, 0.73, c.fw.x)
                      * NycFilteredPulse(c.cellUV.y, 0.67, 0.78, c.fw.y);
    float3 doorEmit = float3(1.0, 0.81, 0.53) * transomGlow * _NycInteriorGlow * 0.32;

    s.emission = winEmit + crownFlood + doorEmit;
    s.ao = NycCanyonAO(positionWS);
    return s;
}

// ---------------------------------------------------------------------------------------------
// Curtain-wall family (styles 3-5)
// ---------------------------------------------------------------------------------------------

NycSurface NycEvaluateCurtain(
    NycFacadeCell c, float3 positionWS, float3 normalWS, float3 cameraPos,
    NycCurtainParams p, float3 canyonGlow)
{
    NycSurface s = (NycSurface)0;

    float2 winUV = saturate(float2(
        (c.cellUV.x - p.mullionThickness) / max(0.01, 1.0 - 2.0 * p.mullionThickness),
        (c.cellUV.y - p.spandrelRatio) / max(0.01, 1.0 - p.spandrelRatio - p.mullionThickness)));

    float seed = frac(c.tone * 9.3 + c.boxSeed * 0.013);
    float4 room = NycApartmentInterior(c, winUV, p.floorHeight, seed, p.warmth,
                                       p.litProbability * 0.65, normalWS, positionWS, cameraPos);

    float shopLit = step(0.24, NycHash2D(c.cellId + float2(seed, 19.4)));
    float floorIdx = floor(c.gridV);
    float isMechFloor = (1.0 - step(1.0, fmod(floorIdx + seed, 16.0))) * step(4.0, floorIdx);

    float isGlassPane = NycFilteredPulse(c.cellUV.x, p.mullionThickness * 0.5, 1.0 - p.mullionThickness * 0.5, c.fw.x)
                      * NycFilteredPulse(c.cellUV.y, p.spandrelRatio + 0.015, 1.0 - p.mullionThickness * 0.5, c.fw.y)
                      * (1.0 - c.isRoof) * (1.0 - c.partial) * (1.0 - c.coping);

    float louverStripes = sin(c.vCoord * 22.0) * 0.5 + 0.5;

    float paneRandX = NycHash2D(c.cellId + float2(seed, 3.7)) - 0.5;
    float paneRandY = NycHash2D(c.cellId + float2(11.3, seed)) - 0.5;
    float bowX = (c.cellUV.x - 0.5) * 0.11 + paneRandX * 0.09;
    float bowY = (c.cellUV.y - 0.55) * 0.11 + paneRandY * 0.09;

    float3 viewDir = normalize(cameraPos - positionWS);
    float3 nPane = normalize(normalWS + float3(bowX, bowY, -bowX));
    float3 reflectDir = reflect(-viewDir, nPane);

    float fresnel = pow(saturate(1.0 - abs(dot(viewDir, nPane))), 2.1);
    float reflectElev = saturate(reflectDir.y * 0.65 + 0.38);

    // Reflected neighbouring towers: a procedural skyline band with its own window grid.
    float refAzimuth = c.uCoord * 0.045 + (reflectDir.x + reflectDir.z) * 3.8;
    float neighborH = NycHash2D(float2(floor(refAzimuth * 2.4), seed)) * 0.46 + 0.16;
    float elevAA = fwidth(reflectElev) + 0.004;
    float inTower = smoothstep(neighborH + elevAA, neighborH - elevAA, reflectElev);
    float refCols = refAzimuth * 15.0;
    float refRows = reflectElev * 30.0;
    float refGrid = NycFilteredPulse(frac(refCols), 0.32, 1.0, fwidth(refCols))
                  * NycFilteredPulse(frac(refRows), 0.32, 1.0, fwidth(refRows));
    float3 reflectedBuilding = lerp(float3(0.05, 0.08, 0.13), float3(1.0, 0.83, 0.58),
                                    refGrid * 0.42 * _NycInteriorGlow);

    float3 skyReflect = lerp(_NycSkyHorizonColor.rgb, _NycSkyTopColor.rgb, saturate(reflectElev * 1.4));
    float3 envReflection = lerp(skyReflect, reflectedBuilding, inTower * 0.62);
    float sunSpec = pow(max(dot(reflectDir, normalize(_NycSunDir.xyz)), 0.0), 48.0) * 1.25;

    float pooledDirt = (1.0 - smoothstep(p.spandrelRatio, p.spandrelRatio + 0.22, c.cellUV.y)) * 0.32;
    float grime = saturate(0.56 + pooledDirt);
    float shopShelves = 1.0 - step(0.78, frac(winUV.y * 3.0)) * 0.35;
    float shopTransom = step(0.76, winUV.y) * 0.35 + 0.65;
    float3 shopInterior = float3(1.0, 0.88, 0.65) * shopShelves * shopTransom * lerp(0.28, 0.88, shopLit);
    float3 shopBase = lerp(p.glassTint, shopInterior, 0.56 * (1.0 - pooledDirt));
    float3 upperGlazed = lerp(room.rgb * float3(0.71, 0.78, 0.75), p.glassTint, grime);
    float3 glazedBase = lerp(upperGlazed, shopBase, c.isGroundFloor);

    float3 glassColor = lerp(glazedBase, envReflection, fresnel * p.reflectivity + 0.18)
                      + _NycSunColor.rgb * sunSpec;

    float3 spandrelDark = p.frameTint * 0.52;
    float3 frameBase = lerp(p.frameTint, spandrelDark, NycFilteredPulse(c.cellUV.y, 0.0, p.spandrelRatio, c.fw.y));

    float canyonBand = smoothstep(0.0, 14.0, c.vCoord) * (1.0 - smoothstep(22.0, 75.0, c.vCoord));
    float3 glow = float3(0.91, 0.61, 0.41) * canyonBand * _NycCanyonBounce * 0.16;

    float3 roofColor = lerp(float3(0.13, 0.14, 0.16), float3(0.23, 0.25, 0.29),
                            NycNoise2D(positionWS.xz * 0.35));

    float3 facadeCol = lerp(frameBase, glassColor, isGlassPane * (1.0 - isMechFloor)) + glow;
    float3 mechCol = lerp(p.frameTint * 0.36, p.frameTint * 0.76, louverStripes);
    float3 wall = lerp(facadeCol, mechCol, isMechFloor * (1.0 - c.isRoof));

    float panelJoint = max(NycFilteredPulse(c.cellUV.x, 0.0, 0.025, c.fw.x),
                           NycFilteredPulse(c.cellUV.y, 0.0, 0.03, c.fw.y));
    float panelStain = NycNoise2D(float2(c.uCoord * 0.9, c.vCoord * 0.04))
                     * (1.0 - smoothstep(0.4, 1.2, fwidth(c.uCoord)));
    float3 panelCol = lerp(float3(0.54, 0.53, 0.49), float3(0.67, 0.65, 0.61), c.tone)
                    * (panelStain * 0.16 + 0.9) * (1.0 - panelJoint * 0.3);
    wall = lerp(wall, panelCol + glow, c.blank);
    wall = lerp(wall, lerp(p.frameTint, float3(0.79, 0.81, 0.83), 0.45), c.coping);

    s.albedo = lerp(wall, roofColor, c.isRoof);

    float isFrame = 1.0 - NycFilteredPulse(c.cellUV.x, p.mullionThickness, 1.0 - p.mullionThickness, c.fw.x)
                        * NycFilteredPulse(c.cellUV.y, p.spandrelRatio, 1.0, c.fw.y);
    float sillDirt = (1.0 - smoothstep(p.spandrelRatio, p.spandrelRatio + 0.18, c.cellUV.y)) * 0.18;
    float glassOrFrame = lerp(0.05 + sillDirt, 0.54, max(max(isFrame, c.isRoof), c.partial));
    s.roughness = lerp(lerp(glassOrFrame, 0.36, c.coping), 0.88, c.blank);
    s.metallic = lerp(0.72, 0.0, c.blank);

    float inPane = NycFilteredPulse(c.cellUV.x, p.mullionThickness + 0.01, 0.99 - p.mullionThickness, max(c.fw.x * 2.0, 0.02))
                 * NycFilteredPulse(c.cellUV.y, p.spandrelRatio + 0.01, 0.99 - p.mullionThickness, max(c.fw.y * 2.0, 0.02))
                 * (1.0 - c.partial) * (1.0 - c.coping);
    float camDist = length(positionWS - cameraPos);
    float detailFade = (1.0 - smoothstep(18.0, 140.0, camDist)) * (1.0 - c.isRoof)
                     * (1.0 - smoothstep(0.03, 0.1, max(c.fw.x, c.fw.y)));
    s.height = (1.0 - inPane) * 0.014 * (1.0 - c.blank) * detailFade + c.coping * 0.01;

    float inPaneE = NycFilteredPulse(c.cellUV.x, p.mullionThickness + 0.015, 1.0 - p.mullionThickness - 0.015, c.fw.x)
                  * NycFilteredPulse(c.cellUV.y, p.spandrelRatio + 0.02, 1.0 - p.mullionThickness * 0.5, c.fw.y);
    float paneMask = inPaneE * (1.0 - c.isRoof) * (1.0 - isMechFloor) * (1.0 - c.blank)
                   * (1.0 - c.partial) * (1.0 - c.coping);
    float shelvesE = 1.0 - step(0.78, frac(winUV.y * 3.0)) * 0.32;
    float3 shopEmit = float3(1.0, 0.88, 0.65) * shopLit * shelvesE * 0.62;
    float3 emitCol = lerp(room.rgb * room.a, shopEmit, c.isGroundFloor);
    float trans = 1.0 - pow(saturate(1.0 - abs(dot(viewDir, normalWS))), 2.0) * 0.75;
    s.emission = emitCol * trans * paneMask * _NycInteriorGlow * 0.72;

    s.ao = NycCanyonAO(positionWS);
    return s;
}

#endif // NYC_FACADE_INCLUDED
