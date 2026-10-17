import json
"""Static test suite for GreenHouse_MVP. Usage: python3 -I static_tests.py <repo root>"""
import os, re, sys, json, collections, subprocess

root = sys.argv[1]
A = os.path.join(root, 'Assets')
results = []  # (name, ok, details)

def test(name):
    def deco(fn):
        try:
            ok, details = fn()
        except Exception as e:
            ok, details = False, ['EXCEPTION ' + repr(e)]
        results.append((name, ok, details))
        return fn
    return deco

def walk(base, exts=None):
    for dp, dn, fn in os.walk(base):
        for f in fn:
            if exts is None or f.endswith(exts):
                yield os.path.join(dp, f)

scripts = sorted(walk(os.path.join(A, 'Scripts'), ('.cs',)))
src = {p: open(p, encoding='utf-8').read() for p in scripts}
allsrc = '\n'.join(src.values())

# ---------------------------------------------------------------- assets
@test('Every asset and folder has a .meta file')
def _():
    tracked = subprocess.run(['git', '-C', root, 'ls-files', 'Assets'], capture_output=True, text=True).stdout.split('\n')
    miss = []
    for p in tracked:
        if not p or p.endswith('.meta'): continue
        if not os.path.exists(os.path.join(root, p + '.meta')): miss.append(p)
        d = os.path.dirname(p)
        while d and d != 'Assets':
            if not os.path.exists(os.path.join(root, d + '.meta')): miss.append(d + '/')
            d = os.path.dirname(d)
    miss = sorted(set(miss))
    return not miss, miss[:20]

guid_of = {}
@test('No duplicate GUIDs')
def _():
    dup = []
    for p in walk(A, ('.meta',)):
        m = re.search(r'guid: (\w+)', open(p, errors='ignore').read())
        if not m: continue
        g = m.group(1)
        if g in guid_of: dup.append(f'{g}: {guid_of[g]} & {p}')
        guid_of[g] = p
    return not dup, dup

@test('Scenes and prefabs: every project script reference resolves (no "missing script")')
def _():
    # Report m_Script refs to unknown GUIDs whose class identifier is one of our own scripts.
    ours = {os.path.basename(p)[:-3] for p in scripts}
    broken = []
    files = [p for p in walk(A, ('.unity', '.prefab'))]
    scene = '\n'.join(open(p, errors='ignore').read() for p in files)
    for block in re.split(r'\n--- !u!114 ', scene)[1:]:
        m = re.search(r'm_Script: \{fileID: 11500000, guid: (\w+)', block)
        if not m: continue
        g = m.group(1)
        if g in guid_of: continue
        ident = re.search(r'm_EditorClassIdentifier: (\S*)', block)
        ident = ident.group(1) if ident else ''
        cls = ident.split('::')[-1]
        if 'Assembly-CSharp' in ident and cls in ours: broken.append(f'{cls} -> {g}')
    # Assets referenced from project scripts by GUID that we created ourselves
    critical = {'fe684c2a3fbb97b4c9ddf49d1dbf559c': 'Configuration.asset', '9ef8512ef9d3c694e9eecb58be35b2d3': 'Newscrash SDF',
                'd0634fdda9e46c34a82d9ca13caae2e9': 'InteractSound', '2b77fa8a25ea029498a452b66376d9f9': 'FieldDead.mat',
                'e0e4f09f1b077104eb6c12e22f11e792': 'Irrigation module'}
    for g, n in critical.items():
        if g not in guid_of: broken.append(f'{n} ({g}) missing')
    return not broken, broken

@test('MonoBehaviour / ScriptableObject class names match their file names')
def _():
    bad = []
    for p, s in src.items():
        fname = os.path.basename(p)[:-3]
        for m in re.finditer(r'public (?:abstract )?class (\w+)\s*:\s*([\w.]+)', s):
            cls, base = m.group(1), m.group(2)
            derived = base in ('MonoBehaviour', 'ScriptableObject', 'Interactable', 'Vehicle') or base.endswith('Behaviour')
            if derived and cls != fname: bad.append(f'{cls} in {os.path.relpath(p, A)}')
    return not bad, bad

