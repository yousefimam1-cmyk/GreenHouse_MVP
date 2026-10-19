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
@test('HDRP: package, 4 HDRP assets on the Low/Medium/High/Ultra quality levels, default = High, no URP left')
def _():
    bad = []
    man = json.load(open(os.path.join(root, 'Packages', 'manifest.json')))['dependencies']
    if 'com.unity.render-pipelines.high-definition' not in man: bad.append('no HDRP package')
    if 'com.unity.render-pipelines.universal' in man: bad.append('URP package still in the manifest')
    guids = {}
    for lv in ('Low', 'Medium', 'High', 'Ultra'):
        f = os.path.join(A, 'Settings', 'HDRP', 'MazraaHDRP_%s.asset' % lv)
        if not os.path.exists(f) or not os.path.exists(f + '.meta'): bad.append('missing ' + lv); continue
        if 'guid: 0cf1dab834d4ec34195b920ea7bbf9ec' not in open(f).read(): bad.append(lv + ' is not an HDRenderPipelineAsset')
        guids[lv] = re.search(r'guid: (\w+)', open(f + '.meta').read()).group(1)
    q = open(os.path.join(root, 'ProjectSettings', 'QualitySettings.asset')).read()
    levels = re.findall(r'\n    name: (\w+)[\s\S]*?customRenderPipeline: \{fileID: 11400000, guid: (\w+)', q)
    if [n for n, _ in levels] != ['Low', 'Medium', 'High', 'Ultra']: bad.append('quality levels %s' % [n for n, _ in levels])
    for n, g in levels:
        if guids.get(n) != g: bad.append(n + ' level uses another pipeline asset')
    gs = open(os.path.join(root, 'ProjectSettings', 'GraphicsSettings.asset')).read()
    if guids.get('High') and 'm_CustomRenderPipeline: {fileID: 11400000, guid: %s' % guids['High'] not in gs: bad.append('default pipeline is not MazraaHDRP_High')
    if 'Rendering.Universal' in gs: bad.append('URP global settings in GraphicsSettings')
    low = open(os.path.join(A, 'Settings', 'HDRP', 'MazraaHDRP_Low.asset')).read()
    if 'supportVolumetrics: 0' not in low or 'supportVolumetricClouds: 0' not in low or 'maxDirectionalShadowMapResolution: 1024' not in low: bad.append('Low is not GTX 1660 safe (volumetrics / shadow map)')
    urp = [os.path.relpath(p, A) for p, s in src.items() if 'Rendering.Universal' in s or 'UniversalAdditional' in s]
    return not bad and not urp, bad + urp


@test('Shader stripper and HDRP assets agree (stripped features are off in every MazraaHDRP asset; never strip a whole pass)')
def _():
    strip = open(os.path.join(A, 'Editor', 'MazraaShaderStripper.cs')).read()
    listed = set(re.findall(r'"([A-Z_0-9]+)"', strip[strip.index('Strip = new'):strip.index('};')]))
    bad = []
    must_keep = {'_ALPHATEST_ON', '_SURFACE_TYPE_TRANSPARENT', '_NORMALMAP', '_MASKMAP', '_DOUBLESIDED_ON', 'LIGHTMAP_ON', 'SHADOWS_SHADOWMASK', 'DYNAMICLIGHTMAP_ON',
                 '_FORWARD_PLUS', 'SCREEN_SPACE_SHADOWS_ON', 'USE_FPTL_LIGHTLIST', 'USE_CLUSTERED_LIGHTLIST', 'DECALS_3RT', 'DECALS_4RT', 'PUNCTUAL_SHADOW_MEDIUM', 'DIRECTIONAL_SHADOW_MEDIUM'}
    bad += sorted(listed & must_keep)
    assets = [open(f).read() for f in walk(os.path.join(A, 'Settings', 'HDRP'), ('.asset',))]
    if 'LOD_FADE_CROSSFADE' in listed and any('supportDitheringCrossFade: 1' in a for a in assets): bad.append('cross-fade stripped but enabled in an HDRP asset')
    if 'keep.Count == 0' not in strip: bad.append('no safety net')
    return not bad and len(assets) == 4, bad


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

