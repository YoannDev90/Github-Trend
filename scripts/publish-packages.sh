#!/usr/bin/env bash
# publish-packages.sh
# Local helper script to produce framework-dependent (FDD) and self-contained (SCD)
# publish outputs for multiple RIDs.
# Outputs are organized under ./publish/{fdd,scd}/{rid}
# Usage: ./scripts/publish-packages.sh [tag]

set -euo pipefail
TAG=${1:-local}
ROOT=$(cd "$(dirname "$0")/.." && pwd)
cd "$ROOT"

RIDS=(win-x64 win-arm64 osx-x64 osx-arm64 linux-x64 linux-arm64)

for RID in "${RIDS[@]}"; do
  echo "\n=== Building for RID=$RID ==="
  FDD_OUT=./publish/fdd/${RID}
  SCD_OUT=./publish/scd/${RID}
  mkdir -p "$FDD_OUT" "$SCD_OUT"

  echo "Framework-dependent publish (RID=$RID) -> $FDD_OUT"
  # restore for specific RID to ensure project.assets.json contains the target
  dotnet restore ./Github-Trend.csproj --runtime $RID || true
  dotnet publish ./Github-Trend.csproj -c Release -r $RID --self-contained false -o "$FDD_OUT" --no-restore || true

  echo "Self-contained publish (RID=$RID) -> $SCD_OUT"
  # ensure restore for the RID before self-contained publish
  dotnet restore ./Github-Trend.csproj --runtime $RID || true
  dotnet publish ./Github-Trend.csproj -c Release -r $RID --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o "$SCD_OUT" --no-restore || true

  # packaging
  PKG_DIR=./publish/packages/${RID}
  mkdir -p "$PKG_DIR"
  STAGE_DIR=$(mktemp -d)

  # package FDD
  if [ -d "$FDD_OUT" ]; then
    echo "Packaging FDD for $RID"
    if [[ "$RID" == win-* ]]; then
      cp -r "$FDD_OUT"/. "$STAGE_DIR"/
      zip -r "$PKG_DIR/github-trend-${TAG}-${RID}-fdd.zip" "$STAGE_DIR" >/dev/null
    else
      cp -r "$FDD_OUT"/. "$STAGE_DIR"/
      tar -czf "$PKG_DIR/github-trend-${TAG}-${RID}-fdd.tar.gz" -C "$STAGE_DIR" .
    fi
  fi

  # package SCD
  if [ -d "$SCD_OUT" ]; then
    echo "Packaging SCD for $RID"
    if [[ "$RID" == win-* ]]; then
      rm -rf "$STAGE_DIR"/*
      cp -r "$SCD_OUT"/. "$STAGE_DIR"/
      zip -r "$PKG_DIR/github-trend-${TAG}-${RID}-scd.zip" "$STAGE_DIR" >/dev/null
    else
      rm -rf "$STAGE_DIR"/*
      cp -r "$SCD_OUT"/. "$STAGE_DIR"/
      tar -czf "$PKG_DIR/github-trend-${TAG}-${RID}-scd.tar.gz" -C "$STAGE_DIR" .
      if command -v hdiutil >/dev/null 2>&1; then
        DMG="$PKG_DIR/github-trend-${TAG}-${RID}-scd.dmg"
        echo "Creating DMG $DMG"
        hdiutil create -volname "Github-Trend" -srcfolder "$STAGE_DIR" -ov -format UDZO "$DMG" || true
      fi

      # Linux extra packaging: create naked binary, tar.gz, deb and rpm (best-effort)
      if [[ "$RID" == linux-* ]]; then
        echo "Linux packaging for $RID"
        # try to install upx and fpm if available
        if command -v apt-get >/dev/null 2>&1; then
          sudo apt-get update -y || true
          sudo apt-get install -y --no-install-recommends upx ruby ruby-dev build-essential rpm dpkg-dev fakeroot || true
          sudo gem install --no-document fpm || true
        fi

        BIN=$(find "$SCD_OUT" -maxdepth 1 -type f -executable -print | head -n1 || true)
        if [ -z "$BIN" ]; then
          BIN=$(find "$FDD_OUT" -maxdepth 1 -type f -executable -print | head -n1 || true)
        fi
        if [ -n "$BIN" ] && [ -f "$BIN" ]; then
          BASENAME=github-trend-${TAG}-${RID}
          NAKED="$PKG_DIR/${BASENAME}"
          cp "$BIN" "$NAKED" || true
          chmod +x "$NAKED" || true
          if command -v upx >/dev/null 2>&1; then
            upx --best --lzma -o "$PKG_DIR/${BASENAME}.upx" "$NAKED" || cp "$NAKED" "$PKG_DIR/${BASENAME}.upx"
          else
            cp "$NAKED" "$PKG_DIR/${BASENAME}.upx"
          fi

          tar -C "$PKG_DIR" -czf "$PKG_DIR/${BASENAME}.tar.gz" "$(basename "$NAKED")"

          PKG_ROOT="$PKG_DIR/pkg-root"

    rm -rf "$STAGE_DIR"
          (cd "$PKG_DIR" && fpm -s dir -t deb -n github-trend -v "$TAG" --prefix / -C "$PKG_ROOT" usr/local/bin/github-trend) || true
          (cd "$PKG_DIR" && fpm -s dir -t rpm -n github-trend -v "$TAG" --prefix / -C "$PKG_ROOT" usr/local/bin/github-trend) || true
        else
          echo "No executable found for linux packaging for $RID"
        fi
      fi
    fi
  fi

  echo "Packages for $RID available in $PKG_DIR"
done

  # Final cleanup: remove temporary folders and keep only packaged files
  find ./publish/packages -type d -name "pkg-root" -exec rm -rf {} + 2>/dev/null || true
  find ./publish/packages -maxdepth 3 -type f \( -name "*.pdb" -o -name "*.dll" -o -name "*.so" -o -name "*.dylib" -o -name "*.runtimeconfig.json" -o -name "*.deps.json" \) -delete 2>/dev/null || true

echo "\nAll done. Find packages under ./publish/packages/"
