"""Mazraa brand kit (all original, drawn in code): the golden-hour farm illustration (Najd oasis and Nile Delta variants)
as layered 4K key art, loading-screen parallax layers, logo lockups (EN / AR), app icon, Microsoft Store tiles and
store art (key art, hero, box, poster). Run: python3 Branding/make_brand_kit.py   (Pillow with raqm, numpy)"""
import math, os, random, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from kitlib import *
import make_logo

STORE = os.path.join(ROOT, 'installer', 'store_art')
MSIX = os.path.join(ROOT, 'installer', 'msix', 'Assets')
RES_BRAND = os.path.join(ROOT, 'Assets', 'Resources', 'Branding')
LOADING = os.path.join(RES_BRAND, 'Loading')
for d in (STORE, MSIX, HERE):
    os.makedirs(d, exist_ok=True)
ensure_dir(LOADING)


# ------------------------------------------------------------------ illustration
def np_vgrad(w, h, stops):
    ys = np.linspace(0, 1, h)
    col = np.zeros((h, 4), np.float32)
    stops = sorted(stops)
    for i in range(len(stops) - 1):
        t0, c0 = stops[i]; t1, c1 = stops[i + 1]
        m = (ys >= t0) & (ys <= t1)
        u = np.clip((ys - t0) / max(1e-6, t1 - t0), 0, 1)[:, None]
        c0 = np.array(rgba(c0), np.float32); c1 = np.array(rgba(c1), np.float32)
        col = np.where(m[:, None], c0 * (1 - u) + c1 * u, col)
    out = np.repeat(col[:, None, :], w, axis=1)
    return Image.fromarray(out.astype(np.uint8), 'RGBA')


def radial_glow(w, h, cx, cy, r, color, alpha, power=2.2):
    yy, xx = np.mgrid[0:h, 0:w]
    d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / r
    a = np.clip(1 - d, 0, 1) ** power * alpha
    im = np.zeros((h, w, 4), np.uint8); im[..., 0] = color[0]; im[..., 1] = color[1]; im[..., 2] = color[2]; im[..., 3] = a.astype(np.uint8)
    return Image.fromarray(im, 'RGBA')


class Layer:
    """Supersampled RGBA drawing surface."""
    def __init__(self, w, h, ss=2):
        self.w, self.h, self.ss = w, h, ss
        self.im = Image.new('RGBA', (w * ss, h * ss), (0, 0, 0, 0)); self.d = ImageDraw.Draw(self.im)
    def s(self, pts): return [(x * self.ss, y * self.ss) for x, y in pts]
    def poly(self, pts, fill): self.d.polygon(self.s(pts), fill=rgba(fill))
    def line(self, pts, fill, w): self.d.line(self.s(pts), fill=rgba(fill), width=int(w * self.ss), joint='curve')
    def ell(self, box, fill): self.d.ellipse([v * self.ss for v in box], fill=rgba(fill))
    def composite(self, other): self.im.alpha_composite(other.im)
    def blur(self, r): self.im = self.im.filter(ImageFilter.GaussianBlur(r * self.ss)); self.d = ImageDraw.Draw(self.im)
    def out(self): return self.im.resize((self.w, self.h), Image.LANCZOS)


def ridge(L, w, base, amp, freqs, fill, y0, seed=0):
    """Hills / dunes: a polygon whose top edge is a sum of sines."""
    rnd = random.Random(seed)
    ph = [rnd.uniform(0, 6.28) for _ in freqs]
    pts = []
    for x in range(0, w + 40, 20):
        y = base
        for k, f in enumerate(freqs):
            y += math.sin(x / w * f * 6.28 + ph[k]) * amp / (k + 1)
        pts.append((x, y))
    pts += [(w + 40, y0), (-40, y0)]
    L.poly(pts, fill)


