#!/usr/bin/env bash
# Checks the ported source in KHost.Secrets/Interop against what it was ported from.
#
# It answers two separate questions, and keeping them apart is the point:
#
#   UPSTREAM MOVED   Git Credential Manager changed this file since the pinned commit. Nothing is
#                    broken; there may be a fix worth taking. Review the diff, port what matters,
#                    then re-pin.
#
#   LOCAL EDITED     Our copy no longer matches what was recorded when it was ported. That is
#                    allowed — the namespace, the header and a couple of inlined guards are already
#                    deviations — but it should never happen by accident, because every edit here
#                    is one more thing standing between us and a clean patch.
#
# Needs network. Run it before taking an upstream fix, and when something in Interop/ is touched.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INTEROP="$ROOT/src/KHost.Secrets/Interop"
MANIFEST="$INTEROP/UPSTREAM"

if [ ! -f "$MANIFEST" ]; then
    echo "No manifest at $MANIFEST — nothing to check against." >&2
    exit 1
fi

repo="$(awk '$1=="repo"    {print $2}' "$MANIFEST")"
prefix="$(awk '$1=="prefix" {print $2}' "$MANIFEST")"
commit="$(awk '$1=="commit" {print $2}' "$MANIFEST")"
raw="${repo/https:\/\/github.com/https://raw.githubusercontent.com}/$commit/$prefix"

echo "Upstream $repo"
echo "Pinned   $commit"
echo

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

moved=0
edited=0
checked=0

# Skip the header, the key=value lines and comments; what is left is one row per ported file.
while read -r local_rel upstream_rel upstream_sha local_sha; do
    case "$local_rel" in ''|'#'*|repo|prefix|commit) continue ;; esac
    [ -n "${local_sha:-}" ] || continue

    checked=$((checked + 1))
    local_path="$INTEROP/$local_rel"

    if [ ! -f "$local_path" ]; then
        echo "MISSING       $local_rel — recorded as ported, but not on disk"
        edited=$((edited + 1))
        continue
    fi

    actual_local="$(shasum -a 256 "$local_path" | cut -c1-16)"
    if [ "$actual_local" != "$local_sha" ]; then
        echo "LOCAL EDITED  $local_rel"
        edited=$((edited + 1))
    fi

    fetched="$work/$(echo "$upstream_rel" | tr '/' '_')"
    if ! curl -fsS --max-time 30 "$raw/$upstream_rel" -o "$fetched"; then
        echo "UNREACHABLE   $upstream_rel — could not fetch; upstream not checked"
        continue
    fi

    actual_upstream="$(shasum -a 256 "$fetched" | cut -c1-16)"
    if [ "$actual_upstream" != "$upstream_sha" ]; then
        echo "UPSTREAM MOVED $local_rel"
        echo "               recorded $upstream_sha, now $actual_upstream"
        moved=$((moved + 1))
    fi
done < "$MANIFEST"

echo
echo "$checked file(s) checked: $moved moved upstream, $edited edited locally."

# A moved upstream is news, not a failure — it is the thing this exists to tell you. A locally
# edited file is what should stop a build, because it is how a port quietly stops being one.
[ "$edited" -eq 0 ] || exit 1
