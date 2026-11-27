#!/usr/bin/env bash
# Builds the plugin with Mono's mcs compiler, for machines without the .NET SDK.
# Usage: tools/build-mono.sh "/path/to/KSP_x64_Data/Managed" [output dir]
# The game's DLLs are only referenced, never copied into the repository.
set -euo pipefail

managed="${1:?usage: build-mono.sh MANAGED_DIR [OUT_DIR]}"
out="${2:-$(dirname "$0")/../GameData/RealCosmonauts/Plugins}"
src="$(dirname "$0")/../src/RealCosmonauts"

refs=()
for dll in Assembly-CSharp Assembly-CSharp-firstpass UnityEngine UnityEngine.CoreModule \
           UnityEngine.AnimationModule UnityEngine.PhysicsModule UnityEngine.SharedInternalsModule \
           UnityEngine.UI UnityEngine.UIModule UnityEngine.IMGUIModule UnityEngine.TextRenderingModule \
           UnityEngine.InputLegacyModule; do
  refs+=("-r:$managed/$dll.dll")
done

mkdir -p "$out"
mcs -target:library -optimize+ -langversion:7.2 -out:"$out/RealCosmonauts.dll" \
    -r:System.Core.dll "${refs[@]}" "$src"/*.cs
echo "Built $out/RealCosmonauts.dll"