def palm(L, x, y, h, lean=0.0, col=(26, 40, 24), rim=None, seed=0):
    """Date palm silhouette: curved trunk, 9 fronds, a date cluster."""
    rnd = random.Random(seed)
    top = (x + lean * h, y - h)
    trunk = make_logo.bez((x, y), (x + lean * h * 0.2, y - h * 0.45), (x + lean * h * 0.7, y - h * 0.8), top, 24)
    Lp, Rp = [], []
    for i, (px, py) in enumerate(trunk):
        tw = h * 0.045 * (1 - 0.5 * i / len(trunk))
        Lp.append((px - tw, py)); Rp.append((px + tw, py))
    L.poly(Lp + Rp[::-1], col)
    for k in range(9):
        a = math.radians(-170 + k * 40 / 9 * 9 + rnd.uniform(-8, 8))
        ln = h * rnd.uniform(0.42, 0.55)
        ex, ey = top[0] + math.cos(a) * ln, top[1] + math.sin(a) * ln * 0.55 + ln * 0.42
        mid = (top[0] + math.cos(a) * ln * 0.5, top[1] + math.sin(a) * ln * 0.3 - ln * 0.12)
        sp = make_logo.bez(top, mid, mid, (ex, ey), 18)
        A, B = [], []
        for i, (sx, sy) in enumerate(sp):
            t = i / 18; fw = h * 0.07 * math.sin(math.pi * min(1, t * 1.05)) + 1
            nx, ny = sp[min(i + 1, 18)]; px, py = sp[max(i - 1, 0)]; dx, dy = nx - px, ny - py; l = math.hypot(dx, dy) or 1
            A.append((sx - dy / l * fw, sy + dx / l * fw)); B.append((sx + dy / l * fw * 0.35, sy - dx / l * fw * 0.35))
        L.poly(A + B[::-1], col)
        if rim: L.line(sp, rim, h * 0.012)
    for k in range(7):
        L.ell([top[0] - h * 0.07 + rnd.uniform(-h * 0.03, h * 0.03), top[1] + h * 0.02 + k * h * 0.012, top[0] + h * 0.07 + rnd.uniform(-h * 0.03, h * 0.03), top[1] + h * 0.09 + k * h * 0.012], col)


def tractor(L, x, y, s, body=(40, 32, 30), rim=(255, 190, 90), glass=(255, 214, 150)):
    """Side view tractor silhouette (facing right) with a warm rim light."""
    P = lambda pts: [(x + px * s, y + py * s) for px, py in pts]
    L.poly(P([(-1.6, 0), (0.5, 0), (0.5, -0.9), (1.5, -0.9), (1.5, 0.35), (-1.6, 0.35)]), body)          # chassis + hood
    L.poly(P([(-1.3, 0), (-1.3, -1.75), (0.2, -1.75), (0.2, 0)]), body)                                 # cab
    L.poly(P([(-1.05, -1.5), (-0.05, -1.5), (-0.05, -0.6), (-1.05, -0.6)]), glass)                      # window
    L.poly(P([(-1.05, -1.5), (-0.05, -1.5), (-0.05, -1.3), (-1.05, -1.1)]), (255, 255, 255, 110))        # glass glint
    L.line(P([(1.1, -0.9), (1.1, -1.9)]), body, s * 0.18)                                               # exhaust
    L.line(P([(-1.6, 0), (0.5, 0)]), rim, s * 0.05); L.line(P([(0.5, -0.9), (1.5, -0.9)]), rim, s * 0.05)
    L.line(P([(-1.3, -1.75), (0.2, -1.75)]), rim, s * 0.05)
    for wx, wr in ((-1.0, 1.05), (1.0, 0.65)):
        L.ell([x + (wx - wr) * s, y + 0.35 * s - wr * s + 0.6 * s, x + (wx + wr) * s, y + 0.35 * s + wr * s + 0.6 * s], body)
        L.ell([x + (wx - wr * 0.5) * s, y + 0.95 * s - wr * 0.5 * s, x + (wx + wr * 0.5) * s, y + 0.95 * s + wr * 0.5 * s], (90, 70, 50))
        L.d.arc([v * L.ss for v in (x + (wx - wr) * s, y + 0.95 * s - wr * s, x + (wx + wr) * s, y + 0.95 * s + wr * s)], 200, 320, fill=rgba(rim), width=int(s * 0.06 * L.ss))