@test('Every Resources.Load path exists')
def _():
    res = os.path.join(A, 'Resources')
    have = set()
    for p in walk(res):
        if p.endswith('.meta'): continue
        rel = os.path.relpath(p, res)
        have.add(os.path.splitext(rel)[0].replace(os.sep, '/'))
    miss = []
    for m in re.finditer(r'Resources\.Load<\w+>\("([^"]+)"', allsrc):
        if m.group(1).endswith('/'): continue  # prefix + variable, checked below
        if m.group(1) not in have: miss.append(m.group(1))
    # TryModel names that we ship (others are optional drop-ins)
    for name in ['Truck', 'Tractor', 'cow', 'sheep', 'chicken', 'Crops/crops_leafsStageA', 'Crops/crops_wheatStageB', 'Crops/crop_melon', 'Crops/crop_turnip', 'Crops/crops_leafsStageB', 'Crops/crops_wheatStageA']:
        if 'Models/' + name not in have: miss.append('Models/' + name)
    for name in ['Data/crop_profiles', 'Data/economy', 'Data/missions', 'Data/scenarios', 'Data/sponsors', 'Fonts/Tajawal-Bold']:
        if name not in have: miss.append(name)
    return not miss, sorted(set(miss))

@test('Model .meta files: animals use legacy animation import')
def _():
    bad = []
    for n in ['cow', 'sheep', 'chicken']:
        t = open(os.path.join(A, 'Resources', 'Models', n + '.fbx.meta')).read()
        if 'animationType: 1' not in t: bad.append(n)
    return not bad, bad

# ---------------------------------------------------------------- data
def load(name):
    return json.load(open(os.path.join(A, 'Resources', 'Data', name + '.json'), encoding='utf-8'))

@test('All data JSON files parse')
def _():
    bad = []
    for n in ['crop_profiles', 'economy', 'missions', 'scenarios', 'sponsors']:
        try: load(n)
        except Exception as e: bad.append(f'{n}: {e}')
    return not bad, bad

@test('crop_profiles: bed keys match the scene and crop ids exist')
def _():
    d = load('crop_profiles'); ids = {c['id'] for c in d['crops']}
    bad = [b['key'] for b in d['beds'] if b['crop'] not in ids]
    expected = {f'{g}/Crop{c}' for g in ('GreenHouse', 'GreenHouse-1') for c in 'ABCD'}
    keys = {b['key'] for b in d['beds']}
    if keys != expected: bad.append(f'keys differ: {sorted(keys ^ expected)}')
    for c in d['crops']:
        if not c.get('nameAr'): bad.append(f'{c["id"]} missing Arabic name')
    return not bad, bad

@test('economy: goods, animals, field crops and products are consistent')
def _():
    d = load('economy'); goods = {g['id'] for g in d['goods']}; bad = []
    for a in d['animals']:
        if a['product'] not in goods: bad.append(f'animal {a["id"]} product {a["product"]} not a good')
        if a['price'] <= 0 or a['interval'] <= 0 or a['max'] <= 0: bad.append(f'animal {a["id"]} bad numbers')
    for c in d['fieldCrops']:
        if c['id'] not in goods: bad.append(f'field crop {c["id"]} has no matching good (cannot be sold)')
        for s in c['seasons']:
            if s not in ('spring', 'summer', 'autumn', 'winter'): bad.append(f'{c["id"]} bad season {s}')
    for g in d['goods']:
        if g['price'] <= 0 or g['weight'] <= 0: bad.append(f'good {g["id"]} bad price/weight')
        if not g.get('nameAr') or not g.get('unitAr'): bad.append(f'good {g["id"]} missing Arabic')
    return not bad, bad

