#!/usr/bin/env bash
# setup-local.sh — UnityAgent ローカル開発セットアップ
#
# OSS リポ (lighfu/unity-agent) を Unity の embedded package として
# Packages/com.ajisaiflow.unityagent + Packages/com.ajisaiflow.unityagent.sdk にリンクする。
#
# Usage:
#   ./setup-local.sh                          # デフォルトパス
#   ./setup-local.sh /path/to/unity-agent     # カスタムパス
#   ./setup-local.sh --remove                 # リンク削除

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
PACKAGES_DIR="$PROJECT_ROOT/Packages"
EDITOR_LINK="com.ajisaiflow.unityagent"
SDK_LINK="com.ajisaiflow.unityagent.sdk"

# OS 検出
is_windows() {
    case "$OSTYPE" in
        msys|msys2|cygwin|win32) return 0 ;;
    esac
    case "$(uname -s 2>/dev/null)" in
        MINGW*|MSYS*|CYGWIN*) return 0 ;;
    esac
    return 1
}

remove_link() {
    local link_path="$1"
    if [[ -e "$link_path" || -L "$link_path" ]]; then
        if is_windows; then
            powershell -Command "Remove-Item -Path '$(cygpath -w "$link_path")' -Force -Recurse"
        else
            rm -rf "$link_path"
        fi
        echo "Removed: $link_path"
    fi
}

create_link() {
    local source="$1"
    local link_path="$2"
    # 既存リンク削除
    remove_link "$link_path"
    # リンク作成
    if is_windows; then
        local win_source="$(cygpath -w "$source")"
        local win_link="$(cygpath -w "$link_path")"
        powershell -Command "New-Item -ItemType Junction -Path '$win_link' -Target '$win_source'" > /dev/null
    else
        ln -s "$source" "$link_path"
    fi
    echo "Linked: $link_path -> $source"
}

# デフォルトパス
if is_windows; then
    DEFAULT_SOURCE="/c/code/unity/unity-agent"
else
    DEFAULT_SOURCE="$HOME/code/unity/unity-agent"
fi

# --remove オプション
if [[ "${1:-}" == "--remove" ]]; then
    remove_link "$PACKAGES_DIR/$EDITOR_LINK"
    remove_link "$PACKAGES_DIR/$SDK_LINK"
    echo ""
    echo "Done. Restart Unity to pick up changes."
    exit 0
fi

SOURCE="${1:-$DEFAULT_SOURCE}"

# バリデーション
if [[ ! -f "$SOURCE/package.json" ]]; then
    echo "ERROR: $SOURCE/package.json not found"
    echo ""
    echo "Clone the repo first:"
    if is_windows; then
        echo "  git clone https://github.com/lighfu/unity-agent.git C:\\code\\unity\\unity-agent"
    else
        echo "  git clone https://github.com/lighfu/unity-agent.git ~/code/unity/unity-agent"
    fi
    exit 1
fi

# Editor パッケージリンク (ルート → Packages/com.ajisaiflow.unityagent)
create_link "$SOURCE" "$PACKAGES_DIR/$EDITOR_LINK"

# SDK パッケージリンク (SDK/ → Packages/com.ajisaiflow.unityagent.sdk)
create_link "$SOURCE/SDK" "$PACKAGES_DIR/$SDK_LINK"

echo ""
echo "Done. Restart Unity to pick up the embedded packages."
