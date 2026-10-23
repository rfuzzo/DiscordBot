#!/usr/bin/env bash
# Builds the bot for Linux and deploys it to a Debian server as a systemd service.
#
#   deploy/deploy.sh root@your-server
#
# The server needs /etc/discordbot/discordbot.env with Discord__Token=... and Nexus__ApiKey=...
# (see the README). Everything else is set up on the first run.
set -euo pipefail

HOST="${1:-${DEPLOY_HOST:-}}"
if [[ -z "$HOST" ]]; then
    echo "Usage: deploy/deploy.sh user@host   (or set DEPLOY_HOST)" >&2
    exit 1
fi

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT

echo "==> Building"
dotnet publish "$ROOT/src/DiscordBot" -c Release -r linux-x64 --self-contained -o "$OUT/app" -p:DebugType=none --nologo -v quiet
mkdir -p "$OUT/units"
for unit in discordbot.service discordbot-backup.service discordbot-backup.timer; do
    sed 's/\r$//' "$ROOT/deploy/$unit" > "$OUT/units/$unit"
done

echo "==> Uploading to $HOST"
tar -C "$OUT" -czf - app units | ssh "$HOST" 'set -e
    rm -rf /tmp/discordbot-deploy && mkdir -p /tmp/discordbot-deploy
    tar -xzf - --no-same-owner -C /tmp/discordbot-deploy'

echo "==> Installing"
ssh "$HOST" 'set -e
    if [ ! -f /etc/discordbot/discordbot.env ]; then
        echo "Missing /etc/discordbot/discordbot.env (Discord__Token=..., Nexus__ApiKey=...)" >&2
        exit 1
    fi
    command -v sqlite3 >/dev/null || apt-get install -y sqlite3
    id discordbot >/dev/null 2>&1 || useradd --system --no-create-home --shell /usr/sbin/nologin discordbot

    cd /tmp/discordbot-deploy
    chmod +x app/DiscordBot
    install -m 644 units/* /etc/systemd/system/
    systemctl daemon-reload

    # Swap in the new build; the previous one stays in app.old for a quick rollback.
    mkdir -p /opt/discordbot
    systemctl stop discordbot 2>/dev/null || true
    rm -rf /opt/discordbot/app.old
    [ -d /opt/discordbot/app ] && mv /opt/discordbot/app /opt/discordbot/app.old
    mv app /opt/discordbot/app
    chown -R root:root /opt/discordbot
    rm -rf /tmp/discordbot-deploy

    systemctl enable --now discordbot.service discordbot-backup.timer
    sleep 15
    systemctl --no-pager --lines=0 status discordbot
    journalctl -u discordbot -n 15 --no-pager -o cat'
