#!/bin/bash
# Unity を起動せずに、同梱の Roslyn で全アセンブリをコンパイルし、Rules の単体テストを走らせる。
# ライセンス認証が無い環境 (CI など) 向け。
#   UNITY_EDITOR_DIR  Unity の Editor/Data (既定: ~/Unity/Hub/Editor/6000.3.21f1/Editor/Data)
#   REF_LIBRARY       uGUI と Input System の DLL を借りる Unity プロジェクトの Library
#                     (既定: ../Unity_ROS2_Robot_Simulator/Library)
#   DOTNET_REF        .NET 8 の参照アセンブリ (テスト実行に使う。無ければテストは飛ばす)
set -u
ROOT=$(cd "$(dirname "$0")/.." && pwd)
P=$ROOT/Assets
U=${UNITY_EDITOR_DIR:-$HOME/Unity/Hub/Editor/6000.3.21f1/Editor/Data}
S=${REF_LIBRARY:-$ROOT/../Unity_ROS2_Robot_Simulator/Library}
OUT=${OUT_DIR:-$ROOT/Temp/check}; mkdir -p "$OUT"
CSC="$U/NetCoreRuntime/dotnet $U/DotNetSdkRoslyn/csc.dll -nologo -noconfig -nostdlib+ -langversion:9.0 -nowarn:1701,1702,CS0649,CS0169,CS8632"
NS=-r:$U/NetStandard/ref/2.1.0/netstandard.dll
ENGINE="$(for f in "$U"/Managed/UnityEngine/UnityEngine.*Module.dll; do printf -- '-r:%s ' "$f"; done)"
EDITOR="$(for f in "$U"/Managed/UnityEngine/UnityEditor.*Module.dll; do printf -- '-r:%s ' "$f"; done)"
UI=-r:$S/ScriptAssemblies/UnityEngine.UI.dll
INPUT="-r:$S/ScriptAssemblies/Unity.InputSystem.dll -r:$S/ScriptAssemblies/Unity.AI.Navigation.dll"
NUNIT_DLL=$(ls "$S"/PackageCache/com.unity.ext.nunit@*/net40/unity-custom/nunit.framework.dll 2>/dev/null | head -1)
DEF="-define:UNITY_EDITOR;UNITY_6000_3;UNITY_6000_0_OR_NEWER;UNITY_2023_1_OR_NEWER;UNITY_STANDALONE;ENABLE_INPUT_SYSTEM"
rc=0
echo "== CoreSim.Rules =="
$CSC -target:library $NS -out:"$OUT/CoreSim.Rules.dll" "$P"/Scripts/Rules/*.cs || rc=1
echo "== CoreSim.Net =="
$CSC -target:library $NS -out:"$OUT/CoreSim.Net.dll" "$P"/Scripts/Net/*.cs || rc=1
echo "== Assembly-CSharp =="
$CSC -target:library $DEF $NS $ENGINE $UI $INPUT -r:"$OUT/CoreSim.Rules.dll" -r:"$OUT/CoreSim.Net.dll" -out:"$OUT/Assembly-CSharp.dll" "$P"/Scripts/Game/*.cs || rc=1
echo "== Assembly-CSharp-Editor =="
$CSC -target:library $DEF $NS $ENGINE $EDITOR -r:"$OUT/Assembly-CSharp.dll" -r:"$OUT/CoreSim.Rules.dll" -out:"$OUT/Assembly-CSharp-Editor.dll" "$P"/Editor/*.cs || rc=1
if [ -n "$NUNIT_DLL" ]; then
  echo "== CoreSim.Tests.EditMode =="
  $CSC -target:library $NS -r:"$NUNIT_DLL" -r:"$U/NetStandard/compat/2.1.0/shims/netfx/mscorlib.dll" -r:"$OUT/CoreSim.Rules.dll" -r:"$OUT/CoreSim.Net.dll" -out:"$OUT/CoreSim.Tests.EditMode.dll" "$P"/Tests/EditMode/*.cs || rc=1
  REF=${DOTNET_REF:-$(dirname "$(find /usr/lib/dotnet /usr/share/dotnet -path '*Microsoft.NETCore.App.Ref/8.*/ref/net8.0/System.Runtime.dll' 2>/dev/null | head -1)")}
  if [ -f "$REF/System.Runtime.dll" ] && command -v dotnet >/dev/null; then
    echo "== run Rules tests (dotnet) =="
    cp "$NUNIT_DLL" "$OUT/"
    $CSC -target:exe $(for f in "$REF"/*.dll; do printf -- '-r:%s ' "$f"; done) -r:"$OUT/nunit.framework.dll" \
      -out:"$OUT/runner.dll" "$ROOT/tools/TestRunner.cs" "$P"/Scripts/Rules/*.cs "$P"/Scripts/Net/*.cs "$P"/Tests/EditMode/*.cs || rc=1
    echo '{ "runtimeOptions": { "tfm": "net8.0", "framework": { "name": "Microsoft.NETCore.App", "version": "8.0.0" } } }' > "$OUT/runner.runtimeconfig.json"
    dotnet "$OUT/runner.dll" || rc=1
  else
    echo "(dotnet の参照アセンブリが無いのでテスト実行は飛ばす)"
  fi
fi
[ $rc -eq 0 ] && echo "ALL OK" || echo "FAILED"
exit $rc
