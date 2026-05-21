#!/bin/bash
# ClipStudio Linux launcher.
#
# LibVLCSharp requires the unversioned libvlc.so symlink, which is only
# present when the libvlc-dev package is installed. This script detects
# the versioned system library (libvlc.so.5, from the libvlc5 runtime
# package) and creates a user-local unversioned symlink on the fly, so
# the app works without any developer packages.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

# Only act if the unversioned symlink is absent from ldconfig's cache.
if ! ldconfig -p 2>/dev/null | grep -q "libvlc\.so$"; then
    LIBVLC_VERSIONED=$(ldconfig -p 2>/dev/null \
        | grep "libvlc\.so\." \
        | grep -v core \
        | awk '{print $NF}' \
        | head -1)

    if [ -n "$LIBVLC_VERSIONED" ]; then
        # Use XDG_RUNTIME_DIR when available (cleared on logout); fall back to /tmp.
        VLC_LINK_DIR="${XDG_RUNTIME_DIR:-/tmp}/clipstudio-vlc"
        mkdir -p "$VLC_LINK_DIR"
        ln -sf "$LIBVLC_VERSIONED" "$VLC_LINK_DIR/libvlc.so"
        export LD_LIBRARY_PATH="$VLC_LINK_DIR${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
    else
        echo "Warning: libvlc5 not found. Install it with: sudo apt-get install libvlc5" >&2
    fi
fi

exec "$SCRIPT_DIR/ClipStudio.UI" "$@"