@test('Look materials: every texture GUID resolves, shaders are HDRP Lit / TerrainLit / Unlit / built-in sky')
def _():
    bad = []
    known = {'6e4ae4064600d784cac1e41a9e6f2e59', 'eb607f5e8fc16e140b732f33d26c8c31', 'c4edd00ff2db5b24391a4fcb1762e459', '0000000000000000f000000000000000'}
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

@test('Materials are HDRP: no .mat in Assets uses URP Lit / Terrain Lit / Simple Lit or a removed Mazraa shader; HDRP/Lit maps use HDRP names')
def _():
    gone = {'933532a4fcc9baf4fa0491de14d08ed7', '69c1f799e772cb6438f56c23efccb782', '8d2bb70cbf9db8d4da26e15b26e74248', '5c911d097b135c6a84c1bc7fd358b3b4',
            '83f40e6f8c854c6e90eed14f9a3c6a69', 'cfc258d465c842c089170691c2465748'}
    bad = []
    for p in walk(A, ('.mat',)):
        s = open(p, encoding='utf-8', errors='ignore').read()
        m = re.search(r'm_Shader: \{fileID: -?\d+, guid: (\w+)', s)
        if m and m.group(1) in gone: bad.append(os.path.relpath(p, A))
        if m and m.group(1) == '6e4ae4064600d784cac1e41a9e6f2e59' and re.search(r'- _BaseMap:\s*\n\s*m_Texture: \{fileID: 2800000', s): bad.append(os.path.relpath(p, A) + ' _BaseMap on HDRP/Lit')
    return not bad, bad[:20]

@test('Look assets credited (Assets/ThirdParty/CREDITS.md lists Poly Haven and every source asset)')
def _():
    c = open(os.path.join(A, 'ThirdParty', 'CREDITS.md'), encoding='utf-8').read()
    srcs = ['sandy_gravel_02', 'gravelly_sand', 'dry_riverbed_rock', 'brown_mud_02', 'grass_ground', 'aerial_mud_1', 'rough_plaster_brick_02', 'white_stucco',
            'concrete_wall_008', 'corrugated_iron', 'rusty_metal_sheet', 'weathered_brown_planks', 'rough_linen', 'curly_teddy_natural', 'hessian_230',
            'palm_bark', 'bark_brown_02', 'palm_tree_bark', 'jacaranda_tree', 'grass_medium_01', 'island_tree_02', 'shrub_04']
    miss = [x for x in srcs if x not in c]
    return 'Poly Haven' in c and not miss, miss
# ---------------------------------------------------------------- online co-op
@test('Netcode / Unity Gaming Services code only in define-guarded files; Windows defines and packages agree')
def _():
    bad = []
    for p, s in src.items():
        uses_ngo = re.search(r'^using Unity\.(Netcode|Networking\.Transport)', s, re.M)
        uses_ugs = re.search(r'^using Unity\.Services\.', s, re.M)
        first = s.lstrip('\ufeff').split('\n', 1)[0].strip()
        if uses_ugs and not first.startswith('#if MAZRAA_ONLINE && MAZRAA_NETCODE'): bad.append('UGS outside MAZRAA_ONLINE: ' + os.path.relpath(p, A))
        elif uses_ngo and not uses_ugs and not first.startswith('#if MAZRAA_NETCODE'): bad.append('Netcode outside MAZRAA_NETCODE: ' + os.path.relpath(p, A))
    ps = open(os.path.join(root, 'ProjectSettings', 'ProjectSettings.asset'), encoding='utf-8', errors='ignore').read()
    m = re.search(r'^    Standalone: (.*)$', ps[ps.index('scriptingDefineSymbols:'):], re.M)
    defs = m.group(1).split(';') if m else []
    man = json.load(open(os.path.join(root, 'Packages', 'manifest.json')))['dependencies']
    if 'MAZRAA_NETCODE' in defs and not ('com.unity.netcode.gameobjects' in man and 'com.unity.transport' in man): bad.append('MAZRAA_NETCODE without the Netcode packages')
    if 'MAZRAA_ONLINE' in defs and 'com.unity.services.multiplayer' not in man: bad.append('MAZRAA_ONLINE without com.unity.services.multiplayer')
    if 'MAZRAA_ONLINE' in defs and 'MAZRAA_NETCODE' not in defs: bad.append('MAZRAA_ONLINE needs MAZRAA_NETCODE')
    return not bad, bad + ['Standalone defines: ' + ';'.join(defs)]