@test('missions: every mission type is handled by MissionManager and texts are bilingual')
def _():
    d = load('missions'); bad = []
    mm = src[os.path.join(A, 'Scripts', 'Managers', 'MissionManager.cs')]
    om = src[os.path.join(A, 'Scripts', 'UI', 'ObjectiveMarker.cs')]
    ids = set()
    for m in d['missions']:
        if m['id'] in ids: bad.append('duplicate id ' + m['id'])
        ids.add(m['id'])
        if f'"{m["type"]}"' not in mm: bad.append(f'{m["id"]}: type {m["type"]} not handled')
        if f'"{m["type"]}"' not in om: bad.append(f'{m["id"]}: no objective marker for {m["type"]} (info)')
        for k in ('titleEn', 'titleAr', 'descEn', 'descAr'):
            if not m.get(k): bad.append(f'{m["id"]} missing {k}')
        if m['target'] <= 0 or m['reward'] < 0: bad.append(f'{m["id"]} bad numbers')
    real = [b for b in bad if not b.endswith('(info)')]
    return not real, bad

@test('scenarios: objectives and broken modules are valid')
def _():
    d = load('scenarios'); bad = []
    for s in d['scenarios']:
        if s['objective'] not in ('allHealthy', 'harvestKg'): bad.append(f'{s["id"]} objective {s["objective"]}')
        if s['breakModule'] not in ('', 'Irrigation', 'Nutrient', 'Energy', 'ClimateControl'): bad.append(f'{s["id"]} breakModule')
        if not (s['goldTime'] <= s['silverTime'] <= s['timeLimit']): bad.append(f'{s["id"]} times not ordered')
        if s['objective'] == 'allHealthy' and not s['setup'] and not s['breakModule']: bad.append(f'{s["id"]} would complete instantly')
    return not bad, bad

@test('sponsors: one slot per module')
def _():
    d = load('sponsors'); mods = sorted(s['module'] for s in d['slots'])
    return mods == sorted(['ClimateControl', 'Energy', 'Irrigation', 'Nutrient']), mods

# ---------------------------------------------------------------- localization
loc = src[os.path.join(A, 'Scripts', 'Localization', 'Loc.cs')]
def dict_keys(name):
    m = re.search(r'Dictionary<string, string> ' + name + r' = new Dictionary<string, string>\s*\{(.*?)\n    \};', loc, re.S)
    return re.findall(r'^\s*\{ "((?:[^"\\]|\\.)*)",', m.group(1), re.M)

ar_keys = set(dict_keys('arabic')); long_en = set(dict_keys('englishLong')); long_ar = set(dict_keys('arabicLong'))
# feature translation packs (Resources/Data/Loc/*.json) count as translated too
import glob as _glob
for _f in _glob.glob(os.path.join(A, 'Resources', 'Data', 'Loc', '*.json')):
    for _e in json.load(open(_f, encoding='utf-8')).get('entries', []):
        if _e.get('en') and _e.get('ar'): ar_keys.add(_e['en'])

@test('No duplicate translation keys')
def _():
    bad = []
    for n in ('arabic', 'englishLong', 'arabicLong'):
        bad += [f'{n}: {k}' for k, c in collections.Counter(dict_keys(n)).items() if c > 1]
    return not bad, bad

@test('Every string passed to Loc.T / Loc.Raw / Loc.Set has an Arabic translation')
def _():
    used = set()
    for s in src.values():
        for m in re.finditer(r'Loc\.(?:T|Raw)\("((?:[^"\\]|\\.)*)"', s): used.add(m.group(1))
        for m in re.finditer(r'Loc\.Set\(\w+, "((?:[^"\\]|\\.)*)"', s): used.add(m.group(1))
        for m in re.finditer(r'Build\.Label\([^"\n]*"([^"]+)"', s): used.add(m.group(1))
        for m in re.finditer(r'Loc\.T\(\w+ \? "([^"]+)" : "([^"]+)"\)', s): used.update(m.groups())
        for m in re.finditer(r'Loc\.Raw\(\w+ \? "([^"]+)" : "([^"]+)"\)', s): used.update(m.groups())
    # strings chosen through variables
    used.update(['Fully upgraded', 'The pen is full for this animal', 'Not enough money', 'Gold', 'Silver', 'Bronze',
                 'Spring', 'Summer', 'Autumn', 'Winter', 'Sunny', 'Cloudy', 'Rain', 'Heatwave', 'Plow', 'Seeder', 'Harvester',
                 'Healthy', 'Weak', 'Sick', 'Day temp', 'Night temp', 'Humidity', 'Water / plant', 'Crop cycle'])
    missing = sorted(k for k in used if k not in ar_keys and not (k in long_en and k in long_ar))
    return not missing, missing

