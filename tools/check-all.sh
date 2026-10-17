#!/usr/bin/env bash
# Run every local check before opening a PR or deploying. Nothing is uploaded.
#
#   tools/check-all.sh            everything (about 6 minutes)
#   tools/check-all.sh --fast     skip tests tagged [Trait("Category", "Slow")] and the site build (about 2 minutes)
#
# Steps: C# tests in the default locale and in zh_CN.UTF-8 (catches culture-dependent sorting and
# formatting), cloud save API tests, generated-file checks (cards.md, ranked decks), deploy tool
# tests, then the Azure site build (which also runs the Node tests) and its package check.
set -euo pipefail
cd "$(dirname "$0")/.."
fast=0
for arg in "$@"; do
    case "$arg" in
        --fast) fast=1 ;;
        -h|--help) sed -n '2,10p' "$0"; exit 0 ;;
        *) echo "unknown option: $arg" >&2; exit 2 ;;
    esac
done
[ -x "$HOME/.dotnet/dotnet" ] && export PATH="$HOME/.dotnet:$PATH"
# A Python virtual environment (e.g. the Azure CLI one) may lack Pillow; the card-art step needs the system Python.
if [ -n "${VIRTUAL_ENV:-}" ]; then PATH="$(printf '%s' "$PATH" | tr ':' '\n' | grep -vxF "$VIRTUAL_ENV/bin" | paste -sd:)"; export PATH; fi

filter=()
[ "$fast" = 1 ] && filter=(--filter "Category!=Slow")
step() { printf '\n== %s\n' "$*"; }

step "WcgTests (default locale)"
dotnet test WcgTests/WcgTests.csproj --nologo "${filter[@]}"
step "WcgTests (zh_CN.UTF-8)"
LANG=zh_CN.UTF-8 LC_ALL=zh_CN.UTF-8 DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0 dotnet test WcgTests/WcgTests.csproj --nologo "${filter[@]}"
step "Wcg.Cloud.Tests"
dotnet test Wcg.Cloud.Tests/Wcg.Cloud.Tests.csproj --nologo

step "Generated files are in sync"
python3 tools/CardDocs/build_cards_md.py --check
python3 tools/RankedDecks/build_ranked_decks.py --check

step "Deploy tool tests"
for t in SoulOath.Static/tools/test-*.py; do python3 "$t"; done

step "Every Node test is run by the site build"
missing=0
for t in SoulOath.Static/tools/*.test.mjs; do
    name="tools/$(basename "$t")"
    if ! grep -qF "'$name'" SoulOath.Static/tools/build-site.mjs; then echo "not in build-site.mjs: $name" >&2; missing=1; fi
done
[ "$missing" = 0 ] || exit 1

if [ "$fast" = 1 ]; then
    step "Node tests"
    [ -f WcgWeb/Client/node_modules/esbuild/package.json ] || (cd WcgWeb/Client && npm ci)
    (cd SoulOath.Static && node --test tools/*.test.mjs)
    printf '\nFast checks passed (slow tests and the site build were skipped).\n'
    exit 0
fi
step "Azure site build (includes Node tests)"
node SoulOath.Static/tools/build-site.mjs --azure .build-tmp/azure-blob-site
node SoulOath.Static/tools/check-azure-output.mjs .build-tmp/azure-blob-site/dist
printf '\nAll checks passed.\n'