@test('No leftover merge-conflict markers in project files')
def _():
    bad = []
    for base in (A, os.path.join(A, '..', 'ProjectSettings'), os.path.join(A, '..', 'Packages')):
        for p in walk(base, ('.meta', '.cs', '.json', '.asset', '.unity', '.prefab', '.mat', '.shader')):
            try: t = open(p, encoding='utf-8', errors='ignore').read()
            except Exception: continue
            if re.search(r'^(<<<<<<< |>>>>>>> )', t, re.M): bad.append(os.path.relpath(p, A))
    return not bad, bad

# ---------------------------------------------------------------- real-Unity compile / runtime risks (QA pass, docs/BUG_AUDIT.md)
# The headless fakes (Tests/Headless/Fake*.cs) are more lenient than Unity 6: these tests catch what they let through.
def strip_comments(s):
    s = re.sub(r'/\*.*?\*/', '', s, flags=re.S)
    return re.sub(r'//[^\n]*', '', s)

code = {p: strip_comments(s) for p, s in src.items()}
starter = {p: open(p, encoding='utf-8').read() for p in walk(os.path.join(A, 'StarterAssets'), ('.cs',))}
editor = {p: open(p, encoding='utf-8').read() for p in walk(os.path.join(A, 'Editor'), ('.cs',))}

@test('Game code uses no fake-only members (they exist only in Tests/Headless fakes, not in Unity)')
def _():
    # Members the fakes add for test bookkeeping; real Unity / packages don't have them, so using one breaks the build.
    fake_only = [r'\bFakeEngine\b', r'\bFakePhysics\b', r'\bObjectCloner\b', r'\.AddConfigured\b', r'Application\.LastUrl', r'\.oneShots\b',
                 r'Screen\.SetResolutionCalls', r'ScreenCapture\.(LastPath|Count)\b', r'PlayerPrefs\.Clear\(', r'\.description_product\b',
                 r'\.(lowMotor|highMotor)\b', r'\.FakeComplete\(', r'RebindingOperation\.Current\b', r'SceneManager\.LoadCount\b', r'\.released\b',
                 r'Shader\.Globals\b', r'Graphics\.(InstancedCalls|InstancesDrawn)\b', r'DynamicGI\.Updates\b', r'\.TextureProperties\b',
                 r'\.alphamaps\b', r'Resources\.(LoadMaterial|LoadTexture|ImageSize|LoadLook)\b', r'Random\.R\b']
    bad = []
    for p, s in list(code.items()) + [(p, strip_comments(s)) for p, s in starter.items()]:
        for pat in fake_only:
            for m in re.finditer(pat, s):
                bad.append(f'{os.path.relpath(p, A)}:{s[:m.start()].count(chr(10)) + 1}: {m.group(0)}')
    return not bad, bad

@test('No writes to read-only Unity properties (fakes let Screen.width, Time.time, Camera.main, *.current... be assigned)')
def _():
    pat = r'\b(Screen\.(width|height|currentResolution)|Terrain\.activeTerrain|Camera\.(main|allCameras)|Time\.(time|deltaTime|frameCount|unscaledTime|unscaledDeltaTime|realtimeSinceStartup)|(Keyboard|Gamepad|Mouse|Touchscreen)\.current|\.currentControlScheme|Debug\.isDebugBuild|\.isActiveAndEnabled|\.activeInHierarchy|\.activeSelf|\.(wasPressedThisFrame|wasReleasedThisFrame|isPressed))\s*=(?!=)'
    bad = [f'{os.path.relpath(p, A)}:{s[:m.start()].count(chr(10)) + 1}: {m.group(0)}' for p, s in code.items() for m in re.finditer(pat, s)]
    return not bad, bad

