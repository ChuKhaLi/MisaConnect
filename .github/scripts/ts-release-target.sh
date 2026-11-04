#!/usr/bin/env bash
# Maps a release tag to its npm workspace. Usage: ts-release-target.sh <tag>
# Prints "<package> <directory> <tag-prefix>" or exits 1 for an unknown tag.
# Self-check: ts-release-target.sh --self-test
set -euo pipefail

map() {
  case "$1" in
    einvoice-ts-v*) echo "@misaconnect/einvoice src/typescript/einvoice einvoice-ts-v" ;;
    esign-ts-v*) echo "@misaconnect/esign src/typescript/esign esign-ts-v" ;;
    *) return 1 ;;
  esac
}

if [ "${1:-}" = "--self-test" ]; then
  [ "$(map einvoice-ts-v1.2.3)" = "@misaconnect/einvoice src/typescript/einvoice einvoice-ts-v" ]
  [ "$(map esign-ts-v0.1.0)" = "@misaconnect/esign src/typescript/esign esign-ts-v" ]
  ! map v1.0.0 >/dev/null 2>&1
  ! map main >/dev/null 2>&1
  echo "ts-release-target self-test OK"
  exit 0
fi

map "${1:-}" || { echo "Refusing: '${1:-}' is not an einvoice-ts-v* or esign-ts-v* tag." >&2; exit 1; }
