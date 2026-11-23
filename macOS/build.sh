#!/bin/bash
set -euo pipefail

source_dir="$(cd "$(dirname "$0")" && pwd)"
output_dir="${1:-$source_dir/../发布}"
mkdir -p "$output_dir"
output_dir="$(cd "$output_dir" && pwd)"
work="$(mktemp -d "${TMPDIR:-/tmp}/waterclock-build.XXXXXXXX")"
trap 'rm -rf -- "$work"' EXIT
app="$work/水流时钟.app"
executable="$app/Contents/MacOS/WaterClock"
mkdir -p "$app/Contents/MacOS"
cp "$source_dir/Info.plist" "$app/Contents/Info.plist"
/usr/bin/plutil -lint "$app/Contents/Info.plist"

xcrun --sdk macosx clang -arch arm64 -arch x86_64 -mmacosx-version-min=13.0 \
    -O2 -fobjc-arc -fblocks -Wall -Werror=implicit-function-declaration \
    "$source_dir"/*.m -o "$executable" \
    -framework Cocoa -framework QuartzCore -framework CoreText \
    -framework CoreGraphics -framework ScreenCaptureKit -framework ServiceManagement
xcrun lipo -verify_arch arm64 x86_64 "$executable"
/usr/bin/codesign --force --sign - --timestamp=none "$app"
/usr/bin/codesign --verify --strict --all-architectures "$app"

archive="水流时钟-macOS.zip"
/usr/bin/ditto -c -k --keepParent "$app" "$work/$archive"
/usr/bin/unzip -tq "$work/$archive"
mv -f "$work/$archive" "$output_dir/$archive"
printf '%s\n' "$output_dir/$archive"
