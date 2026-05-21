#!/bin/bash
# ClipStudio macOS launcher.
#
# LaunchServices strips DYLD_* environment variables before spawning an app
# bundle process, so LibVLCSharp cannot find VLC's dylibs or plugins at
# runtime even if Core.Initialize points at the correct path.
# Running this script as CFBundleExecutable sets the required variables
# before exec-ing the real binary, preserving them across the exec call.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

VLC_BASE="/Applications/VLC.app/Contents/MacOS"
if [ -d "$VLC_BASE/lib" ]; then
    export DYLD_LIBRARY_PATH="$VLC_BASE/lib${DYLD_LIBRARY_PATH:+:$DYLD_LIBRARY_PATH}"
    export VLC_PLUGIN_PATH="$VLC_BASE/plugins"
else
    echo "Warning: VLC.app not found at /Applications/VLC.app. Install VLC from https://www.videolan.org/" >&2
fi

exec "$SCRIPT_DIR/ClipStudio.UI" "$@"
