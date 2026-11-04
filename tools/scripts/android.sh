#!/usr/bin/env bash
# Exporta o APK do celular (Android arm64). Precisa da Godot 4.7.2 .NET, dos modelos de exportação .NET, do SDK do .NET 10,
# do JDK 17 e das build-tools do Android (apksigner) configurados nas Editor Settings da Godot (export/android/*).
# A chave de assinatura fica FORA do git; o mesmo arquivo tem que assinar todas as versões, senão o Android recusa a atualização.
# Uso: GODOT=/caminho/Godot_v4.7.2-stable_mono_linux.x86_64 CHAVE=/caminho/dw4-android.keystore SENHA=... tools/scripts/android.sh saida.apk
set -euo pipefail
repo="$(cd "$(dirname "$0")/../.." && pwd)"
client="$repo/src/DW4.Client"
out="$(realpath -m "${1:?caminho do .apk}")"
export DW4_GODOT_NUPKGS="${DW4_GODOT_NUPKGS:-$(dirname "$GODOT")/GodotSharp/Tools/nupkgs}"
export MSBUILDDISABLENODEREUSE=1 UseSharedCompilation=false
export GODOT_ANDROID_KEYSTORE_RELEASE_PATH="$CHAVE" GODOT_ANDROID_KEYSTORE_RELEASE_USER="${USUARIO:-dw4}" GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD="$SENHA"
[ -f "$client/icone.png" ] || echo "aviso: falta src/DW4.Client/icone.png (ícone do launcher, fora do git)" >&2
"$GODOT" --headless --path "$client" --export-release "Android" "$out"
unzip -l "$out" | grep "DW4.Client.dll" >/dev/null || { echo "o APK saiu sem o C# (DW4.Client.dll)" >&2; exit 1; }
echo "APK: $out"