@test('Input System control types (Vector2Control, AxisControl, AnyKeyControl...) have their using (fakes keep them in UnityEngine.InputSystem)')
def _():
    bad = [os.path.relpath(p, A) for p, s in list(code.items()) + list(starter.items())
           if re.search(r'\b(Vector2Control|Vector3Control|AnyKeyControl|DeltaControl|IntegerControl|TouchControl|QuaternionControl|DoubleControl|TouchPressControl)\b', strip_comments(s))
           and 'UnityEngine.InputSystem.Controls' not in s]
    return not bad, bad

@test('Runtime scripts use no editor-only API outside #if UNITY_EDITOR (the player build has no UnityEditor)')
def _():
    bad = []
    for p, s in list(src.items()) + list(starter.items()):
        depth = 0; editor_depth = []
        for i, line in enumerate(s.split('\n'), 1):
            t = line.strip()
            if t.startswith('#if'):
                depth += 1
                if 'UNITY_EDITOR' in t and '!UNITY_EDITOR' not in t: editor_depth.append(depth)
            elif t.startswith('#endif'):
                if editor_depth and editor_depth[-1] == depth: editor_depth.pop()
                depth -= 1
            elif t.startswith('#else') and editor_depth and editor_depth[-1] == depth:
                editor_depth.pop()
            if editor_depth or t.startswith('//'): continue
            if re.search(r'\bUnityEditor\b|\bAssetDatabase\.|\bEditorApplication\.|\bEditorUtility\.|\bPrefabUtility\.|\[MenuItem\(|\bHandles\.|\bEditorGUI(Layout)?\.', t):
                bad.append(f'{os.path.relpath(p, A)}:{i}: {t[:100]}')
    return not bad, bad

@test('Every MonoBehaviour (direct or via a game base class) lives in a file of the same name, incl. StarterAssets')
def _():
    decl = {}
    allfiles = list(src.items()) + list(starter.items())
    for p, s in allfiles:
        for m in re.finditer(r'\bclass\s+(\w+)\s*(?:<[^>]*>)?\s*:\s*([\w.]+)', strip_comments(s)):
            decl.setdefault(m.group(1), []).append((p, m.group(2).split('.')[-1], s[max(0, m.start() - 60):m.start()]))
    mb = {'MonoBehaviour', 'NetworkBehaviour', 'UIBehaviour'}
    changed = True
    while changed:
        changed = False
        for cls, ds in decl.items():
            if cls not in mb and any(b in mb for _, b, _ in ds): mb.add(cls); changed = True
    bad = []
    for cls, ds in decl.items():
        for p, b, pre in ds:
            if b in mb and cls not in ('MonoBehaviour',) and os.path.basename(p)[:-3] != cls and 'abstract' not in pre.split('\n')[-1]:
                bad.append(f'{cls} in {os.path.relpath(p, A)}')
    return not bad, bad

@test('StarterAssets scripts (not compiled by the headless tests) only use game members that exist')
def _():
    # FirstPersonController reads PlayerLife.* etc.: if a game class renames a member the real build breaks, the fakes don't notice.
    game_classes = {}
    for p, s in src.items():
        for m in re.finditer(r'\b(?:class|struct|enum)\s+(\w+)', s): game_classes.setdefault(m.group(1), []).append(s)
    bad = []
    for p, s in starter.items():
        for m in re.finditer(r'\b([A-Z]\w+)\.([A-Z]\w*)\b', strip_comments(s)):
            cls, mem = m.group(1), m.group(2)
            if cls not in game_classes: continue
            if not any(re.search(r'\b' + mem + r'\b', g) for g in game_classes[cls]):
                bad.append(f'{os.path.relpath(p, A)}: {cls}.{mem}')
    return not bad, sorted(set(bad))

