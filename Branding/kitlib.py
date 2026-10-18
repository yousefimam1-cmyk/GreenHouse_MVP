"""Shared drawing helpers for the Mazraa brand / UI kit generators (Pillow + numpy). All art is original."""
import hashlib, math, os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
FONT = os.path.join(ROOT, 'Assets', 'Resources', 'Fonts', 'Tajawal-Bold.ttf')

# Brand palette (Branding/BRAND.md)
SKY = (64, 196, 214); SUN = (255, 200, 40); OUT = (43, 27, 14)
FIELD = (46, 140, 70); PALM = (46, 107, 58); PALM_DARK = (22, 58, 32); SAND = (233, 216, 180); GOLD = (250, 194, 64)
ORANGE = (255, 140, 40); RED = (226, 50, 46); CREAM = (255, 248, 232)


def font(size, rtl=False):
    return ImageFont.truetype(FONT, size, layout_engine=ImageFont.Layout.RAQM)


def rgba(c, a=255):
    return (c[0], c[1], c[2], a) if len(c) == 3 else c


def lerp(a, b, t):
    return tuple(int(round(a[i] * (1 - t) + b[i] * t)) for i in range(len(a)))


def vgrad(w, h, stops):
    """Vertical gradient. stops = [(t, (r,g,b[,a])), ...] with t in 0..1."""
    im = Image.new('RGBA', (w, h)); d = ImageDraw.Draw(im)
    stops = sorted(stops)
    for y in range(h):
        t = y / max(1, h - 1)
        for i in range(len(stops) - 1):
            if stops[i][0] <= t <= stops[i + 1][0]:
                u = (t - stops[i][0]) / max(1e-6, stops[i + 1][0] - stops[i][0])
                c = lerp(rgba(stops[i][1]), rgba(stops[i + 1][1]), u)
                break
        else:
            c = rgba(stops[-1][1])
        d.line([(0, y), (w, y)], fill=c)
    return im


def rounded(w, h, r, fill, outline=None, width=0, ss=4):
    """Antialiased rounded rectangle (drawn supersampled)."""
    im = Image.new('RGBA', (w * ss, h * ss), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.rounded_rectangle([0, 0, w * ss - 1, h * ss - 1], radius=r * ss, fill=rgba(fill), outline=rgba(outline) if outline else None, width=width * ss)
    return im.resize((w, h), Image.LANCZOS)


def shadow(im, blur, dx, dy, alpha=120, color=(0, 0, 0)):
    """Drop shadow under a transparent image (returns a bigger canvas)."""
    pad = blur * 3
    out = Image.new('RGBA', (im.width + pad * 2, im.height + pad * 2), (0, 0, 0, 0))
    a = im.split()[3].point(lambda v: v * alpha // 255)
    sh = Image.new('RGBA', im.size, color + (0,)); sh.putalpha(a)
    sh = sh.filter(ImageFilter.GaussianBlur(blur))
    out.alpha_composite(sh, (pad + dx, pad + dy))
    out.alpha_composite(im, (pad, pad))
    return out


def star8(cx, cy, r, ri=None):
    """Eight-pointed star (classic Islamic geometric motif) as a polygon."""
    ri = ri if ri is not None else r * 0.72
    pts = []
    for k in range(16):
        a = math.pi / 8 * k - math.pi / 2
        rr = r if k % 2 == 0 else ri
        pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))
    return pts


def pattern_tile(size, color, alpha, line=3, ss=3):
    """Original girih-style tile: 8-point stars joined by small crosses; tiles seamlessly."""
    S = size * ss
    im = Image.new('RGBA', (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    c = rgba(color, alpha); w = line * ss
    half = S / 2
    for (cx, cy) in [(0, 0), (S, 0), (0, S), (S, S), (half, half)]:
        d.line(star8(cx, cy, S * 0.30, S * 0.21) + [star8(cx, cy, S * 0.30, S * 0.21)[0]], fill=c, width=w, joint='curve')
        d.line(star8(cx, cy, S * 0.14, S * 0.10) + [star8(cx, cy, S * 0.14, S * 0.10)[0]], fill=c, width=w, joint='curve')
    # crosses between the stars (the "kite" connections)
    for (cx, cy) in [(half, 0), (0, half), (S, half), (half, S)]:
        k = S * 0.11
        d.line([(cx - k, cy), (cx + k, cy)], fill=c, width=w); d.line([(cx, cy - k), (cx, cy + k)], fill=c, width=w)
    return im.resize((size, size), Image.LANCZOS)


def text_image(text, size, fill, rtl=False, stroke=0, stroke_fill=OUT, pad=8):
    kw = dict(direction='rtl', language='ar') if rtl else {}
    f = font(size)
    tmp = ImageDraw.Draw(Image.new('L', (4, 4)))
    b = tmp.textbbox((0, 0), text, font=f, stroke_width=stroke, **kw)
    w, h = b[2] - b[0] + pad * 2, b[3] - b[1] + pad * 2
    im = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(im).text((pad - b[0], pad - b[1]), text, font=f, fill=rgba(fill), stroke_width=stroke, stroke_fill=rgba(stroke_fill), **kw)
    return im


def guid_for(rel_path):
    """Stable Unity GUID for a generated asset (md5 of its project path)."""
    return hashlib.md5(('mazraa-kit:' + rel_path.replace(os.sep, '/')).encode()).hexdigest()


def write_meta(path, rel, kind='sprite', border=(0, 0, 0, 0), max_size=256, mips=False, compress=1, readable=False):
    """TextureImporter .meta: Sprite (2D and UI) with optional 9-slice border, or a plain Default texture."""
    sprite = kind == 'sprite'
    l, b, r, t = border
    meta = f"""fileFormatVersion: 2
guid: {guid_for(rel)}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: {1 if mips else 0}
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: {1 if readable else 0}
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: {max_size}
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: {1 if sprite else 0}
  spriteExtrude: 1
  spriteMeshType: 0
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: {l}, y: {b}, z: {r}, w: {t}}}
  spriteGenerateFallbackPhysicsShape: 0
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: {8 if sprite else 0}
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: {max_size}
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: {compress}
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    customData:
    physicsShape: []
    bones: []
    spriteID: 5e97eb03825dee720800000000000000
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    spriteCustomMetadata:
      entries: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""
    with open(path + '.meta', 'w') as f:
        f.write(meta)


def write_folder_meta(path, rel):
    if os.path.exists(path + '.meta'):
        return
    with open(path + '.meta', 'w') as f:
        f.write(f"""fileFormatVersion: 2
guid: {guid_for(rel + '/')}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
""")


def ensure_dir(abs_dir):
    """Create the folder (and .meta files for every new folder under Assets)."""
    os.makedirs(abs_dir, exist_ok=True)
    rel = os.path.relpath(abs_dir, ROOT)
    parts = rel.split(os.sep)
    for i in range(1, len(parts) + 1):
        sub = os.path.join(ROOT, *parts[:i])
        if parts[0] == 'Assets' and i > 1:
            write_folder_meta(sub, os.path.relpath(sub, ROOT))
