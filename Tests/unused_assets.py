#!/usr/bin/env python3
"""
Report assets that nothing in the build uses (read-only: deletes nothing).

    python3 Tests/unused_assets.py <project> [--overlay <other checkout>] > Tests/UNUSED_ASSETS.md

Roots = scenes in EditorBuildSettings + everything in a Resources folder + ProjectSettings + shaders that code finds
with Shader.Find. References are followed through every Unity text asset (.unity .prefab .mat .asset .controller
.shadergraph ...), model .meta files (material remaps) and binary .asset files (GUIDs in binary form).
--overlay: a second checkout (e.g. a sparse worktree) whose Assets files are added on top of <project>.
Scripts (.cs), editor code, docs and packages are not judged (they are used from code, not by reference).
"""
import os, re, sys, collections

args = sys.argv[1:]
overlay = None
if '--overlay' in args:
    i = args.index('--overlay'); overlay = args[i + 1]; del args[i:i + 2]
root = args[0] if args else '.'

TEXT = {'.unity', '.prefab', '.mat', '.asset', '.controller', '.overrideController', '.anim', '.playable', '.mask',
        '.shadergraph', '.shadersubgraph', '.terrainlayer', '.lighting', '.physicMaterial', '.physicsMaterial2D',
        '.spriteatlas', '.spriteatlasv2', '.mixer', '.preset', '.guiskin', '.fontsettings', '.renderTexture',
        '.cubemap', '.flare', '.signal', '.brush', '.vfx', '.vfxoperator', '.asmdef', '.asmref', '.inputactions',
        '.uxml', '.uss', '.tss', '.json'}
SKIP_EXT = {'.cs', '.dll', '.asmdef', '.asmref', '.md', '.txt', '.pdf', '.rsp', '.so', '.jslib', '.xml', '.py', '.sh'}
HEX = re.compile(rb'[0-9a-f]{32}')

files = {}          # rel path -> absolute path (overlay wins)
for base in [root] + ([overlay] if overlay else []):
    a = os.path.join(base, 'Assets')
    for d, dirs, fs in os.walk(a):
        for f in fs:
            p = os.path.join(d, f)
            files[os.path.relpath(p, base).replace(os.sep, '/')] = p

guid_of, path_of = {}, {}
for rel, p in files.items():
    if not rel.endswith('.meta'): continue
    asset = rel[:-5]
    try: m = re.search(r'^guid: ([0-9a-f]{32})', open(p, errors='ignore').read(), re.M)
    except OSError: continue
    if not m: continue
    guid_of[asset] = m.group(1); path_of[m.group(1)] = asset

assets = sorted(a for a in guid_of if a in files and not os.path.isdir(files[a]))

def size(rel):
    p = files[rel]
    try:
        with open(p, 'rb') as fh: head = fh.read(200)
        if head.startswith(b'version https://git-lfs'):
            m = re.search(rb'size (\d+)', head)
            if m: return int(m.group(1))
        return os.path.getsize(p)
    except OSError: return 0

def binguid(g):
    return bytes(int(g[2 * i + 1] + g[2 * i], 16) for i in range(16))
bin_index = None

def refs(rel):
    """GUIDs referenced by an asset (its file if text/binary-serialized, plus its .meta)."""
    out = set()
    p = files.get(rel)
    ext = os.path.splitext(rel)[1]
    texts = []
    if p and ext in TEXT:
        try: data = open(p, 'rb').read()
        except OSError: data = b''
        if data[:5] == b'%YAML' or ext not in ('.asset',) or b'guid' in data[:4096]:
            texts.append(data)
        else:   # binary serialized asset (e.g. TerrainData)
            global bin_index
            if bin_index is None: bin_index = {binguid(g): g for g in path_of}
            for bg, g in bin_index.items():
                if bg in data: out.add(g)
    meta = files.get(rel + '.meta')
    if meta:
        try: texts.append(open(meta, 'rb').read())
        except OSError: pass
    for t in texts:
        for m in HEX.findall(t):
            g = m.decode()
            if g in path_of: out.add(g)
    out.discard(guid_of.get(rel))
    return out

graph = {a: refs(a) for a in assets}
referenced_by = collections.defaultdict(set)
for a, rs in graph.items():
    for g in rs: referenced_by[path_of[g]].add(a)

