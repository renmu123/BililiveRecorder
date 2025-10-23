#!/bin/bash

set -e

umask "$UMASK"

if [ "$(id -u)" != "0" ]; then
    echo "Skipped changing user and group because current user is not root."
    exec dotnet /app/BililiveRecorder.Cli.dll "$@"
fi

PUID=${PUID:-0}
PGID=${PGID:-0}

if [ "${PUID}" != "0" ] && [ "${PGID}" != "0" ]; then
    chown -R "${PUID}":"${PGID}" /rec
    exec /usr/local/bin/gosu "${PUID}":"${PGID}" dotnet /app/BililiveRecorder.Cli.dll "$@"
else
    exec dotnet /app/BililiveRecorder.Cli.dll "$@"
fi