@test('Scene UI texts all have translations')
def _():
    scene = open(os.path.join(A, 'Scenes', 'Blak_Pulse_Main.unity'), errors='ignore').read()
    texts = set(re.findall(r'^  m_text: (.+)$', scene, re.M))
    skip = {'2', 'X', 'Prompt UI', 'Value'}
    missing = sorted(t for t in texts if t.strip() not in ar_keys and t.strip() not in skip and not t.strip().replace('.', '').isdigit())
    return not missing, missing

# ---------------------------------------------------------------- input
@test('Keyboard keys: no two systems react to the same key in the same state (report)')
def _():
    uses = collections.defaultdict(set)
    for p, s in src.items():
        for m in re.finditer(r'kb\.(\w+Key)\.wasPressedThisFrame|Keyboard\.current\.(\w+Key)', s):
            uses[m.group(1) or m.group(2)].add(os.path.basename(p)[:-3])
    table = [f'{k}: {", ".join(sorted(v))}' for k, v in sorted(uses.items())]
    # Known safe overlaps: E (interaction vs vehicle exit vs shop close are frame/state guarded), Escape/Tab panels
    risky = [t for t in table if len(t.split(':')[1].split(',')) > 1 and not t.startswith(('eKey', 'escapeKey', 'tabKey', 'spaceKey', 'rKey'))]
    return not risky, table


# ---------------------------------------------------------------- rendering
@test('Shader stripper and URP renderers agree (Forward+ keywords not stripped while a renderer uses Forward+)')
def _():
    strip = open(os.path.join(A, 'Editor', 'MazraaShaderStripper.cs')).read()
    fplus = [f for f in walk(os.path.join(A, 'Settings'), ('.asset',)) if 'Renderer' in f and re.search(r'm_RenderingMode: [23]\b', open(f).read())]
    aware = 'ForwardPlusUsed()' in strip and 'keep.Count == 0' in strip
    return aware or not fplus, fplus


@test('Input System control types have their using (real Unity keeps them in UnityEngine.InputSystem.Controls)')
def _():
    bad = [os.path.relpath(p, A) for p, s in src.items() if re.search(r'\b(KeyControl|ButtonControl|AxisControl|StickControl|Vector2Control|DpadControl)\b', s) and 'UnityEngine.InputSystem.Controls' not in s]
    return not bad, bad

# ---------------------------------------------------------------- graphics makeover (Resources/Look)
LOOK = os.path.join(A, 'Resources', 'Look')