def birds(L, pts, size, col):
    for (bx, by) in pts:
        L.line([(bx - size, by - size * 0.4), (bx, by), (bx + size, by - size * 0.4)], col, size * 0.22)


def scene(W, H, region='najd', ss=2, seed=7):
    """Returns layers: sky (opaque), far, mid, fg (transparent)."""
    rnd = random.Random(seed)
    delta = region == 'nile_delta'
    hz = H * 0.60   # horizon
    sunx, suny, sunr = W * 0.70, H * 0.48, H * 0.105
    # ---- sky
    if delta:
        stops = [(0, (52, 66, 120)), (0.3, (150, 110, 130)), (0.52, (255, 160, 90)), (0.62, (255, 215, 150)), (1, (255, 230, 190))]
    else:
        stops = [(0, (38, 48, 108)), (0.28, (160, 90, 110)), (0.5, (255, 140, 60)), (0.62, (255, 212, 130)), (1, (255, 230, 170))]
    sky = np_vgrad(W, H, stops)
    sky.alpha_composite(radial_glow(W, H, sunx, suny, H * 0.9, (255, 200, 110), 150, 2.5))
    sky.alpha_composite(radial_glow(W, H, sunx, suny, sunr * 2.4, (255, 235, 180), 230, 1.6))
    Ls = Layer(W, H, ss)
    Ls.ell([sunx - sunr, suny - sunr, sunx + sunr, suny + sunr], (255, 244, 205))
    # thin cirrus streaks
    for k in range(7):
        cy = H * rnd.uniform(0.08, 0.42); cw = W * rnd.uniform(0.12, 0.3); ch = H * rnd.uniform(0.006, 0.014); cx = W * rnd.uniform(0.05, 0.95)
        Ls.ell([cx - cw, cy - ch, cx + cw, cy + ch], (255, 200, 170, 90))
        Ls.ell([cx - cw * 0.6, cy - ch * 0.6 - ch, cx + cw * 0.9, cy + ch * 0.4 - ch], (255, 225, 200, 70))
    Ls.blur(1.2)
    sky.alpha_composite(Ls.out())
    # ---- far: hazy hills / dunes (or a tree line for the delta)
    Lf = Layer(W, H, ss)
    if delta:
        ridge(Lf, W, hz - H * 0.012, H * 0.004, [11, 23], (205, 150, 130, 255), H, 1)   # far riverbank
        for k in range(60):   # distant trees as soft bumps
            tx = rnd.uniform(0, W); th = H * rnd.uniform(0.02, 0.05)
            Lf.ell([tx - th * 0.9, hz - H * 0.012 - th, tx + th * 0.9, hz - H * 0.012 + th * 0.2], (175, 125, 120, 255))
    else:
        ridge(Lf, W, hz - H * 0.07, H * 0.045, [1.3, 3.1, 7.2], (205, 140, 120, 255), H, 1)
        ridge(Lf, W, hz - H * 0.03, H * 0.03, [2.2, 4.7, 9.1], (190, 125, 105, 255), H, 2)
        # oasis town silhouette on the ridge: flat roofs and a minaret-free water tower
        bx = W * 0.2
        for k in range(14):
            bw = W * rnd.uniform(0.012, 0.03); bh = H * rnd.uniform(0.015, 0.04); bx += bw * rnd.uniform(1.0, 1.6)
            Lf.poly([(bx, hz - H * 0.03), (bx, hz - H * 0.03 - bh), (bx + bw, hz - H * 0.03 - bh), (bx + bw, hz - H * 0.03)], (165, 105, 95, 255))
    Lf.blur(0.6)
    far = Lf.out()
    # ---- mid: ground, fields in perspective, water, palms, buildings
    Lm = Layer(W, H, ss)
    ground = (120, 150, 70) if delta else (205, 170, 110)
    Lm.poly([(0, hz), (W, hz), (W, H), (0, H)], ground)
    # field stripes converging towards a vanishing point left of the sun
    vx, vy = W * 0.55, hz - H * 0.02
    stripes = [(46, 130, 60), (90, 160, 70), (205, 170, 90), (60, 140, 65), (120, 170, 80)] if delta else [(60, 130, 60), (210, 170, 90), (95, 150, 70), (225, 185, 110), (70, 140, 65)]
    n = 22
    for k in range(n):
        x0 = -W * 1.2 + (W * 3.4) * k / n; x1 = -W * 1.2 + (W * 3.4) * (k + 1) / n
        Lm.poly([(vx, vy), (x0, H * 1.05), (x1, H * 1.05)], stripes[k % len(stripes)])
    # furrows
    for k in range(n * 3):
        x0 = -W * 1.2 + (W * 3.4) * k / (n * 3)
        Lm.line([(vx, vy), (x0, H * 1.05)], (0, 0, 0, 28), 2)
    # irrigation canal / river crossing the fields
    if delta:
        river = [(0, hz + H * 0.09), (W * 0.3, hz + H * 0.05), (W * 0.7, hz + H * 0.08), (W, hz + H * 0.04)]
        band = river + [(W, hz + H * 0.14), (W * 0.7, hz + H * 0.19), (W * 0.3, hz + H * 0.16), (0, hz + H * 0.2)]
        Lm.poly(band, (120, 90, 60))
        Lm.poly([(x, y + H * 0.004) for x, y in river] + [(W, hz + H * 0.136), (W * 0.7, hz + H * 0.186), (W * 0.3, hz + H * 0.156), (0, hz + H * 0.196)], (235, 175, 120))
        Lm.poly([(x, y + H * 0.01) for x, y in river] + [(W, hz + H * 0.12), (W * 0.7, hz + H * 0.17), (W * 0.3, hz + H * 0.14), (0, hz + H * 0.18)], (255, 215, 160))
        # felucca
        fx, fy = W * 0.62, hz + H * 0.12
        Lm.poly([(fx - W * 0.03, fy), (fx + W * 0.03, fy), (fx + W * 0.025, fy + H * 0.012), (fx - W * 0.025, fy + H * 0.012)], (40, 30, 30))
        Lm.poly([(fx - W * 0.002, fy - H * 0.11), (fx + W * 0.03, fy - H * 0.002), (fx - W * 0.004, fy - H * 0.002)], (255, 240, 220))
        Lm.line([(fx - W * 0.004, fy), (fx - W * 0.004, fy - H * 0.11)], (40, 30, 30), 3)
    else:
        canal = [(W * 0.0, hz + H * 0.22), (W * 0.35, hz + H * 0.13), (W * 0.62, hz + H * 0.11), (W * 1.0, hz + H * 0.16)]
        Lm.line(canal, (120, 95, 60), H * 0.035)
        Lm.line(canal, (255, 205, 140), H * 0.02)
        Lm.line(canal, (255, 240, 200), H * 0.006)
    # farmhouse / buildings on the left
    if delta:
        # pigeon tower + mud house
        px0, py0 = W * 0.18, hz + H * 0.01
        Lm.poly([(px0, py0), (px0 + W * 0.015, py0 - H * 0.16), (px0 + W * 0.045, py0 - H * 0.16), (px0 + W * 0.06, py0)], (120, 80, 60))
        for r in range(4):
            for c in range(3):
                Lm.ell([px0 + W * 0.018 + c * W * 0.01, py0 - H * 0.14 + r * H * 0.03, px0 + W * 0.023 + c * W * 0.01, py0 - H * 0.13 + r * H * 0.03], (50, 35, 30))
        Lm.poly([(px0 + W * 0.06, py0), (px0 + W * 0.06, py0 - H * 0.06), (px0 + W * 0.13, py0 - H * 0.06), (px0 + W * 0.13, py0)], (140, 95, 70))
    else:
        hx, hy = W * 0.13, hz + H * 0.005
        Lm.poly([(hx, hy), (hx, hy - H * 0.07), (hx + W * 0.09, hy - H * 0.07), (hx + W * 0.09, hy)], (150, 105, 70))
        Lm.poly([(hx + W * 0.09, hy), (hx + W * 0.09, hy - H * 0.1), (hx + W * 0.13, hy - H * 0.1), (hx + W * 0.13, hy)], (130, 90, 60))
        for k in range(5): Lm.poly([(hx + W * 0.012 + k * W * 0.016, hy - H * 0.07), (hx + W * 0.012 + k * W * 0.016, hy - H * 0.078), (hx + W * 0.02 + k * W * 0.016, hy - H * 0.078), (hx + W * 0.02 + k * W * 0.016, hy - H * 0.07)], (150, 105, 70))
        Lm.poly([(hx + W * 0.02, hy), (hx + W * 0.02, hy - H * 0.035), (hx + W * 0.04, hy - H * 0.035), (hx + W * 0.04, hy)], (255, 200, 120))   # lit doorway
        # greenhouses
        for k in range(3):
            gx = W * 0.3 + k * W * 0.06; gy = hz + H * 0.02
            Lm.poly([(gx, gy), (gx, gy - H * 0.025), (gx + W * 0.025, gy - H * 0.045), (gx + W * 0.05, gy - H * 0.025), (gx + W * 0.05, gy)], (235, 240, 245, 200))
    # palm groves: a row along the horizon (smaller, hazier) and larger ones at the sides
    palms = []
    for k in range(18):
        px = rnd.uniform(-W * 0.02, W * 1.02); ph = H * rnd.uniform(0.07, 0.13)
        palms.append((px, hz + H * 0.01 + rnd.uniform(0, H * 0.01), ph, 0.7))
    for (px, py, ph, a) in sorted(palms, key=lambda p: p[2]):
        palm(Lm, px, py, ph, rnd.uniform(-0.1, 0.1), (60, 70, 50) if delta else (70, 62, 50), None, int(px))
    big = [(W * 0.06, hz + H * 0.07, H * 0.34, -0.08), (W * 0.13, hz + H * 0.1, H * 0.28, 0.12), (W * 0.9, hz + H * 0.08, H * 0.36, 0.1), (W * 0.97, hz + H * 0.12, H * 0.26, -0.1)]
    for (px, py, ph, lean) in big:
        palm(Lm, px, py, ph, lean, (26, 44, 28) if delta else (34, 40, 28), (255, 190, 90, 140), int(px) + 3)
    birds(Lm, [(W * 0.42, H * 0.22), (W * 0.45, H * 0.2), (W * 0.47, H * 0.235), (W * 0.5, H * 0.21), (W * 0.52, H * 0.245)], W * 0.006, (60, 40, 50))
    mid = Lm.out()
    # ---- foreground: tractor on the near track, crop leaves in the corners, dust
    Lg = Layer(W, H, ss)
    Lg.poly([(0, H * 0.86), (W, H * 0.84), (W, H), (0, H)], (165, 130, 80) if not delta else (110, 90, 60))   # dirt track
    Lg.line([(0, H * 0.9), (W, H * 0.88)], (0, 0, 0, 40), H * 0.01)
    # dust kicked up behind the tractor (drawn first so the tractor stays crisp)
    for k in range(40):
        dx = W * 0.33 - H * rnd.uniform(0.05, 0.35); dy = H * 0.9 - H * rnd.uniform(0, 0.12); dr = H * rnd.uniform(0.01, 0.045)
        Lg.ell([dx - dr, dy - dr * 0.6, dx + dr, dy + dr * 0.6], (235, 190, 130, rnd.randint(14, 40)))
    tractor(Lg, W * 0.33, H * 0.86, H * 0.075)
    # wheat ears and grass blades in the lower corners (dark, slightly out of focus)
    dark = (28, 40, 24)
    for k in range(34):
        side = rnd.choice([0, 1]); bx = (W * rnd.uniform(-0.02, 0.22)) if side == 0 else W * rnd.uniform(0.78, 1.02)
        by = H * rnd.uniform(0.98, 1.06); lh = H * rnd.uniform(0.2, 0.42); lean = rnd.uniform(-0.2, 0.2)
        top = (bx + lean * lh, by - lh)
        if k % 3 == 0:   # grass blade: tapered polygon
            Lg.poly([(bx - H * 0.012, by), (bx + H * 0.012, by), (top[0] + lean * lh * 0.4, top[1] - lh * 0.1)], dark)
            continue
        stem = make_logo.bez((bx, by), (bx + lean * lh * 0.3, by - lh * 0.5), (bx + lean * lh * 0.8, by - lh * 0.85), top, 12)
        Lg.line(stem, dark, H * 0.005)
        ear = int(lh * 0.42); n_g = 9
        for j in range(n_g):
            t = j / n_g; ex = top[0] + (stem[-1][0] - stem[-4][0]) * -t * 1.2; ey = top[1] + ear * t
            for sgn in (-1, 1):
                Lg.poly([(ex, ey), (ex + sgn * H * 0.016, ey + H * 0.006), (ex + sgn * H * 0.012, ey + H * 0.024), (ex, ey + H * 0.018)], dark)
                Lg.line([(ex + sgn * H * 0.012, ey + H * 0.004), (ex + sgn * H * 0.02, ey - H * 0.03)], dark, H * 0.0012)   # awn
    Lg.blur(0.8)
    # floating pollen / dust motes in the light
    Lp = Layer(W, H, 1)
    for k in range(140):
        px, py = rnd.uniform(0, W), rnd.uniform(H * 0.1, H * 0.95); pr = rnd.uniform(1.5, 5) * W / 1920
        Lp.ell([px - pr, py - pr, px + pr, py + pr], (255, 230, 170, rnd.randint(60, 170)))
    Lp.blur(1.0)
    fg = Lg.out(); fg.alpha_composite(Lp.out())
    return {'sky': sky, 'far': far, 'mid': mid, 'fg': fg}