@test('Types saved with JsonUtility serialize in real Unity ([Serializable] nested types; no Dictionary/HashSet/readonly/auto-property data)')
def _():
    # The fake JsonUtility is Newtonsoft, which saves all of these; Unity's JsonUtility silently drops them (lost saves).
    classes = {}
    for p, s in src.items():
        for m in re.finditer(r'((?:\[[^\]]*\]\s*)*)(?:public |internal |private |protected |sealed |static |partial )*(class|struct)\s+(\w+)[^{;=]*\{', s):
            i = m.end(); d = 1
            while d and i < len(s):
                d += {'{': 1, '}': -1}.get(s[i], 0); i += 1
            classes.setdefault(m.group(3), []).append((p, 'Serializable' in m.group(1), s[m.end():i - 1]))
    enums = set(re.findall(r'\benum\s+(\w+)', allsrc))
    builtin = {'int', 'float', 'double', 'bool', 'string', 'long', 'byte', 'short', 'char', 'uint', 'Vector2', 'Vector3', 'Vector4', 'Color', 'Quaternion', 'Color32', 'Rect', 'Bounds', 'Vector2Int', 'Vector3Int'}
    def top(body):
        out, d = [], 0
        for ch in body:
            if ch == '{': d += 1
            if d == 0: out.append(ch)
            if ch == '}': d -= 1
        return ''.join(out)
    def resolve(n, prefer):
        lst = classes[n]
        return next((c for c in lst if c[0] == prefer), lst[0])
    problems, seen = [], set()
    def check(n, path, prefer):
        if n not in classes: return
        p, ser, body = resolve(n, prefer)
        if (p, n) in seen: return
        seen.add((p, n)); rel = os.path.relpath(p, A)
        if path and not ser: problems.append(f'{n} (field {path}) is not [Serializable] ({rel})')
        for pm in re.finditer(r'public\s+(?!static|override|abstract|virtual)[\w.<>,\[\]]+\s+(\w+)\s*\{\s*get;\s*(?:private\s+|protected\s+|internal\s+)?set;', body):
            problems.append(f'{n}.{pm.group(1)}: auto-property ({rel})')
        for stmt in top(body).split(';'):
            stmt = re.sub(r'^.*?\b(class|struct|enum)\s+\w+[^\[]*', '', stmt, flags=re.S)
            mm = re.match(r'^\s*((?:\[[^\]]*\]\s*)*)(public|private|protected|internal)?\s*((?:static|const|readonly|new|volatile)\s+)*([\w.<>,\[\] ?]+?)\s+(\w+(?:\s*,\s*\w+)*)\s*(=.*)?$', stmt, re.S)
            if not mm: continue
            attrs, acc, typ, names = mm.group(1), mm.group(2) or 'private', mm.group(4).strip(), mm.group(5)
            head = stmt.split('=')[0]
            if re.search(r'\b(static|const)\b', head) or '(' in head or '=>' in stmt or 'NonSerialized' in attrs: continue
            if typ in ('return', 'using', 'var', 'throw', 'new', 'else', 'goto', 'yield', 'get', 'set'): continue
            if not (acc == 'public' or 'SerializeField' in attrs): continue
            if re.search(r'\breadonly\b', head): problems.append(f'{n}.{names}: readonly ({rel})'); continue
            t = typ.replace(' ', '')
            if re.search(r'(^|[.<])(Dictionary|HashSet|Queue|Stack|IList|IEnumerable|IDictionary|SortedList|LinkedList)<', t) or '?' in t or ',]' in t or '[][]' in t or 'List<List<' in t:
                problems.append(f'{n}.{names}: {typ} ({rel})'); continue
            inner = re.sub(r'^(List<(.+)>|(.+)\[\])$', lambda x: x.group(2) or x.group(3), t).split('.')[-1]
            if inner not in builtin and inner not in enums: check(inner, n + '.' + names.split(',')[0].strip(), p)
    roots = set()
    for p, s in src.items():
        for m in re.finditer(r'JsonUtility\.FromJson<([\w.]+)>', s): roots.add((m.group(1).split('.')[-1], p))
        for m in re.finditer(r'JsonUtility\.ToJson\(\s*new\s+(\w+)', s): roots.add((m.group(1), p))
        for m in re.finditer(r'\b(?:LoadAs|\.Load)<(\w+)>', s):
            if m.group(1) not in ('T', 'TextAsset', 'GameObject', 'Material', 'Texture2D', 'Font', 'Sprite', 'AudioClip', 'Shader', 'Mesh'): roots.add((m.group(1), p))
    for r, rp in sorted(roots):
        if r == 'T' or re.fullmatch(r'T[A-Z]\w*', r) and re.search(r'<\s*' + r + r'\s*>\s*\(', src[rp]): continue   # generic parameter, not a type
        check(r, '', rp)
    return not problems and len(seen) > 30, problems + [f'{len(seen)} types checked']