@test('Look textures: <= 2048, mipmapped, compressed, normal maps typed, foliage keeps alpha coverage; total < 150 MB')
def _():
    bad, total = [], 0
    tdir = os.path.join(LOOK, 'Textures')
    if not os.path.isdir(tdir): return False, ['missing ' + tdir]
    for f in sorted(os.listdir(tdir)):
        if f.endswith('.meta'): continue
        p = os.path.join(tdir, f); total += os.path.getsize(p)
        m = open(p + '.meta').read()
        sizes = [int(x) for x in re.findall(r'maxTextureSize: (\d+)', m)]
        if not sizes or max(sizes) > 2048: bad.append(f + ' size')
        if 'enableMipMap: 1' not in m: bad.append(f + ' mips')
        if re.search(r'textureCompression: 0', m): bad.append(f + ' uncompressed')
        if f.endswith('_n.jpg') and 'textureType: 1' not in m: bad.append(f + ' not a normal map')
        if (f.endswith('_n.jpg') or f.endswith('_m.png')) and 'sRGBTexture: 0' not in m: bad.append(f + ' should be linear')
        if f.endswith('_d.png') and 'mipMapsPreserveCoverage: 1' not in m: bad.append(f + ' alpha coverage')
    if total > 150 * 1024 * 1024: bad.append('total %d MB' % (total // 2**20))
    return not bad, bad + ['%.1f MB' % (total / 2**20)]

@test('Look materials: every texture GUID resolves, shaders are URP Lit / Terrain Lit / Mazraa/Foliage / built-in sky')
def _():
    bad = []
    known = {'933532a4fcc9baf4fa0491de14d08ed7', '69c1f799e772cb6438f56c23efccb782', '0000000000000000f000000000000000'}
    for p in walk(os.path.join(LOOK, 'Shaders'), ('.shader.meta',)): known.add(re.search(r'guid: (\w+)', open(p).read()).group(1))
    mats = list(walk(os.path.join(LOOK, 'Materials'), ('.mat',)))
    for p in mats:
        t = open(p).read()
        g = re.search(r'm_Shader: \{fileID: -?\d+, guid: (\w+)', t).group(1)
        if g not in known: bad.append(os.path.basename(p) + ' shader ' + g)
        for tg in re.findall(r'm_Texture: \{fileID: 2800000, guid: (\w+)', t):
            if tg not in guid_of: bad.append(os.path.basename(p) + ' texture ' + tg)
    names = {os.path.basename(p)[:-4] for p in mats}
    enum = re.search(r'public enum LookMat\s*\{(.*?)\}', allsrc, re.S).group(1)
    for n in re.findall(r'\b([A-Z]\w+)\b', re.sub(r'//.*', '', enum)):
        if n not in names: bad.append('LookMat.%s has no .mat' % n)
    return not bad and len(mats) > 20, bad

@test('Look shader: foliage shader has forward, shadow, depth and depth-normals passes with instancing')
def _():
    t = open(os.path.join(LOOK, 'Shaders', 'MazraaFoliage.shader')).read()
    need = ['"LightMode" = "UniversalForward"', '"LightMode" = "ShadowCaster"', '"LightMode" = "DepthOnly"', '"LightMode" = "DepthNormals"', 'CBUFFER_START(UnityPerMaterial)']
    bad = [n for n in need if n not in t]
    if t.count('#pragma multi_compile_instancing') < 4: bad.append('instancing in every pass')
    if 'LOD_FADE_CROSSFADE' in t or '_FORWARD_PLUS' in t: bad.append('uses keywords the build strips')
    return not bad, bad

@test('Look assets credited (Assets/ThirdParty/CREDITS.md lists Poly Haven and every source asset)')
def _():
    c = open(os.path.join(A, 'ThirdParty', 'CREDITS.md'), encoding='utf-8').read()
    srcs = ['sandy_gravel_02', 'gravelly_sand', 'dry_riverbed_rock', 'brown_mud_02', 'grass_ground', 'aerial_mud_1', 'rough_plaster_brick_02', 'white_stucco',
            'concrete_wall_008', 'corrugated_iron', 'rusty_metal_sheet', 'weathered_brown_planks', 'rough_linen', 'curly_teddy_natural', 'hessian_230',
            'palm_bark', 'bark_brown_02', 'palm_tree_bark', 'jacaranda_tree', 'grass_medium_01', 'island_tree_02', 'shrub_04']
    miss = [x for x in srcs if x not in c]
    return 'Poly Haven' in c and not miss, miss

# ---------------------------------------------------------------- report
ok = sum(1 for r in results if r[1])
for name, passed, details in results:
    print(('PASS ' if passed else 'FAIL ') + name)
    for d in details[:25]:
        if not passed or name.startswith('Keyboard'):
            print('      ' + str(d))
print(f'\n{ok}/{len(results)} static tests passed')
