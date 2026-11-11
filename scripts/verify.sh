#!/usr/bin/env bash
set -euo pipefail
sdk_dotnet=${1:-dotnet}
repo_root=$(cd "$(dirname "$0")/.." && pwd)
if [[ "$sdk_dotnet" != /* ]]; then sdk_dotnet=$(command -v "$sdk_dotnet"); fi
cd "$repo_root"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false
"$sdk_dotnet" build tests/SocketTests -c Release --nologo
"$sdk_dotnet" tests/SocketTests/bin/Release/net10.0/Webmania.SocketTests.dll
if [[ $# -ge 2 ]]; then
  sdk_feed=$2
else
  sdk_feed="$repo_root/artifacts/feed"
  "$sdk_dotnet" pack src/Webmania.Sdk -c Release --no-build --no-restore -o "$sdk_feed"
fi
"$sdk_dotnet" tests/SocketTests/bin/Release/net10.0/Webmania.SocketTests.dll --consumers "$sdk_dotnet" "$repo_root" "$sdk_feed"
