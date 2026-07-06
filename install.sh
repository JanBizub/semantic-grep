#!/bin/bash
set -e

REPO="JanBizub/semantic-grep"
INSTALL_DIR="/usr/local/bin"
BINARY="segrep"

OS=$(uname -s | tr '[:upper:]' '[:lower:]')
ARCH=$(uname -m)

case "$ARCH" in
  x86_64)          ARCH_LABEL="x64" ;;
  arm64|aarch64)   ARCH_LABEL="arm64" ;;
  *) echo "Unsupported architecture: $ARCH"; exit 1 ;;
esac

case "$OS" in
  darwin) PLATFORM="osx" ;;
  linux)  PLATFORM="linux" ;;
  *) echo "Unsupported OS: $OS"; exit 1 ;;
esac

ARTIFACT="${BINARY}-${PLATFORM}-${ARCH_LABEL}"
URL="https://github.com/${REPO}/releases/latest/download/${ARTIFACT}"

echo "Detected: ${PLATFORM}-${ARCH_LABEL}"
echo "Downloading ${ARTIFACT}..."

TMP=$(mktemp)
curl -fsSL "$URL" -o "$TMP"
chmod +x "$TMP"

if [ -w "$INSTALL_DIR" ]; then
  mv "$TMP" "${INSTALL_DIR}/${BINARY}"
else
  echo "Installing to ${INSTALL_DIR} (sudo required)..."
  sudo mv "$TMP" "${INSTALL_DIR}/${BINARY}"
fi

echo "Installed: $(which segrep)"
segrep --version 2>/dev/null || segrep --help 2>/dev/null | head -3 || true
