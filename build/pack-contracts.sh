#!/usr/bin/env bash
# Packs the two projects a plugin builds against and drops them in the local feed.
#
# The feed is a plain folder of .nupkg files, registered once as a NuGet source (see AGENTS.md).
# A plugin then takes a PackageReference instead of a ProjectReference into a sibling checkout,
# which is what let the plugin repo break every time this one changed branch.
#
# Re-packing the same version is the normal case while contracts are moving, and NuGet will not
# notice it on its own: a version it has already extracted into the global packages folder is
# never re-read. So the matching cache entries go first — without that, a plugin builds against
# whatever it restored the first time and the errors make no sense.
set -euo pipefail

FEED="${KHOST_LOCAL_FEED:-$HOME/.nuget/khost-local}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

VERSION="$(sed -n 's/.*<ContractsVersion>\(.*\)<\/ContractsVersion>.*/\1/p' "$ROOT/Directory.Build.props")"
if [ -z "$VERSION" ]; then
    echo "Could not read ContractsVersion from Directory.Build.props" >&2
    exit 1
fi

mkdir -p "$FEED"

for package in KHost.Abstractions KHost.Common; do
    lower="$(echo "$package" | tr '[:upper:]' '[:lower:]')"
    rm -rf "${NUGET_PACKAGES:-$HOME/.nuget/packages}/$lower/$VERSION"
    rm -f "$FEED/$package.$VERSION.nupkg"

    dotnet pack "$ROOT/src/$package/$package.csproj" \
        --configuration Release \
        --output "$FEED" \
        -p:ContinuousIntegrationBuild=true
done

echo
echo "Packed $VERSION into $FEED:"
ls -1 "$FEED" | sed 's/^/  /'
