#!/bin/bash
# AdGuard Home no servidor de impressao (Dell, amd64).
#   1) .\deploy\run-remote.ps1 deploy\adguard\setup-adguard.sh -HostName <ip> -KeyFile <chave>
#      instala e deixa o assistente em http://<ip>:3000 (login/senha: o usuario cria).
#   2) depois do assistente, aplica as configuracoes da casa:
#      ssh root@<ip> "python3 /opt/AdGuardHome/apply-settings.py"
#      (o setup copia o apply-settings.py e o adguard-settings.yaml para /opt/AdGuardHome)
set -euo pipefail
cd "$(dirname "$0")"

VERSION="v0.107.80"
if [ "$(dpkg --print-architecture)" != "amd64" ]; then
  echo "Este script e para amd64. Para ARMv7 use a branch arm." >&2
  exit 1
fi
PKG=linux_amd64
SHA256="28593bc761d71469abdbc6f39ca42cbed763ec92b63082c729177b037ed51d18"
URL="https://github.com/AdguardTeam/AdGuardHome/releases/download/$VERSION/AdGuardHome_$PKG.tar.gz"
IP=$(hostname -I | awk '{print $1}')
export DEBIAN_FRONTEND=noninteractive

echo "==> Pacotes"
apt-get install -y -q --no-install-recommends python3-yaml wget ca-certificates

# O proprio servidor continua usando o DNS do roteador: se o AdGuard cair, ele nao fica sem DNS.
if systemctl is-active -q systemd-resolved; then
  echo "==> Liberando a porta 53 (stub do systemd-resolved)"
  install -d /etc/systemd/resolved.conf.d
  cat > /etc/systemd/resolved.conf.d/20-adguard.conf <<'EOF'
[Resolve]
DNSStubListener=no
EOF
  ln -sf /run/systemd/resolve/resolv.conf /etc/resolv.conf
  systemctl restart systemd-resolved
fi

if [ ! -x /opt/AdGuardHome/AdGuardHome ]; then
  echo "==> AdGuard Home $VERSION"
  tmp=$(mktemp -d)
  wget -q -O "$tmp/agh.tar.gz" "$URL"
  echo "$SHA256  $tmp/agh.tar.gz" | sha256sum -c -
  tar -xzf "$tmp/agh.tar.gz" -C /opt
  rm -rf "$tmp"
  # O -s install grava a pasta atual como WorkingDirectory (onde ficam os dados).
  (cd /opt/AdGuardHome && ./AdGuardHome -s install)
else
  echo "==> AdGuard Home ja instalado"
fi

install -m 0644 adguard-settings.yaml /opt/AdGuardHome/adguard-settings.yaml
install -m 0755 apply-settings.py /opt/AdGuardHome/apply-settings.py

systemctl is-active AdGuardHome
if [ -f /opt/AdGuardHome/AdGuardHome.yaml ]; then
  echo "==> Configurado. Para reaplicar: python3 /opt/AdGuardHome/apply-settings.py"
else
  echo "==> Abra http://$IP:3000 e conclua o assistente (painel na porta 3000, DNS na 53)."
fi
