#!/bin/bash
set -euo pipefail
archive="$1"
output="$2"
TAG="$3"
work="$(mktemp -d)"
trap 'rm -rf -- "$work"' EXIT
cd "$work"
mkdir payload
ditto -x -k "$archive" payload
app="payload/水流时钟.app"
test "$(defaults read "$PWD/$app/Contents/Info" CFBundleShortVersionString)" = "${TAG#v}"
codesign --verify --deep --strict --all-architectures "$app"
ln -s /Applications payload/Applications
hdiutil create -volname "水流时钟" -fs HFS+ -format UDZO -srcfolder payload WaterflowClock-macOS.dmg
hdiutil verify WaterflowClock-macOS.dmg
mkdir mount
hdiutil attach -readonly -nobrowse -mountpoint "$PWD/mount" WaterflowClock-macOS.dmg
trap 'hdiutil detach "$PWD/mount" && rm -rf -- "$work"' EXIT
test -L mount/Applications
test "$(readlink mount/Applications)" = /Applications
test ! -e "/Applications/水流时钟.app"
sudo ditto "mount/水流时钟.app" "mount/Applications/水流时钟.app"
installed="/Applications/水流时钟.app"
codesign --verify --deep --strict --all-architectures "$installed"
exe="$installed/Contents/MacOS/WaterClock"
cmp "$exe" "payload/水流时钟.app/Contents/MacOS/WaterClock"
lipo "$exe" -verify_arch arm64 x86_64
"$exe" &
pid=$!
sleep 4
kill -0 "$pid"
kill "$pid"
mv WaterflowClock-macOS.dmg "$output"
