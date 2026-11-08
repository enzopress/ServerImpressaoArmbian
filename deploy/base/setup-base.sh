#!/bin/bash
# Robustez do MX9: watchdog de hardware, atualizações de segurança automáticas,
# nftables instalado (as regras são aplicadas à parte, com rollback) e LLMNR desligado.
# Rodar como root a partir da pasta com os arquivos de deploy/base:
#   .\deploy\run-remote.ps1 deploy\base\setup-base.sh
set -euo pipefail
cd "$(dirname "$0")"
export DEBIAN_FRONTEND=noninteractive

echo "==> Watchdog e kernel panic"
install -d /etc/systemd/system.conf.d
install -m 0644 watchdog.conf /etc/systemd/system.conf.d/watchdog.conf
install -m 0644 sysctl-mx9.conf /etc/sysctl.d/90-mx9.conf
sysctl -q --system
systemctl daemon-reexec

# Limitar a CPU (1,0 GHz) nao resolveu os erros do eMMC no teste de 01/11/2026: CPU no padrao.
# NAO usar o overlay emmc-pins neste MX9: com ele o eMMC reiniciou 80 vezes e corrompeu
# leituras no teste de estresse (01/11/2026); sem ele, 0 reinicios.
if grep -q 'emmc-pins' /boot/armbianEnv.txt; then
  echo "AVISO: overlay emmc-pins ativo em /boot/armbianEnv.txt; remova e religue o MX9." >&2
fi

echo "==> Pacotes"
apt-get update -q
apt-get install -y -q --no-install-recommends unattended-upgrades apt-listchanges nftables

echo "==> Atualizações de segurança automáticas (sem reboot)"
install -m 0644 52unattended-mx9 /etc/apt/apt.conf.d/52unattended-mx9
install -d /etc/systemd/system/apt-daily-upgrade.timer.d
cat > /etc/systemd/system/apt-daily-upgrade.timer.d/override.conf <<'EOF'
[Timer]
OnCalendar=
OnCalendar=*-*-* 05:00
RandomizedDelaySec=15m
EOF
systemctl daemon-reload
systemctl enable --now apt-daily.timer apt-daily-upgrade.timer >/dev/null

echo "==> LLMNR desligado (porta 5355)"
install -d /etc/systemd/resolved.conf.d
cat > /etc/systemd/resolved.conf.d/10-mx9.conf <<'EOF'
[Resolve]
LLMNR=no
EOF
systemctl restart systemd-resolved

echo "==> Regras do firewall copiadas (ainda não aplicadas)"
install -m 0644 nftables.conf /etc/nftables.conf.new
nft -c -f /etc/nftables.conf.new && echo "sintaxe ok"

echo "==> Estado"
systemctl show -p RuntimeWatchdogUSec -p RebootWatchdogUSec
systemctl list-timers apt-daily-upgrade.timer --no-pager --no-legend
