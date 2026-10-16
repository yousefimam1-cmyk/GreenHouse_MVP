#!/bin/bash
# Headless gameplay tests: compiles every game script against a small fake UnityEngine and plays the game logic.
# Needs mono (mcs) and Newtonsoft.Json.dll (nuget Newtonsoft.Json 13) next to this script.
set -e
cd "$(dirname "$0")"
[ -f Newtonsoft.Json.dll ] || { curl -sSL -o nj.nupkg https://www.nuget.org/api/v2/package/Newtonsoft.Json/13.0.3 && unzip -q -o -j nj.nupkg lib/net45/Newtonsoft.Json.dll -d . && rm nj.nupkg; }
mcs -langversion:Latest -nowarn:0169,0414,0649,0067,0618,0108,0114,0660,0661,0219,0168 -r:Newtonsoft.Json.dll -out:tests.exe \
  FakeUnity.cs FakePkg.cs GameTests.cs FarmLifeTests.cs FrontEndTests.cs $(find ../../Assets/Scripts -name "*.cs")
mono tests.exe ../../Assets/Resources
