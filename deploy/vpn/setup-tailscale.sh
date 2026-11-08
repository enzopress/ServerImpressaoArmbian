#!/bin/bash
# VPN Tailscale no servidor como "subnet router" da rede de casa (192.168.15.0/24).
#   .\deploy\run-remote.ps1 deploy\vpn\setup-tailscale.sh
# Na primeira vez o "tailscale up" mostra um link de login: o usuario abre e autoriza.
# Depois, no painel do Tailscale: aprovar a rota 192.168.15.0/24 e, em DNS, usar o IP 100.x
# do servidor como nameserver ("Override local DNS") para ter o AdGuard fora de casa.
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
LAN="192.168.15.0/24"

if ! command -v tailscale >/dev/null; then
  echo "==> Repositorio oficial do Tailscale"
  . /etc/os-release
  curl -fsSL "https://pkgs.tailscale.com/stable/debian/$VERSION_CODENAME.noarmor.gpg" \
    -o /usr/share/keyrings/tailscale-archive-keyring.gpg
  curl -fsSL "https://pkgs.tailscale.com/stable/debian/$VERSION_CODENAME.tailscale-keyring.list" \
    -o /etc/apt/sources.list.d/tailscale.list
  apt-get update -q
  apt-get install -y -q tailscale
fi

echo "==> Encaminhamento de pacotes (VPN -> rede de casa)"
cat > /etc/sysctl.d/91-tailscale.conf <<'EOF'
net.ipv4.ip_forward = 1
EOF
sysctl -q --system

systemctl enable --now tailscaled >/dev/null

echo "==> Conectando"
# O firewall e o NAT ficam no /etc/nftables.conf (forward tailscale0 -> eno1 + masquerade):
# netfilter-mode=off impede o Tailscale de mexer nas regras.
tailscale up --advertise-routes="$LAN" --accept-dns=false --netfilter-mode=off --timeout=10s \
  || echo "Abra o link acima para autorizar o servidor e rode este script de novo."

tailscale status --peers=false 2>/dev/null || true
tailscale ip -4 2>/dev/null || true
