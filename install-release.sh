#!/bin/sh
# Runs install-release.ps1 from Git Bash, passing the arguments through, e.g.
#   ./install-release.sh -CopyDebugConfig -Start
# The PowerShell script elevates itself through UAC when needed.
script="$(cygpath -w "$(dirname "$0")/install-release.ps1")"
exec powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$script" "$@"
