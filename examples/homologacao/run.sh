#!/bin/sh
# Exemplo neutro de homologação. Sem WEBMANIA_LIVE=1 não compila nem faz requisições (saída 77).
# Compila uma cópia do checkout (o código-fonte pode estar somente leitura) e executa o exemplo.
set -eu
here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
root=$(CDPATH= cd -- "$here/../.." && pwd)
if [ "${WEBMANIA_LIVE:-}" != "1" ]; then printf '%s\n' '{"result":"blocked"}'; exit 77; fi
work=$(mktemp -d "${TMPDIR:-/tmp}/webmania-example.XXXXXX")
(cd "$root" && tar --exclude=./.git --exclude='*/bin' --exclude='*/obj' -cf - .) | (cd "$work" && tar -xf -)
dotnet build "$work/src/Webmania.DnsHelper" -c Release --nologo >&2
dotnet build "$work/examples/homologacao" -c Release --nologo >&2
exec dotnet "$work/examples/homologacao/bin/Release/net10.0/Homologacao.dll"
