"""Mazraa UI art kit (original): 9-slice panels / buttons / bars / tabs, a geometric pattern tile, 90+ flat icons (128 px,
white silhouettes tinted at runtime by UiKit) and controller glyphs (Xbox and PlayStation styles, our own drawings).
Writes PNGs + Unity .meta files (Sprite, 9-slice borders, no mipmaps, compressed) to Assets/Resources/UI/.
Run: python3 Branding/make_ui_kit.py"""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from PIL import Image, ImageDraw, ImageFilter
from kitlib import *

UI = os.path.join(ROOT, 'Assets', 'Resources', 'UI')
KIT, ICONS, GLYPHS = (os.path.join(UI, n) for n in ('Kit', 'Icons', 'Glyphs'))
for d in (KIT, ICONS, GLYPHS):
    ensure_dir(d)

BORDERS = {}   # name -> (l, b, r, t) 9-slice border in pixels


def save_kit(name, im, border, max_size=256):
    p = os.path.join(KIT, name + '.png'); im.save(p, optimize=True)
    write_meta(p, os.path.relpath(p, ROOT), 'sprite', border, max_size)
    BORDERS[name] = border


# ------------------------------------------------------------------ 9-slice panels and buttons
# Design system (UiTheme in UiKit.cs): flat, near-opaque charcoal surfaces, a faint hairline, a small soft shadow,
# one warm amber accent. No gradients, no gloss. Radii: panels 10, buttons 6, bars fully round.
import numpy as _np
AMBER = (242, 169, 59); AMBER_HI = (255, 200, 110); INK = (16, 19, 23)


def _mask_min(im, mask):
    a = _np.minimum(_np.array(im.split()[3]), _np.array(mask))
    im.putalpha(Image.fromarray(a.astype('uint8')))
    return im