# roots
roots = set()
bs = os.path.join(overlay or root, 'ProjectSettings', 'EditorBuildSettings.asset')
if not os.path.exists(bs): bs = os.path.join(root, 'ProjectSettings', 'EditorBuildSettings.asset')
build_scenes = []
try:
    for g in re.findall(r'guid: ([0-9a-f]{32})', open(bs).read()):
        if g in path_of and path_of[g].endswith('.unity'): build_scenes.append(path_of[g])
except OSError: pass
roots.update(build_scenes)
roots.update(a for a in assets if '/Resources/' in a)
for base in [root] + ([overlay] if overlay else []):
    ps = os.path.join(base, 'ProjectSettings')
    if not os.path.isdir(ps): continue
    for f in os.listdir(ps):
        try: t = open(os.path.join(ps, f), 'rb').read()
        except OSError: continue
        for m in HEX.findall(t):
            if m.decode() in path_of: roots.add(path_of[m.decode()])
code = ''.join(open(files[r], errors='ignore').read() for r in files if r.endswith('.cs'))
for a in assets:
    if a.endswith('.shader'):
        m = re.search(r'Shader\s+"([^"]+)"', open(files[a], errors='ignore').read())
        if m and ('"' + m.group(1) + '"') in code: roots.add(a)

reach, stack = set(), list(roots)
while stack:
    a = stack.pop()
    if a in reach or a not in graph: continue
    reach.add(a)
    stack.extend(path_of[g] for g in graph[a])

def judged(a):
    ext = os.path.splitext(a)[1]
    return ext not in SKIP_EXT and '/Editor/' not in a and not a.startswith('Assets/Plugins/') and '/TextMesh Pro/' not in a

unref = [a for a in assets if judged(a) and a not in reach and not referenced_by.get(a)]
indirect = [a for a in assets if judged(a) and a not in reach and referenced_by.get(a)]

def mb(n): return '%.2f MB' % (n / 1048576.0)
def table(rows):
    out = ['| Asset | Size |', '|---|---:|']
    for a in sorted(rows, key=lambda r: (-size(r), r)): out.append('| %s | %s |' % (a, mb(size(a))))
    return '\n'.join(out)
def by_folder(rows):
    tot = collections.Counter(); cnt = collections.Counter()
    for a in rows:
        parts = a.split('/'); key = '/'.join(parts[:3]) if len(parts) > 3 else '/'.join(parts[:2])
        tot[key] += size(a); cnt[key] += 1
    out = ['| Folder | Files | Size |', '|---|---:|---:|']
    for k, v in tot.most_common(): out.append('| %s | %d | %s |' % (k, cnt[k], mb(v)))
    return '\n'.join(out)

print('# Unused asset candidates\n')
print('Generated by `python3 Tests/unused_assets.py <project>` (read-only scan, nothing deleted). '
      'Roots: build scenes (%s), every Resources folder, ProjectSettings, and shaders found by name in code. '
      'References are followed through scenes, prefabs, materials, other text assets, model .meta material remaps '
      'and binary .asset files. Scripts, editor code, plugins, TextMesh Pro and docs are not judged.\n'
      % (', '.join(build_scenes) or 'none found'))
print('**Check in the Unity editor before deleting anything** (right-click > Find References In Scene, or a '
      'dependency tool): assets loaded by path from code outside Resources, or used only by demo scenes, will show '
      'up here.\n')
print('Totals: %d judged assets, %d reachable from the build, %d never referenced (%s), %d referenced only by '
      'other unused assets (%s).\n' % (sum(1 for a in assets if judged(a)), sum(1 for a in assets if judged(a) and a in reach),
      len(unref), mb(sum(size(a) for a in unref)), len(indirect), mb(sum(size(a) for a in indirect))))
stale = [a for a in unref + indirect if a.startswith('Assets/Scenes/') and ('/Lightmap-' in a or '/ReflectionProbe-' in a)]
if stale:
    print('Note: %d baked lightmap / reflection-probe files of the game scenes are listed because the scene\'s LightingData.asset points '
          'to GUIDs that no longer exist (the .meta files were regenerated after the old .gitignore dropped them). '
          'Re-bake lighting (Window > Rendering > Lighting > Generate) instead of deleting them by hand.\n' % len(stale))
print('## By folder (both lists)\n')
print(by_folder(unref + indirect) + '\n')
print('## 1. Not referenced by any scene, prefab, material or other asset\n')
print(table(unref) + '\n')
print('## 2. Referenced only by assets that are themselves unused (demo scenes, unused prefabs/materials)\n')
print(table(indirect))
