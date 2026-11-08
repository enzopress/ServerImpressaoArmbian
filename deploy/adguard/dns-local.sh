#!/bin/bash
# O proprio MX9 passa a resolver nomes pelo AdGuard local (127.0.0.1), com 1.1.1.1 de reserva.
# Motivo: o DNS do roteador da Vivo (192.168.15.1) devolve respostas que o resolvedor do Go
# rejeita ("no such host"), e o wa-print (Go) nao conseguiria conectar em web.whatsapp.com.
# Rodar depois de instalar o AdGuard e concluir o assistente:
#   .\deploy\run-remote.ps1 deploy\adguard\dns-local.sh
set -euo pipefail

if ! systemctl is-active -q AdGuardHome; then
  echo "O AdGuard Home nao esta ativo: rode deploy/adguard/setup-adguard.sh primeiro." >&2
  exit 1
fi

# No Armbian o /etc/resolv.conf costuma ser um link para o arquivo do systemd-resolved, que traz
# o DNS do roteador recebido por DHCP. Um arquivo comum no lugar do link nao e reescrito por ele.
rm -f /etc/resolv.conf
cat > /etc/resolv.conf <<'EOF'
# Fixo (deploy/adguard/dns-local.sh): AdGuard local, Cloudflare se o AdGuard cair.
nameserver 127.0.0.1
nameserver 1.1.1.1
options timeout:2
EOF

cat /etc/resolv.conf
getent ahostsv4 web.whatsapp.com | head -1