def glass(w, h, radius, fill=(20, 22, 25, 232), stroke=(255, 255, 255, 18), top=None, shadow_px=0, shadow_a=90):
    """Rounded glass rect: flat translucent fill, a faint top sheen, 1 px light hairline; optional soft shadow baked
    into a transparent margin (shadow_px on every side, a little deeper below)."""
    ss = 4; m = shadow_px
    iw, ih = w - 2 * m, h - 2 * m
    S = Image.new('RGBA', (iw * ss, ih * ss), (0, 0, 0, 0)); d = ImageDraw.Draw(S)
    d.rounded_rectangle([0, 0, iw * ss - 1, ih * ss - 1], radius=radius * ss, fill=fill)
    mask = Image.new('L', S.size, 0); ImageDraw.Draw(mask).rounded_rectangle([0, 0, iw * ss - 1, ih * ss - 1], radius=radius * ss, fill=255)
    if top:
        band = vgrad(iw * ss, ih * ss, [(0, top), (min(0.9, 40.0 / ih), top[:3] + (0,)), (1, (0, 0, 0, 0))])
        S.alpha_composite(_mask_min(band, mask))
    if stroke:
        d.rounded_rectangle([0, 0, iw * ss - 1, ih * ss - 1], radius=radius * ss, outline=stroke, width=ss)
    S = S.resize((iw, ih), Image.LANCZOS)
    out = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    if m:
        sh = Image.new('L', (w, h), 0)
        ImageDraw.Draw(sh).rounded_rectangle([m, m + m // 4, w - m - 1, h - m - 1 + m // 4], radius=radius, fill=shadow_a)
        sh = sh.filter(ImageFilter.GaussianBlur(m / 2.2))
        so = Image.new('RGBA', (w, h), (0, 0, 0, 0)); so.putalpha(sh)
        out.alpha_composite(so)
    out.alpha_composite(S, (m, m))
    return out


def btn(w=128, h=64, radius=6, fill=(255, 255, 255, 13), stroke=None, top=None, grad=None):
    ss = 4
    im = Image.new('RGBA', (w * ss, h * ss), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    mask = Image.new('L', im.size, 0); ImageDraw.Draw(mask).rounded_rectangle([0, 0, w * ss - 1, h * ss - 1], radius=radius * ss, fill=255)
    if grad:
        g = vgrad(w * ss, h * ss, grad); im.alpha_composite(_mask_min(g, mask))
    else:
        d.rounded_rectangle([0, 0, w * ss - 1, h * ss - 1], radius=radius * ss, fill=fill)
    if top:
        g = vgrad(w * ss, h * ss, [(0, top), (0.5, top[:3] + (0,)), (1, (0, 0, 0, 0))]); im.alpha_composite(_mask_min(g, mask))
    if stroke:
        d.rounded_rectangle([0, 0, w * ss - 1, h * ss - 1], radius=radius * ss, outline=stroke[:4], width=int(stroke[4] if len(stroke) > 4 else 1) * ss)
    return im.resize((w, h), Image.LANCZOS)


# panels (shadow baked into a 14 px margin: the 9-slice border covers margin + corner radius)
save_kit('panel', glass(192, 192, 10, shadow_px=14), (28, 28, 28, 28))
save_kit('panel_plain', glass(192, 192, 10, fill=(20, 22, 25, 200)), (16, 16, 16, 16))
save_kit('panel_light', glass(192, 192, 10, fill=(240, 236, 228, 250), stroke=(0, 0, 0, 30), shadow_px=14), (28, 28, 28, 28))
save_kit('tooltip', glass(128, 128, 8, fill=(14, 16, 19, 242), shadow_px=10), (20, 20, 20, 20))
# card: glass with a thin amber rule along the top edge
card = glass(192, 192, 10, shadow_px=14)
ImageDraw.Draw(card).rectangle([14 + 10, 14, 192 - 14 - 10, 16], fill=AMBER + (255,))
save_kit('card', card, (28, 28, 28, 28))
# buttons / menu rows
save_kit('button', btn(), (24, 24, 24, 24))
save_kit('button_hover', btn(fill=(255, 255, 255, 26), stroke=(255, 255, 255, 40, 1)), (24, 24, 24, 24))
save_kit('button_selected', btn(fill=AMBER + (255,)), (24, 24, 24, 24))
save_kit('button_disabled', btn(fill=(255, 255, 255, 6)), (24, 24, 24, 24))
save_kit('button_primary', btn(fill=AMBER + (255,)), (24, 24, 24, 24))
save_kit('button_danger', btn(fill=(214, 74, 60, 240)), (24, 24, 24, 24))
save_kit('button_hc', btn(fill=(255, 217, 0, 255), stroke=(0, 0, 0, 255, 3)), (24, 24, 24, 24))   # high-contrast selection
# focus ring (controller focus around a card / tile)
save_kit('highlight', btn(fill=(0, 0, 0, 0), stroke=AMBER + (255, 3), radius=8), (30, 30, 30, 30))
# tabs: inactive = nothing but a faint hover plate; active = light plate with an amber underline
save_kit('tab', btn(fill=(255, 255, 255, 0)), (24, 8, 24, 24))
tab = btn(fill=(255, 255, 255, 22))
ImageDraw.Draw(tab).rounded_rectangle([16, 58, 111, 63], radius=2, fill=AMBER + (255,))
save_kit('tab_active', tab, (24, 8, 24, 24))
# progress bars: pill track + white pill fills (tinted at runtime) and coloured variants
save_kit('bar_track', btn(64, 24, 11, fill=(255, 255, 255, 26)), (12, 11, 12, 11))
save_kit('bar_fill', btn(64, 24, 11, fill=AMBER + (255,)), (12, 11, 12, 11))
save_kit('bar_fill_green', btn(64, 24, 11, fill=(124, 203, 107, 255)), (12, 11, 12, 11))
save_kit('bar_fill_blue', btn(64, 24, 11, fill=(127, 184, 230, 255)), (12, 11, 12, 11))
save_kit('bar_fill_red', btn(64, 24, 11, fill=(239, 106, 90, 255)), (12, 11, 12, 11))
save_kit('bar_fill_white', btn(64, 24, 11, fill=(255, 255, 255, 255)), (12, 11, 12, 11))
# divider: hairline fading out at both ends
div = Image.new('RGBA', (256, 16), (0, 0, 0, 0))
for x in range(256):
    a = int(60 * min(1.0, min(x, 255 - x) / 64.0))
    div.putpixel((x, 8), (255, 255, 255, a))
save_kit('divider', div, (100, 0, 100, 0))
# pattern tile (tiled behind headers / loading tips, very subtle)
pattern_tile(128, AMBER, 26).save(os.path.join(KIT, 'pattern.png'), optimize=True)
write_meta(os.path.join(KIT, 'pattern.png'), os.path.relpath(os.path.join(KIT, 'pattern.png'), ROOT), 'sprite', (0, 0, 0, 0), 256)
BORDERS['pattern'] = (0, 0, 0, 0)
# soft circle for dots / glows
dot = Image.new('RGBA', (256, 256), (0, 0, 0, 0)); ImageDraw.Draw(dot).ellipse([4, 4, 251, 251], fill=(255, 255, 255, 255))
dot = dot.resize((64, 64), Image.LANCZOS)
dot.save(os.path.join(KIT, 'dot.png')); write_meta(os.path.join(KIT, 'dot.png'), os.path.relpath(os.path.join(KIT, 'dot.png'), ROOT), 'sprite', (0, 0, 0, 0), 64); BORDERS['dot'] = (0, 0, 0, 0)
glow = Image.new('L', (128, 128), 0)
for r in range(60, 0, -1):
    ImageDraw.Draw(glow).ellipse([64 - r, 64 - r, 64 + r, 64 + r], fill=int(255 * (1 - r / 60) ** 2))
g = Image.new('RGBA', (128, 128), (255, 255, 255, 0)); g.putalpha(glow)
g.save(os.path.join(KIT, 'glow.png')); write_meta(os.path.join(KIT, 'glow.png'), os.path.relpath(os.path.join(KIT, 'glow.png'), ROOT), 'sprite', (0, 0, 0, 0), 128); BORDERS['glow'] = (0, 0, 0, 0)


def plain(name, im, size):
    p = os.path.join(KIT, name + '.png'); im.save(p, optimize=True)
    write_meta(p, os.path.relpath(p, ROOT), 'sprite', (0, 0, 0, 0), size); BORDERS[name] = (0, 0, 0, 0)


# gauge ring (speedometer / minimap bezel): white ring, radial-filled at runtime (Image.Type.Filled)
ring = Image.new('RGBA', (1024, 1024), (0, 0, 0, 0)); ImageDraw.Draw(ring).ellipse([8, 8, 1015, 1015], outline=(255, 255, 255, 255), width=72)
plain('ring', ring.resize((256, 256), Image.LANCZOS), 256)
thin = Image.new('RGBA', (1024, 1024), (0, 0, 0, 0)); ImageDraw.Draw(thin).ellipse([8, 8, 1015, 1015], outline=(255, 255, 255, 255), width=16)
plain('ring_thin', thin.resize((256, 256), Image.LANCZOS), 256)
# disc with a soft shadow (round glass bezel behind the speedometer / minimap)
disc = Image.new('RGBA', (256, 256), (0, 0, 0, 0))
sh = Image.new('L', (256, 256), 0); ImageDraw.Draw(sh).ellipse([16, 20, 240, 244], fill=90); sh = sh.filter(ImageFilter.GaussianBlur(7))
so = Image.new('RGBA', (256, 256), (0, 0, 0, 0)); so.putalpha(sh); disc.alpha_composite(so)
d4 = Image.new('RGBA', (1024, 1024), (0, 0, 0, 0)); dd4 = ImageDraw.Draw(d4)
dd4.ellipse([64, 64, 959, 959], fill=(20, 22, 25, 235), outline=(255, 255, 255, 22), width=4)
disc.alpha_composite(d4.resize((256, 256), Image.LANCZOS))
plain('disc', disc, 256)


# ------------------------------------------------------------------ icons (128 px, white, tinted at runtime)
N = 128; SS = 4; S = N * SS
W = (255, 255, 255, 255); W2 = (255, 255, 255, 150); W3 = (255, 255, 255, 90)
ICON_NAMES = []


class Ico:
    def __init__(self):
        self.im = Image.new('RGBA', (S, S), (0, 0, 0, 0)); self.d = ImageDraw.Draw(self.im)

    def P(self, pts, fill=W, outline=None, w=0):
        self.d.polygon([(x * SS, y * SS) for x, y in pts], fill=fill, outline=outline, width=w)
    def E(self, box, fill=W, outline=None, w=0):
        self.d.ellipse([v * SS for v in box], fill=fill, outline=outline, width=w * SS)
    def R(self, box, fill=W, r=0, outline=None, w=0):
        self.d.rounded_rectangle([v * SS for v in box], radius=r * SS, fill=fill, outline=outline, width=w * SS)
    def L(self, pts, w=6, fill=W):
        self.d.line([(x * SS, y * SS) for x, y in pts], fill=fill, width=w * SS, joint='curve')
    def A(self, box, a0, a1, w=6, fill=W):
        self.d.arc([v * SS for v in box], a0, a1, fill=fill, width=w * SS)
    def PS(self, box, a0, a1, fill=W):
        self.d.pieslice([v * SS for v in box], a0, a1, fill=fill)
    def HL(self, box, r, fill):
        """Translucent highlight composited over what is already drawn (ImageDraw would replace the pixels)."""
        layer = Image.new('RGBA', (S, S), (0, 0, 0, 0)); ImageDraw.Draw(layer).rounded_rectangle([v * SS for v in box], radius=r * SS, fill=fill)
        self.im.alpha_composite(layer); self.d = ImageDraw.Draw(self.im)
    def HLE(self, box, fill):
        layer = Image.new('RGBA', (S, S), (0, 0, 0, 0)); ImageDraw.Draw(layer).ellipse([v * SS for v in box], fill=fill)
        self.im.alpha_composite(layer); self.d = ImageDraw.Draw(self.im)
    def cut(self, pts):
        self.d.polygon([(x * SS, y * SS) for x, y in pts], fill=(0, 0, 0, 0))
    def cutE(self, box):
        self.d.ellipse([v * SS for v in box], fill=(0, 0, 0, 0))
    def T(self, text, size, cx, cy, fill=W):
        f = font(size * SS); b = self.d.textbbox((0, 0), text, font=f)
        self.d.text((cx * SS - (b[2] - b[0]) / 2 - b[0], cy * SS - (b[3] - b[1]) / 2 - b[1]), text, font=f, fill=fill)
    def done(self, name):
        im = self.im.resize((N, N), Image.LANCZOS)
        p = os.path.join(ICONS, name + '.png'); im.save(p, optimize=True)
        write_meta(p, os.path.relpath(p, ROOT), 'sprite', (0, 0, 0, 0), 128)
        ICON_NAMES.append(name)


def icon(name):
    def deco(fn):
        i = Ico(); fn(i); i.done(name)
        return fn
    return deco


def star(i, cx, cy, r, fill=W):
    pts = []
    for k in range(10):
        a = -math.pi / 2 + k * math.pi / 5; rr = r if k % 2 == 0 else r * 0.45
        pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))
    i.P(pts, fill)


def leaf(i, x0, y0, x1, y1, w, fill=W):
    """Pointed leaf between two points."""
    dx, dy = x1 - x0, y1 - y0; l = math.hypot(dx, dy) or 1; nx, ny = -dy / l * w, dx / l * w
    mx, my = (x0 + x1) / 2, (y0 + y1) / 2
    i.P([(x0, y0), (mx + nx, my + ny), (x1, y1), (mx - nx, my - ny)], fill)


# --- economy / resources
@icon('money')
def _(i):
    i.R([14, 36, 114, 92], W, 8); i.cut([(22, 44), (106, 44), (106, 84), (22, 84)])
    i.E([46, 46, 82, 82]); i.cutE([54, 54, 74, 74]); i.E([26, 58, 38, 70], W2); i.E([90, 58, 102, 70], W2)

@icon('coins')
def _(i):
    for k, y in enumerate((88, 70, 52)):
        i.E([24, y - 14, 104, y + 14], W if k == 2 else W2); i.cutE([30, y - 10, 98, y + 10]) if k < 2 else None
    i.E([24, 38, 104, 66]); i.E([34, 44, 94, 60], W3)

@icon('loan')
def _(i):
    i.R([16, 30, 112, 98], W, 6); i.cut([(24, 38), (104, 38), (104, 90), (24, 90)])
    i.T('%', 44, 64, 64)

@icon('contract')
def _(i):
    i.P([(30, 14), (84, 14), (102, 32), (102, 114), (30, 114)]); i.cut([(84, 14), (84, 32), (102, 32)])
    for y in (46, 62, 78): i.L([(42, y), (90, y)], 5, (0, 0, 0, 0))
    i.L([(42, 96), (66, 96)], 5, (0, 0, 0, 0))

@icon('fuel')
def _(i):
    i.R([24, 20, 80, 110], W, 8); i.cut([(32, 28), (72, 28), (72, 54), (32, 54)])
    i.L([(80, 44), (96, 44), (96, 86)], 7); i.E([88, 82, 104, 98]); i.cutE([93, 87, 99, 93])
    i.R([18, 106, 86, 116], W, 3)

@icon('water')
def _(i):
    i.P([(64, 12), (100, 70), (96, 92), (80, 108), (48, 108), (32, 92), (28, 70)]); i.E([28, 60, 100, 112])
    i.A([44, 72, 92, 100], 90, 180, 6, (0, 0, 0, 0))

@icon('drop')
def _(i):
    i.P([(64, 20), (92, 72), (36, 72)]); i.E([36, 56, 92, 108]); i.E([48, 76, 60, 94], (0, 0, 0, 0))

@icon('electricity')
def _(i):
    i.P([(72, 10), (36, 70), (60, 70), (52, 118), (94, 54), (68, 54)])

@icon('solar')
def _(i):
    i.P([(30, 40), (100, 40), (112, 84), (18, 84)]); i.L([(64, 40), (64, 84)], 4, (0, 0, 0, 0)); i.L([(24, 62), (106, 62)], 4, (0, 0, 0, 0))
    i.L([(64, 84), (64, 108)], 8); i.L([(40, 110), (88, 110)], 8); i.E([84, 10, 112, 38], W2)

@icon('warehouse')
def _(i):
    i.P([(16, 56), (64, 20), (112, 56), (112, 108), (16, 108)]); i.cut([(32, 64), (96, 64), (96, 108), (32, 108)])
    i.R([40, 72, 62, 90], W2, 2); i.R([66, 72, 88, 90], W2, 2); i.R([40, 92, 88, 108], W2, 2)

@icon('seed_bag')
def _(i):
    i.P([(40, 36), (88, 36), (104, 108), (24, 108)]); i.R([44, 18, 84, 40], W, 6); i.cut([(48, 26), (80, 26), (80, 34), (48, 34)])
    i.E([54, 60, 74, 88], (0, 0, 0, 0)); i.L([(64, 62), (64, 86)], 4)

@icon('worker')
def _(i):
    i.E([44, 14, 84, 54]); i.P([(40, 40), (88, 40), (84, 48), (44, 48)], W2); i.P([(20, 116), (28, 70), (100, 70), (108, 116)]); i.R([48, 60, 80, 70], W, 3)

@icon('friends')
def _(i):
    i.E([24, 22, 60, 58]); i.P([(10, 108), (16, 68), (68, 68), (74, 108)]); i.E([72, 30, 104, 62], W2); i.P([(70, 108), (74, 72), (112, 72), (118, 108)], W2)

@icon('chat')
def _(i):
    i.R([14, 22, 114, 90], W, 16); i.P([(34, 86), (30, 112), (58, 86)]); i.E([38, 50, 50, 62], (0, 0, 0, 0)); i.E([58, 50, 70, 62], (0, 0, 0, 0)); i.E([78, 50, 90, 62], (0, 0, 0, 0))

# --- time / weather
@icon('clock')
def _(i):
    i.E([14, 14, 114, 114]); i.cutE([24, 24, 104, 104]); i.L([(64, 36), (64, 66), (86, 80)], 7); i.E([59, 59, 69, 69])

@icon('calendar')
def _(i):
    i.R([16, 26, 112, 112], W, 8); i.cut([(24, 50), (104, 50), (104, 104), (24, 104)]); i.L([(40, 14), (40, 36)], 8); i.L([(88, 14), (88, 36)], 8)
    for r in range(2):
        for c in range(4): i.R([30 + c * 18, 58 + r * 20, 42 + c * 18, 70 + r * 20], W2, 2)

@icon('weather_sun')
def _(i):
    i.E([36, 36, 92, 92])
    for k in range(8):
        a = k * math.pi / 4; i.L([(64 + math.cos(a) * 36, 64 + math.sin(a) * 36), (64 + math.cos(a) * 52, 64 + math.sin(a) * 52)], 8)

@icon('weather_cloud')
def _(i):
    i.E([20, 60, 64, 104]); i.E([44, 36, 100, 92]); i.E([76, 58, 116, 98]); i.R([40, 78, 100, 104], W, 4)

@icon('weather_rain')
def _(i):
    i.E([20, 40, 64, 84]); i.E([44, 20, 100, 76]); i.E([76, 42, 116, 82]); i.R([40, 60, 100, 84], W, 4)
    for x in (40, 64, 88): i.L([(x + 4, 92), (x - 4, 114)], 6)

@icon('weather_heat')
def _(i):
    i.E([40, 20, 88, 68])
    for k in range(8):
        a = k * math.pi / 4; i.L([(64 + math.cos(a) * 30, 44 + math.sin(a) * 30), (64 + math.cos(a) * 42, 44 + math.sin(a) * 42)], 6)
    for y in (86, 102): i.L([(20, y), (40, y - 8), (60, y), (80, y - 8), (100, y)], 6)

@icon('wind')
def _(i):
    i.L([(16, 44), (84, 44)], 7); i.A([76, 30, 104, 58], 270, 450, 7); i.L([(16, 72), (104, 72)], 7); i.A([96, 72, 120, 96], 180, 360, 7); i.L([(30, 98), (76, 98)], 7)

@icon('temperature')
def _(i):
    i.R([50, 10, 78, 84], W, 14); i.E([38, 72, 90, 124]); i.cut([(56, 20), (72, 20), (72, 80), (56, 80)]); i.cutE([46, 80, 82, 116]); i.R([60, 44, 68, 92], W, 4); i.E([50, 84, 78, 112])

@icon('season')
def _(i):
    leaf(i, 64, 110, 64, 20, 28); i.L([(64, 110), (64, 40)], 4, (0, 0, 0, 0))

# --- crops
def wheat(i, cx, top, h, fill=W):
    i.L([(cx, top + h), (cx, top + 10)], 5, fill)
    for k in range(5):
        y = top + 10 + k * 10
        leaf(i, cx, y, cx - 14, y - 10, 5, fill); leaf(i, cx, y, cx + 14, y - 10, 5, fill)
    leaf(i, cx, top + 10, cx, top - 6, 4, fill)

@icon('crop_wheat')
def _(i):
    wheat(i, 40, 30, 90, W2); wheat(i, 64, 16, 104); wheat(i, 88, 30, 90, W2)

@icon('crop_potato')
def _(i):
    i.E([22, 44, 106, 100]); i.E([26, 40, 70, 84], W); i.E([40, 58, 48, 66], (0, 0, 0, 0)); i.E([70, 72, 78, 80], (0, 0, 0, 0)); i.E([84, 56, 90, 62], (0, 0, 0, 0))

@icon('crop_onion')
def _(i):
    i.E([28, 40, 100, 112]); i.P([(50, 44), (64, 10), (78, 44)]); i.L([(64, 112), (64, 60)], 4, (0, 0, 0, 0)); i.A([30, 44, 98, 112], 110, 250, 4, (0, 0, 0, 0))

@icon('crop_watermelon')
def _(i):
    i.PS([14, 14, 114, 114], 0, 180); i.PS([26, 26, 102, 102], 0, 180, W2)
    for x, y in ((50, 70), (64, 84), (78, 70), (64, 60)): i.E([x - 4, y - 6, x + 4, y + 6], (0, 0, 0, 0))

@icon('crop_dates')
def _(i):
    i.L([(64, 118), (64, 50)], 6)
    for a, l in ((-150, 48), (-120, 52), (-90, 56), (-60, 52), (-30, 48)):
        r = math.radians(a); leaf(i, 64, 50, 64 + math.cos(r) * l, 50 + math.sin(r) * l * 0.8 + 6, 7)
    for k in range(5): i.E([50 + (k % 3) * 10, 58 + (k // 3) * 12, 60 + (k % 3) * 10, 72 + (k // 3) * 12], W2)

@icon('crop_rice')
def _(i):
    i.L([(64, 118), (64, 30)], 5); i.L([(64, 70), (40, 30)], 5); i.L([(64, 70), (90, 36)], 5)
    for x, y in ((40, 30), (34, 44), (46, 44), (90, 36), (84, 50), (96, 50), (64, 30), (58, 44), (70, 44)): i.E([x - 5, y - 8, x + 5, y + 8])

@icon('crop_cotton')
def _(i):
    i.L([(64, 118), (64, 70)], 6); i.E([34, 40, 70, 76]); i.E([58, 40, 94, 76]); i.E([46, 24, 82, 60]); i.E([42, 58, 86, 94])
    i.P([(64, 90), (50, 112), (78, 112)], W2)

@icon('crop_berseem')
def _(i):
    i.L([(64, 118), (64, 60)], 5)
    for cx, cy in ((48, 46), (80, 46), (64, 28)): i.E([cx - 16, cy - 16, cx + 16, cy + 16])
    i.E([54, 44, 74, 64], W)

@icon('crop_maize')
def _(i):
    i.E([42, 14, 86, 100]); leaf(i, 64, 118, 30, 60, 12, W2); leaf(i, 64, 118, 98, 60, 12, W2)
    for r in range(4):
        for c in range(3): i.E([50 + c * 10, 26 + r * 16, 56 + c * 10, 36 + r * 16], (0, 0, 0, 0))

@icon('crop_alfalfa')
def _(i):
    i.L([(64, 118), (64, 50)], 5)
    for cx, cy in ((44, 54), (84, 54), (64, 34)):
        for a in range(3):
            r = math.radians(90 + a * 120); leaf(i, cx, cy, cx + math.cos(r) * 18, cy - math.sin(r) * 18, 8)

@icon('crop_tomato')
def _(i):
    i.E([22, 34, 106, 112]); leaf(i, 64, 40, 40, 22, 6); leaf(i, 64, 40, 88, 22, 6); i.L([(64, 42), (64, 20)], 5); i.E([40, 52, 56, 68], W3)

@icon('crop_cucumber')
def _(i):
    i.R([14, 50, 114, 86], W, 18); i.R([24, 58, 104, 78], W2, 12); i.L([(14, 68), (6, 60)], 6)

@icon('crop_generic')
def _(i):
    i.L([(64, 118), (64, 50)], 6); leaf(i, 64, 90, 30, 60, 12); leaf(i, 64, 90, 98, 60, 12); leaf(i, 64, 56, 64, 14, 14)

# --- animals (simple profiles)
@icon('animal_cow')
def _(i):
    i.R([22, 46, 98, 92], W, 16); i.E([78, 36, 118, 76]); i.E([96, 60, 118, 82], W2); i.L([(90, 30), (104, 42)], 6); i.L([(118, 30), (110, 42)], 6)
    for x in (32, 50, 70, 88): i.R([x, 86, x + 10, 112], W, 3)
    i.E([40, 58, 60, 78], (0, 0, 0, 0))

@icon('animal_sheep')
def _(i):
    for cx, cy in ((36, 60), (56, 50), (78, 52), (94, 64), (60, 76), (40, 80), (84, 80)): i.E([cx - 18, cy - 18, cx + 18, cy + 18])
    i.E([92, 40, 124, 66]); i.E([98, 36, 108, 46], W2)
    for x in (34, 54, 76, 96): i.R([x, 92, x + 9, 112], W, 3)

@icon('animal_chicken')
def _(i):
    i.E([30, 50, 96, 104]); i.E([74, 24, 108, 58]); i.P([(106, 44), (122, 50), (106, 56)]); i.P([(84, 22), (90, 10), (96, 22), (102, 12), (106, 26)])
    i.P([(30, 60), (6, 50), (10, 70), (14, 60), (30, 80)]); i.L([(54, 104), (54, 118)], 5); i.L([(72, 104), (72, 118)], 5); i.E([88, 36, 94, 42], (0, 0, 0, 0))

@icon('animal_goat')
def _(i):
    i.R([24, 50, 94, 90], W, 14); i.E([82, 34, 114, 70]); i.L([(96, 36), (104, 16)], 6); i.L([(108, 38), (118, 20)], 6); i.L([(108, 68), (108, 84)], 5)
    for x in (32, 48, 70, 84): i.R([x, 86, x + 9, 112], W, 3)

@icon('animal_camel')
def _(i):
    i.E([22, 50, 60, 84]); i.E([50, 42, 88, 78]); i.R([20, 62, 92, 92], W, 12); i.L([(90, 76), (104, 40), (120, 36)], 10); i.E([106, 28, 124, 46])
    for x in (28, 44, 70, 84): i.R([x, 88, x + 8, 116], W, 3)

@icon('animal_horse')
def _(i):
    i.R([22, 50, 92, 88], W, 14); i.L([(88, 60), (104, 30), (118, 36)], 11); i.E([102, 24, 124, 46]); i.L([(92, 30), (104, 44), (112, 26)], 5, W2); i.L([(22, 56), (10, 84)], 6, W2)
    for x in (28, 44, 72, 84): i.R([x, 84, x + 8, 116], W, 3)

@icon('animal_dog')
def _(i):
    i.R([28, 56, 90, 90], W, 14); i.E([76, 36, 112, 72]); i.P([(80, 36), (76, 18), (90, 36)]); i.L([(28, 64), (14, 44)], 7)
    for x in (34, 50, 70, 82): i.R([x, 86, x + 8, 112], W, 3)

@icon('animal_cat')
def _(i):
    i.E([34, 54, 94, 108]); i.E([42, 22, 86, 66]); i.P([(44, 34), (42, 10), (58, 26)]); i.P([(84, 34), (86, 10), (70, 26)]); i.L([(94, 90), (116, 70), (118, 90)], 7)
    i.E([54, 38, 60, 46], (0, 0, 0, 0)); i.E([68, 38, 74, 46], (0, 0, 0, 0))

@icon('animal_fish')
def _(i):
    i.E([14, 44, 94, 84]); i.P([(88, 64), (122, 40), (122, 88)]); i.E([30, 54, 40, 64], (0, 0, 0, 0)); i.P([(50, 44), (64, 24), (74, 44)], W2)

@icon('animal_bee')
def _(i):
    i.E([32, 46, 96, 94]); i.E([18, 54, 44, 86]); i.E([44, 20, 76, 50], W2); i.E([60, 20, 92, 50], W2)
    for x in (50, 66, 82): i.L([(x, 46), (x, 94)], 5, (0, 0, 0, 0))

@icon('egg')
def _(i):
    i.E([32, 20, 96, 110]); i.E([44, 18, 84, 76], W)

@icon('milk')
def _(i):
    i.P([(40, 40), (88, 40), (98, 60), (98, 112), (30, 112), (30, 60)]); i.R([48, 16, 80, 44], W, 4); i.R([40, 74, 88, 100], W2, 4)

# --- vehicles / implements
@icon('vehicle_tractor')
def _(i):
    i.R([44, 22, 84, 62], W, 4); i.R([18, 56, 104, 86], W, 6); i.E([14, 68, 62, 116]); i.E([84, 82, 118, 116]); i.cutE([28, 82, 48, 102]); i.cutE([94, 92, 108, 106])
    i.L([(96, 56), (96, 24)], 7); i.R([50, 30, 78, 52], W3, 2)

@icon('vehicle_truck')
def _(i):
    i.R([10, 40, 70, 90], W, 6); i.R([66, 56, 118, 90], W, 6); i.E([18, 80, 48, 110]); i.E([80, 80, 110, 110]); i.cutE([28, 90, 38, 100]); i.cutE([90, 90, 100, 100]); i.R([76, 62, 110, 84], W3, 2)

@icon('vehicle_harvester')
def _(i):
    i.R([34, 20, 94, 60], W, 4); i.R([24, 54, 110, 84], W, 6); i.P([(10, 70), (30, 70), (30, 100), (6, 100)]); i.E([30, 78, 66, 114]); i.E([84, 84, 112, 112]); i.cutE([42, 90, 54, 102])
    i.L([(94, 30), (112, 10)], 7); i.R([42, 28, 86, 52], W3, 2)

@icon('vehicle_pickup')
def _(i):
    i.P([(10, 60), (36, 60), (50, 36), (82, 36), (82, 60), (118, 60), (118, 90), (10, 90)]); i.E([20, 78, 50, 108]); i.E([80, 78, 110, 108]); i.cutE([30, 88, 40, 98]); i.cutE([90, 88, 100, 98]); i.P([(52, 42), (78, 42), (78, 58), (44, 58)], W3)

@icon('vehicle_boat')
def _(i):
    i.P([(10, 74), (118, 74), (100, 104), (28, 104)]); i.L([(64, 74), (64, 14)], 6); i.P([(70, 18), (112, 68), (70, 68)], W2)

@icon('implement_plow')
def _(i):
    i.L([(20, 30), (60, 30), (60, 70)], 9)
    for x in (36, 60, 84): i.P([(x - 6, 70), (x + 6, 70), (x + 14, 100), (x - 2, 110), (x - 10, 86)])
    i.L([(36, 70), (84, 70)], 9); i.E([12, 20, 32, 40])

@icon('implement_seeder')
def _(i):
    i.R([24, 30, 104, 62], W, 6); i.L([(24, 62), (104, 62)], 6)
    for x in (34, 54, 74, 94): i.L([(x, 62), (x, 98)], 7); i.E([x - 6, 94, x + 6, 106])
    i.L([(10, 44), (24, 44)], 7)

@icon('implement_sprayer')
def _(i):
    i.R([44, 34, 84, 80], W, 10); i.L([(10, 60), (118, 60)], 7)
    for x in (16, 36, 92, 112): i.L([(x, 60), (x, 80)], 5); i.P([(x - 8, 100), (x + 8, 100), (x, 82)], W2)

@icon('implement_trailer')
def _(i):
    i.P([(16, 44), (104, 44), (112, 84), (8, 84)]); i.E([30, 80, 58, 108]); i.E([70, 80, 98, 108]); i.cutE([40, 90, 48, 98]); i.cutE([80, 90, 88, 98]); i.L([(104, 70), (122, 70)], 7)

@icon('implement_cultivator')
def _(i):
    i.L([(14, 40), (114, 40)], 9)
    for x in (24, 44, 64, 84, 104): i.L([(x, 40), (x, 76)], 6); i.P([(x - 8, 74), (x + 8, 74), (x, 100)])

@icon('irrigation')
def _(i):
    i.L([(14, 90), (114, 90)], 10)
    for x in (34, 64, 94):
        i.L([(x, 90), (x, 60)], 6); i.P([(x, 40), (x + 10, 58), (x - 10, 58)]); i.E([x - 10, 50, x + 10, 68])

# --- places (map POIs)
@icon('poi_farm')
def _(i):
    i.P([(14, 60), (64, 20), (114, 60), (114, 108), (14, 108)]); i.cut([(50, 70), (78, 70), (78, 108), (50, 108)]); i.R([24, 70, 42, 86], W3, 2); i.R([86, 70, 104, 86], W3, 2)

@icon('poi_market')
def _(i):
    i.P([(10, 46), (118, 46), (110, 70), (18, 70)])
    for k in range(4): i.P([(10 + k * 27, 46), (37 + k * 27, 46), (37 + k * 27, 34), (10 + k * 27, 34)], W if k % 2 else W2)
    i.R([20, 70, 108, 108], W, 4); i.cut([(50, 80), (78, 80), (78, 108), (50, 108)])

@icon('poi_shop')
def _(i):
    i.P([(14, 48), (64, 20), (114, 48), (114, 108), (14, 108)]); i.cut([(24, 56), (104, 56), (104, 100), (24, 100)]); i.R([56, 70, 72, 100], W, 2); i.R([30, 64, 50, 82], W2, 2); i.R([78, 64, 98, 82], W2, 2)

@icon('poi_fuel')
def _(i):
    i.R([24, 20, 80, 110], W, 8); i.cut([(32, 28), (72, 28), (72, 54), (32, 54)]); i.L([(80, 44), (96, 44), (96, 86)], 7); i.E([88, 82, 104, 98]); i.cutE([93, 87, 99, 93])

@icon('poi_well')
def _(i):
    i.E([20, 70, 108, 110]); i.cutE([32, 78, 96, 102]); i.L([(28, 72), (28, 30)], 7); i.L([(100, 72), (100, 30)], 7); i.P([(16, 32), (64, 10), (112, 32)]); i.L([(64, 30), (64, 60)], 4); i.R([54, 58, 74, 72], W2, 2)

@icon('poi_barn')
def _(i):
    i.P([(14, 56), (30, 30), (64, 16), (98, 30), (114, 56), (114, 108), (14, 108)]); i.cut([(46, 66), (82, 66), (82, 108), (46, 108)]); i.L([(46, 66), (82, 108)], 4); i.L([(82, 66), (46, 108)], 4)

@icon('poi_silo')
def _(i):
    i.R([34, 30, 94, 112], W, 6); i.E([34, 14, 94, 46]); i.L([(34, 60), (94, 60)], 4, (0, 0, 0, 0)); i.L([(34, 84), (94, 84)], 4, (0, 0, 0, 0))

@icon('poi_greenhouse')
def _(i):
    i.P([(12, 108), (12, 60), (64, 20), (116, 60), (116, 108)]); i.cut([(22, 64), (64, 32), (106, 64), (106, 100), (22, 100)]); i.L([(64, 32), (64, 100)], 4); i.L([(22, 64), (106, 64)], 4); i.L([(22, 82), (106, 82)], 4)

@icon('poi_town')
def _(i):
    i.R([12, 60, 44, 110], W); i.R([40, 36, 80, 110], W); i.R([76, 52, 116, 110], W2)
    for x in (20, 30): i.R([x, 70, x + 6, 78], (0, 0, 0, 0)); i.R([x, 86, x + 6, 94], (0, 0, 0, 0))
    for x in (48, 60, 70): i.R([x, 46, x + 6, 54], (0, 0, 0, 0)); i.R([x, 62, x + 6, 70], (0, 0, 0, 0)); i.R([x, 78, x + 6, 86], (0, 0, 0, 0))

@icon('poi_bank')
def _(i):
    i.P([(14, 44), (64, 16), (114, 44)]); i.R([20, 44, 108, 54], W)
    for x in (26, 50, 74, 98): i.R([x, 56, x + 10, 96], W)
    i.R([14, 98, 114, 110], W)

@icon('poi_workshop')
def _(i):
    i.L([(30, 98), (84, 44)], 12); i.E([74, 14, 118, 58]); i.cutE([86, 26, 106, 46]); i.cut([(96, 36), (124, 10), (124, 40)]); i.E([18, 86, 44, 112])

@icon('poi_port')
def _(i):
    i.E([54, 14, 74, 34]); i.cutE([60, 20, 68, 28]); i.L([(64, 34), (64, 100)], 7); i.L([(40, 50), (88, 50)], 7); i.A([20, 50, 108, 118], 20, 160, 8)

@icon('poi_field')
def _(i):
    i.P([(20, 100), (44, 30), (84, 30), (108, 100)])
    for k in range(3): i.L([(48 + k * 10 - 4, 30), (34 + k * 22, 100)], 4, (0, 0, 0, 0))
    i.L([(26, 82), (102, 82)], 4, (0, 0, 0, 0)); i.L([(36, 56), (92, 56)], 4, (0, 0, 0, 0))

@icon('poi_home')
def _(i):
    i.P([(14, 62), (64, 18), (114, 62)]); i.R([26, 58, 102, 110], W); i.cut([(54, 76), (74, 76), (74, 110), (54, 110)]); i.R([82, 26, 94, 50], W)

@icon('poi_cooperative')
def _(i):
    i.R([14, 50, 114, 110], W, 4); i.P([(8, 52), (64, 18), (120, 52)]); i.cut([(24, 60), (104, 60), (104, 102), (24, 102)]); i.E([52, 66, 76, 90]); i.R([60, 90, 68, 102], W)

@icon('poi_pigeon_tower')
def _(i):
    i.P([(34, 110), (44, 30), (84, 30), (94, 110)]); i.E([44, 14, 84, 46])
    for r in range(3):
        for c in range(2): i.E([50 + c * 18, 50 + r * 18, 60 + c * 18, 60 + r * 18], (0, 0, 0, 0))

@icon('poi_canal')
def _(i):
    i.L([(10, 40), (40, 30), (88, 50), (118, 40)], 8); i.L([(10, 64), (40, 54), (88, 74), (118, 64)], 8); i.L([(10, 88), (40, 78), (88, 98), (118, 88)], 8)

@icon('poi_oasis')
def _(i):
    i.L([(44, 110), (52, 50)], 6)
    for a, l in ((-160, 36), (-120, 40), (-80, 40), (-40, 36), (0, 30)):
        r = math.radians(a); leaf(i, 52, 50, 52 + math.cos(r) * l, 50 + math.sin(r) * l * 0.8 + 6, 6)
    i.E([12, 98, 116, 122], W2)

# --- settings categories
@icon('settings')
def _(i):
    for k in range(8):
        a = k * math.pi / 4; i.L([(64 + math.cos(a) * 36, 64 + math.sin(a) * 36), (64 + math.cos(a) * 52, 64 + math.sin(a) * 52)], 16)
    i.E([24, 24, 104, 104]); i.cutE([48, 48, 80, 80])

@icon('graphics')
def _(i):
    i.R([12, 22, 116, 92], W, 8); i.cut([(22, 32), (106, 32), (106, 82), (22, 82)]); i.P([(24, 80), (50, 50), (66, 66), (82, 44), (104, 80)], W2); i.E([84, 38, 96, 50], W2); i.R([44, 98, 84, 108], W, 3)

@icon('audio')
def _(i):
    i.P([(16, 48), (40, 48), (68, 22), (68, 106), (40, 80), (16, 80)]); i.A([50, 36, 100, 92], -50, 50, 7); i.A([50, 20, 118, 108], -50, 50, 7)

@icon('controls')
def _(i):
    i.R([10, 36, 118, 92], W, 24); i.cut([(32, 56), (56, 56), (56, 72), (32, 72)]); i.R([40, 48, 48, 80], W); i.E([84, 48, 96, 60], (0, 0, 0, 0)); i.E([98, 58, 110, 70], (0, 0, 0, 0)); i.E([70, 58, 82, 70], (0, 0, 0, 0)); i.E([84, 68, 96, 80], (0, 0, 0, 0))

@icon('gamepad')
def _(i):
    i.R([10, 36, 118, 92], W, 24); i.cut([(32, 56), (56, 56), (56, 72), (32, 72)]); i.R([40, 48, 48, 80], W); i.E([84, 48, 96, 60], (0, 0, 0, 0)); i.E([98, 58, 110, 70], (0, 0, 0, 0)); i.E([70, 58, 82, 70], (0, 0, 0, 0)); i.E([84, 68, 96, 80], (0, 0, 0, 0))
    i.P([(20, 86), (36, 112), (48, 108), (40, 86)]); i.P([(108, 86), (92, 112), (80, 108), (88, 86)])

@icon('keyboard')
def _(i):
    i.R([8, 34, 120, 94], W, 8); i.cut([(16, 42), (112, 42), (112, 86), (16, 86)])
    for r in range(2):
        for c in range(6): i.R([22 + c * 15, 48 + r * 14, 32 + c * 15, 58 + r * 14], W)
    i.R([34, 76, 94, 84], W)

@icon('mouse')
def _(i):
    i.R([34, 12, 94, 116], W, 30); i.L([(64, 12), (64, 56)], 4, (0, 0, 0, 0)); i.L([(34, 56), (94, 56)], 4, (0, 0, 0, 0)); i.R([60, 26, 68, 44], (0, 0, 0, 0), 3)

@icon('accessibility')
def _(i):
    i.E([54, 10, 74, 30]); i.L([(24, 44), (104, 44)], 8); i.L([(64, 40), (64, 76)], 9); i.L([(64, 76), (40, 116)], 8); i.L([(64, 76), (88, 116)], 8)

@icon('language')
def _(i):
    i.E([14, 14, 114, 114]); i.cutE([22, 22, 106, 106]); i.E([44, 22, 84, 106], (0, 0, 0, 0)); i.A([44, 22, 84, 106], 0, 360, 4); i.L([(22, 64), (106, 64)], 4); i.L([(30, 42), (98, 42)], 4); i.L([(30, 86), (98, 86)], 4); i.L([(64, 22), (64, 106)], 4)

@icon('save')
def _(i):
    i.R([16, 16, 112, 112], W, 6); i.cut([(36, 24), (92, 24), (92, 50), (36, 50)]); i.cut([(30, 66), (98, 66), (98, 104), (30, 104)]); i.R([74, 28, 86, 46], W); i.R([38, 74, 90, 96], W2, 2)

@icon('load')
def _(i):
    i.P([(12, 36), (48, 36), (58, 48), (116, 48), (116, 108), (12, 108)]); i.cut([(20, 56), (108, 56), (108, 100), (20, 100)]); i.P([(64, 60), (82, 80), (70, 80), (70, 96), (58, 96), (58, 80), (46, 80)])

@icon('trash')
def _(i):
    i.R([28, 36, 100, 112], W, 6); i.R([20, 24, 108, 36], W, 3); i.R([50, 14, 78, 28], W, 3); i.L([(48, 48), (50, 100)], 6, (0, 0, 0, 0)); i.L([(64, 48), (64, 100)], 6, (0, 0, 0, 0)); i.L([(80, 48), (78, 100)], 6, (0, 0, 0, 0))

@icon('info')
def _(i):
    i.E([14, 14, 114, 114]); i.cutE([24, 24, 104, 104]); i.E([57, 34, 71, 48]); i.R([57, 56, 71, 94], W, 4)

@icon('help')
def _(i):
    i.E([14, 14, 114, 114]); i.cutE([24, 24, 104, 104]); i.T('?', 64, 64, 62)

@icon('check')
def _(i):
    i.L([(24, 66), (54, 96), (106, 36)], 14)

@icon('cross')
def _(i):
    i.L([(30, 30), (98, 98)], 14); i.L([(98, 30), (30, 98)], 14)

@icon('plus')
def _(i):
    i.L([(64, 22), (64, 106)], 14); i.L([(22, 64), (106, 64)], 14)

@icon('minus')
def _(i):
    i.L([(22, 64), (106, 64)], 14)

for nm, rot in (('arrow_right', 0), ('arrow_down', 90), ('arrow_left', 180), ('arrow_up', 270)):
    def _f(i, rot=rot):
        pts = [(22, 64), (78, 64), (60, 40), (60, 50)]
        i.L([(26, 64), (96, 64)], 14); i.L([(66, 34), (98, 64), (66, 94)], 14)
    ii = Ico(); _f(ii); ii.im = ii.im.rotate(-rot); ii.done(nm)

@icon('chevron_left')
def _(i):
    i.L([(80, 24), (44, 64), (80, 104)], 14)

@icon('chevron_right')
def _(i):
    i.L([(48, 24), (84, 64), (48, 104)], 14)

@icon('lock')
def _(i):
    i.R([26, 56, 102, 112], W, 8); i.A([38, 16, 90, 76], 180, 360, 10); i.L([(38, 46), (38, 60)], 10); i.L([(90, 46), (90, 60)], 10); i.E([56, 72, 72, 88], (0, 0, 0, 0)); i.R([60, 80, 68, 98], (0, 0, 0, 0), 2)

@icon('star')
def _(i):
    star(i, 64, 66, 56)

@icon('trophy')
def _(i):
    i.P([(34, 20), (94, 20), (90, 66), (64, 80), (38, 66)]); i.A([10, 24, 46, 60], 90, 270, 8); i.A([82, 24, 118, 60], 270, 450, 8); i.R([56, 78, 72, 96], W); i.R([40, 96, 88, 110], W, 3)

for medal, shade in (('medal_gold', W), ('medal_silver', W2), ('medal_bronze', W3)):
    def _m(i, shade=shade):
        i.P([(40, 10), (64, 50), (88, 10), (100, 10), (74, 58), (54, 58), (28, 10)], W2); i.E([30, 50, 98, 118], shade); i.E([38, 58, 90, 110], W); star(i, 64, 86, 20, (0, 0, 0, 0))
    ii = Ico(); _m(ii); ii.done(medal)

@icon('achievement')
def _(i):
    i.P([(64, 10), (112, 30), (104, 84), (64, 118), (24, 84), (16, 30)]); star(i, 64, 62, 30, (0, 0, 0, 0))

@icon('map')
def _(i):
    i.P([(12, 32), (44, 20), (84, 36), (116, 24), (116, 96), (84, 108), (44, 92), (12, 104)]); i.L([(44, 20), (44, 92)], 4, (0, 0, 0, 0)); i.L([(84, 36), (84, 108)], 4, (0, 0, 0, 0))

@icon('pin')
def _(i):
    i.E([30, 10, 98, 78]); i.P([(34, 60), (94, 60), (64, 118)]); i.cutE([50, 30, 78, 58])

@icon('flag')
def _(i):
    i.L([(30, 14), (30, 116)], 8); i.P([(34, 18), (104, 30), (84, 48), (104, 68), (34, 76)])

@icon('compass')
def _(i):
    i.E([14, 14, 114, 114]); i.cutE([22, 22, 106, 106]); i.P([(64, 28), (76, 64), (64, 100), (52, 64)]); i.P([(28, 64), (64, 52), (100, 64), (64, 76)], W2)

@icon('pause')
def _(i):
    i.R([30, 22, 54, 106], W, 4); i.R([74, 22, 98, 106], W, 4)

@icon('play')
def _(i):
    i.P([(36, 20), (104, 64), (36, 108)])

@icon('photo')
def _(i):
    i.R([12, 34, 116, 102], W, 8); i.R([44, 22, 84, 36], W, 4); i.E([44, 48, 84, 88], (0, 0, 0, 0)); i.E([52, 56, 76, 80]); i.R([96, 44, 108, 54], (0, 0, 0, 0))

@icon('journal')
def _(i):
    i.R([22, 14, 106, 114], W, 6); i.cut([(36, 24), (96, 24), (96, 104), (36, 104)]); i.R([22, 14, 34, 114], W)
    for y in (40, 56, 72, 88): i.L([(46, y), (86, y)], 4)

@icon('quit')
def _(i):
    i.A([20, 20, 108, 108], -60, 240, 12); i.L([(64, 16), (64, 64)], 12)

@icon('home_menu')
def _(i):
    for y in (34, 64, 94): i.R([20, y - 7, 108, y + 7], W, 6)

@icon('sort')
def _(i):
    i.L([(22, 36), (106, 36)], 10); i.L([(22, 64), (86, 64)], 10); i.L([(22, 92), (66, 92)], 10)

@icon('search')
def _(i):
    i.E([16, 16, 84, 84]); i.cutE([28, 28, 72, 72]); i.L([(76, 76), (110, 110)], 14)

@icon('mazraa_leaf')
def _(i):
    # the "M" leaf mark used as a bullet / list icon
    leaf(i, 24, 110, 100, 22, 30); i.L([(24, 110), (100, 22)], 5, (0, 0, 0, 0))

# ------------------------------------------------------------------ controller glyphs (original drawings, 128 px)
GLYPH_NAMES = []


def glyph(name, fn):
    i = Ico(); fn(i)
    im = i.im.resize((N, N), Image.LANCZOS)
    p = os.path.join(GLYPHS, name + '.png'); im.save(p, optimize=True)
    write_meta(p, os.path.relpath(p, ROOT), 'sprite', (0, 0, 0, 0), 128)
    GLYPH_NAMES.append(name)


XBOX = {'a': (107, 203, 91), 'b': (255, 107, 107), 'x': (91, 168, 255), 'y': (255, 210, 77)}
for letter, col in XBOX.items():
    def _f(i, letter=letter, col=col):
        i.E([8, 8, 120, 120], (30, 30, 34, 255)); i.E([14, 14, 114, 114], col + (255,)); i.HLE([22, 22, 106, 70], (255, 255, 255, 60))
        i.T(letter.upper(), 64, 64, 62, (255, 255, 255, 255))
    glyph('xbox_' + letter, _f)
PS = {'cross': (124, 178, 232), 'circle': (255, 107, 107), 'square': (232, 155, 208), 'triangle': (79, 209, 165)}
for shape, col in PS.items():
    def _f(i, shape=shape, col=col):
        i.E([8, 8, 120, 120], (30, 30, 34, 255)); i.E([14, 14, 114, 114], (52, 52, 60, 255)); c = col + (255,)
        if shape == 'cross': i.L([(42, 42), (86, 86)], 9, c); i.L([(86, 42), (42, 86)], 9, c)
        elif shape == 'circle': i.A([40, 40, 88, 88], 0, 360, 9, c)
        elif shape == 'square': i.R([42, 42, 86, 86], (0, 0, 0, 0), 2, c, 8)
        else: i.P([(64, 36), (92, 86), (36, 86)], (0, 0, 0, 0), c, 8 * SS)
    glyph('ps_' + shape, _f)


def shoulder(label, trigger=False):
    def _f(i):
        i.R([8, 30, 120, 98], (30, 30, 34, 255), 22 if not trigger else 34); i.R([14, 36, 114, 92], (70, 70, 78, 255), 18 if not trigger else 30)
        i.HL([20, 42, 108, 64], 12, (255, 255, 255, 30)); i.T(label, 40, 64, 64, (255, 255, 255, 255))
    return _f


for lab in ('LB', 'RB', 'L1', 'R1'): glyph(('xbox_' if lab[1] == 'B' else 'ps_') + lab.lower(), shoulder(lab))
for lab in ('LT', 'RT', 'L2', 'R2'): glyph(('xbox_' if lab[1] == 'T' else 'ps_') + lab.lower(), shoulder(lab, True))


def pill(label):
    def _f(i):
        i.R([6, 40, 122, 88], (30, 30, 34, 255), 24); i.R([12, 46, 116, 82], (70, 70, 78, 255), 18); i.T(label, 26 if len(label) > 4 else 34, 64, 64, (255, 255, 255, 255))
    return _f


glyph('xbox_menu', pill('MENU')); glyph('xbox_view', pill('VIEW')); glyph('ps_options', pill('OPTIONS')); glyph('ps_share', pill('SHARE'))


def stick(label):
    def _f(i):
        i.E([8, 8, 120, 120], (30, 30, 34, 255)); i.E([16, 16, 112, 112], (70, 70, 78, 255)); i.E([30, 30, 98, 98], (40, 40, 46, 255)); i.T(label, 34, 64, 64, (255, 255, 255, 255))
    return _f


glyph('xbox_ls', stick('LS')); glyph('xbox_rs', stick('RS')); glyph('ps_l3', stick('L3')); glyph('ps_r3', stick('R3'))


def dpad(i):
    i.E([8, 8, 120, 120], (30, 30, 34, 255)); i.E([14, 14, 114, 114], (70, 70, 78, 255))
    i.R([50, 22, 78, 106], (36, 36, 42, 255), 6); i.R([22, 50, 106, 78], (36, 36, 42, 255), 6)
    for pts in ([(64, 30), (58, 40), (70, 40)], [(64, 98), (58, 88), (70, 88)], [(30, 64), (40, 58), (40, 70)], [(98, 64), (88, 58), (88, 70)]): i.P(pts, (255, 255, 255, 230))


glyph('xbox_dpad', dpad); glyph('ps_dpad', dpad)


def key(label):
    def _f(i):
        i.R([10, 14, 118, 114], (30, 30, 34, 255), 16); i.R([16, 20, 112, 104], (236, 230, 214, 255), 12); i.HL([22, 26, 106, 60], 8, (255, 255, 255, 120))
        i.T(label, 44 if len(label) == 1 else 26, 64, 62, (43, 27, 14, 255))
    return _f


for k in ('E', 'Q', 'R', 'F', 'Esc', 'Enter', 'Space', 'Tab', 'Shift', 'Ctrl', 'W', 'A', 'S', 'D', 'M', 'N', 'I', 'P'): glyph('key_' + k.lower(), key(k))

# ------------------------------------------------------------------ manifest (read by the C# UiKit and the tests)
manifest = {'borders': BORDERS, 'icons': sorted(ICON_NAMES), 'glyphs': sorted(GLYPH_NAMES)}
import json
mp = os.path.join(UI, 'kit_manifest.json')
with open(mp, 'w') as f:
    json.dump(manifest, f, indent=1)
if not os.path.exists(mp + '.meta'):
    with open(mp + '.meta', 'w') as f:
        f.write(f"fileFormatVersion: 2\nguid: {guid_for('Assets/Resources/UI/kit_manifest.json')}\nTextAssetImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
print('kit sprites', len(BORDERS), 'icons', len(ICON_NAMES), 'glyphs', len(GLYPH_NAMES))