def composite(layers, vignette=True):
    im = layers['sky'].copy()
    for k in ('far', 'mid', 'fg'):
        im.alpha_composite(layers[k])
    if vignette:
        W, H = im.size
        yy, xx = np.mgrid[0:H, 0:W]
        d = np.sqrt(((xx - W / 2) / (W / 2)) ** 2 + ((yy - H / 2) / (H / 2)) ** 2)
        a = (np.clip(d - 0.65, 0, 1) * 150).astype(np.uint8)
        v = np.zeros((H, W, 4), np.uint8); v[..., 0] = 20; v[..., 1] = 10; v[..., 2] = 20; v[..., 3] = a
        im.alpha_composite(Image.fromarray(v, 'RGBA'))
    return im


def fit(im, w, h):
    """Cover-crop to w x h."""
    r = max(w / im.width, h / im.height)
    im2 = im.resize((int(im.width * r + 0.5), int(im.height * r + 0.5)), Image.LANCZOS)
    x, y = (im2.width - w) // 2, (im2.height - h) // 2
    return im2.crop((x, y, x + w, y + h))


# ------------------------------------------------------------------ logo lockups (badge + wordmark), tagline per language
def lockup(lang, stacked=False, scale=1.0):
    em = make_logo.emblem(int(640 * scale))
    if lang == 'en':
        wd = make_logo.word("MAZRAA", int(300 * scale)); tg = make_logo.tagline("FARMING SIMULATOR", int(64 * scale))
    else:
        wd = make_logo.word("مزرعة", int(320 * scale), True); tg = make_logo.tagline("محاكي الزراعة", int(70 * scale), True)
    gap = int(50 * scale)
    if stacked:
        W = max(em.width, wd.width) + gap * 2; H = em.height + gap // 2 + wd.height + 10 + tg.height + gap
        im = Image.new('RGBA', (W, H), (0, 0, 0, 0))
        im.alpha_composite(em, ((W - em.width) // 2, gap // 2)); y = em.height + gap // 2
        im.alpha_composite(wd, ((W - wd.width) // 2, y)); im.alpha_composite(tg, ((W - tg.width) // 2, y + wd.height + 10))
        return im
    tw = max(wd.width, tg.width); W = gap + em.width + gap + tw + gap; H = em.height + gap * 2
    im = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    top = (H - (wd.height + 10 + tg.height)) // 2
    if lang == 'en':
        im.alpha_composite(em, (gap, gap)); x = gap + em.width + gap
        im.alpha_composite(wd, (x, top)); im.alpha_composite(tg, (x + 10, top + wd.height + 10))
    else:
        im.alpha_composite(em, (W - gap - em.width, gap)); right = W - gap - em.width - gap
        im.alpha_composite(wd, (right - wd.width, top)); im.alpha_composite(tg, (right - tg.width - 10, top + wd.height + 10))
    return im


def save(im, path, **kw):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    if path.lower().endswith('.jpg'):
        im.convert('RGB').save(path, quality=kw.get('quality', 90), optimize=True, progressive=True)
    else:
        im.save(path, optimize=True)
    return path


if __name__ == '__main__':
    print('illustrations...')
    najd = scene(3840, 2160, 'najd'); delta = scene(3840, 2160, 'nile_delta', seed=11)
    key_najd = composite(najd); key_delta = composite(delta)
    save(key_najd, os.path.join(HERE, 'keyart_najd_4k.jpg'), quality=92)
    save(key_delta, os.path.join(HERE, 'keyart_delta_4k.jpg'), quality=92)

    print('logos...')
    en = lockup('en'); ar = lockup('ar')
    save(en, os.path.join(HERE, 'mazraa_logo_en.png')); save(ar, os.path.join(HERE, 'mazraa_logo_ar.png'))
    en_st = lockup('en', stacked=True); ar_st = lockup('ar', stacked=True)
    save(en_st, os.path.join(HERE, 'mazraa_logo_en_stacked.png')); save(ar_st, os.path.join(HERE, 'mazraa_logo_ar_stacked.png'))
    E = make_logo.emblem(1024)
    save(E, os.path.join(HERE, 'mazraa_icon_1024.png')); save(E.resize((512, 512), Image.LANCZOS), os.path.join(HERE, 'mazraa_icon_512.png'))
    E.save(os.path.join(HERE, 'mazraa.ico'), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    E.save(os.path.join(ROOT, 'installer', 'mazraa.ico'), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    # in-game: app icon, logos (1024 wide for crisp title screens), splash logo
    save(E, os.path.join(ROOT, 'Assets', 'Branding', 'MazraaIcon.png'))
    for lang, lk in (('en', en), ('ar', ar)):
        r = 1024 / lk.width
        save(lk.resize((1024, int(lk.height * r)), Image.LANCZOS), os.path.join(RES_BRAND, f'mazraa_logo_{lang}_512.png'))   # same asset name, now 1024 px wide
    for p in ('mazraa_logo_en_512.png', 'mazraa_logo_ar_512.png'):
        write_meta(os.path.join(RES_BRAND, p), os.path.relpath(os.path.join(RES_BRAND, p), ROOT), 'default', (0, 0, 0, 0), 1024) if not os.path.exists(os.path.join(RES_BRAND, p + '.meta')) else None
    splash = Image.new('RGBA', (1600, 900), (0, 0, 0, 0)); st = en_st.copy(); st.thumbnail((1200, 760), Image.LANCZOS)
    splash.alpha_composite(st, ((1600 - st.width) // 2, (900 - st.height) // 2))
    sp = os.path.join(ROOT, 'Assets', 'Branding', 'MazraaSplashLogo.png'); save(splash, sp)
    write_meta(sp, os.path.relpath(sp, ROOT), 'sprite', (0, 0, 0, 0), 2048)

    print('loading screens (parallax layers, 1920x1080)...')
    for name, layers in (('najd', najd), ('delta', delta)):
        small = {k: v.resize((1920, 1080), Image.LANCZOS) for k, v in layers.items()}
        p = save(small['sky'], os.path.join(LOADING, f'{name}_sky.jpg'), quality=88); write_meta(p, os.path.relpath(p, ROOT), 'default', (0, 0, 0, 0), 2048)
        for k in ('far', 'mid', 'fg'):
            p = save(small[k], os.path.join(LOADING, f'{name}_{k}.png')); write_meta(p, os.path.relpath(p, ROOT), 'default', (0, 0, 0, 0), 2048)
        # flat composite for the main-menu / title backdrop fallback and the pause blur
        p = save(composite(small, vignette=False), os.path.join(LOADING, f'{name}_full.jpg'), quality=86); write_meta(p, os.path.relpath(p, ROOT), 'default', (0, 0, 0, 0), 2048)

    print('store tiles (MSIX)...')
    def sq(n, nm): save(E.resize((n, n), Image.LANCZOS), os.path.join(MSIX, nm))
    for base, n in (('Square44x44Logo', 44), ('Square150x150Logo', 150), ('Square310x310Logo', 310), ('StoreLogo', 50), ('Square71x71Logo', 71)):
        sq(n, base + '.png'); sq(n * 2, base + '.scale-200.png')
    for t in (16, 24, 32, 48, 256): sq(t, 'Square44x44Logo.targetsize-%d.png' % t)
    def wide(w, h, nm, lk=None):
        im = fit(key_najd, w, h)
        if lk is not None:
            l = lk.copy(); l.thumbnail((int(w * 0.8), int(h * 0.72)), Image.LANCZOS)
            im.alpha_composite(shadow(l, max(2, h // 60), 0, h // 90, 150).resize(l.size), ((w - l.width) // 2, (h - l.height) // 2)) if False else im.alpha_composite(l, ((w - l.width) // 2, (h - l.height) // 2))
        save(im, os.path.join(MSIX, nm))
    wide(310, 150, 'Wide310x150Logo.png', en); wide(620, 300, 'Wide310x150Logo.scale-200.png', en)
    wide(620, 300, 'SplashScreen.png', en); wide(1240, 600, 'SplashScreen.scale-200.png', en)

    print('store art...')
    def art(base, w, h, nm, lk, lk_frac=(0.5, 0.42), pos='center', sub=None):
        im = fit(base, w, h)
        l = lk.copy(); l.thumbnail((int(w * lk_frac[0]), int(h * lk_frac[1])), Image.LANCZOS)
        l = shadow(l, max(4, h // 80), 0, h // 60, 170)
        if pos == 'center': xy = ((w - l.width) // 2, int(h * 0.56) - l.height // 2)
        elif pos == 'top': xy = ((w - l.width) // 2, int(h * 0.05))
        else: xy = (int(w * 0.05), int(h * 0.06))
        im.alpha_composite(l, xy)
        if sub:
            t = text_image(sub, max(18, h // 24), CREAM, stroke=max(2, h // 300))
            im.alpha_composite(t, ((w - t.width) // 2, int(h * 0.9) - t.height // 2))
        return save(im, os.path.join(STORE, nm))
    art(key_najd, 1920, 1080, 'key_art_1920x1080.png', en_st, (0.46, 0.6))
    art(key_najd, 2160, 1080, 'hero_2160x1080.png', en, (0.7, 0.4))
    art(key_najd, 1080, 1080, 'box_art_1080x1080.png', en_st, (0.8, 0.6))
    art(key_najd, 720, 1080, 'poster_720x1080.png', en_st, (0.9, 0.5), 'top')
    art(key_delta, 1920, 1080, 'key_art_delta_1920x1080.png', en_st, (0.46, 0.6), 'center', 'Egypt: Nile Delta DLC')
    art(key_najd, 1920, 1080, 'key_art_ar_1920x1080.png', ar_st, (0.46, 0.6))
    art(key_najd, 3840, 2160, 'key_art_3840x2160.png', en_st, (0.46, 0.6))
    save(E.resize((300, 300), Image.LANCZOS), os.path.join(STORE, 'store_logo_300x300.png'))
    save(E.resize((1080, 1080), Image.LANCZOS), os.path.join(STORE, 'app_icon_1080x1080.png'))
    save(key_najd.resize((1920, 1080), Image.LANCZOS), os.path.join(STORE, 'background_clean_1920x1080.jpg'), quality=92)
    print('ok')