@test('Static events: every handler a script adds to a static event is removed again (no calls into destroyed objects after a reload)')
def _():
    bad = []
    for p, s in code.items():
        for m in re.finditer(r'\b((?:GameEvents|SceneManager|Loc|Application|GameInput|NetSession|Regions|GameModes|SaveSlots)\.\w+)\s*\+=\s*([^;]+);', s):
            ev, h = m.group(1), m.group(2).strip()
            if '=>' in h or 'delegate' in h:
                if 'RuntimeInitializeOnLoadMethod' not in s and 'static class' not in s[:m.start()]:
                    bad.append(f'{os.path.relpath(p, A)}: lambda on {ev} can never be removed')
                continue
            if not re.search(re.escape(ev) + r'\s*-=\s*' + re.escape(h) + r'\s*;', s):
                bad.append(f'{os.path.relpath(p, A)}: {ev} += {h} without -=')
    return not bad, bad

@test('FixedUpdate reads no one-frame input (presses are missed between physics steps) and allocates no arrays / LINQ')
def _():
    bad = []
    for p, s in code.items():
        for m in re.finditer(r'void\s+FixedUpdate\s*\(\s*\)\s*\{', s):
            i, d = m.end(), 1
            while d and i < len(s):
                d += {'{': 1, '}': -1}.get(s[i], 0); i += 1
            body = s[m.end():i]
            for pat in [r'wasPressedThisFrame|wasReleasedThisFrame|WasPressedThisFrame|WasReleasedThisFrame|GameInput\.Pressed|GetKeyDown|GetButtonDown|MenuNav\.',
                        r'new\s+\w+\s*\[\s*\]\s*\{|new\s*\[\s*\]|new\s+(List|Dictionary|HashSet)<|\.(Where|Select|ToList|ToArray|OrderBy|Any|First)\(']:
                for x in re.finditer(pat, body):
                    bad.append(f'{os.path.relpath(p, A)}:{s[:m.end() + x.start()].count(chr(10)) + 1}: {x.group(0)}')
    return not bad, bad

@test('Save file is written atomically with a backup, and a damaged slot is never copied over the save')
def _():
    sd = src[os.path.join(A, 'Scripts', 'Base', 'SaveData.cs')]
    ss = src[os.path.join(A, 'Scripts', 'UI', 'SaveSlots.cs')]
    bad = []
    if 'File.WriteAllText(Path,' in sd: bad.append('SaveData writes the save file in place')
    if 'BackupPath' not in sd or '.tmp' not in sd: bad.append('SaveData has no temp file / backup')
    if 'IsReadable(SlotPath(slot))' not in ss: bad.append('SaveSlots copies a slot without checking it')
    return not bad, bad

# ---------------------------------------------------------------- report
ok = sum(1 for r in results if r[1])
for name, passed, details in results:
    print(('PASS ' if passed else 'FAIL ') + name)
    for d in details[:25]:
        if not passed or name.startswith('Keyboard'):
            print('      ' + str(d))
print(f'\n{ok}/{len(results)} static tests passed')
