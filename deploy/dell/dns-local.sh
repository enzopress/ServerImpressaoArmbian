#!/bin/bash
# O proprio Dell passa a resolver nomes pelo AdGuard local (127.0.0.1), com 1.1.1.1 de reserva.
# Motivo: o DNS do roteador da Vivo (192.168.15.1) devolve respostas que o resolvedor do Go
# rejeita ("no such host"), e o wa-print (Go) nao conseguia conectar em web.whatsapp.com.
# O dhcpcd deixa de reescrever o /etc/resolv.conf.
set -euo pipefail

CONF=/etc/dhcpcd.conf
if [ -f "$CONF" ] && ! grep -q '^nohook resolv.conf' "$CONF"; then
  printf '\n# resolv.conf fixo (AdGuard local): ver deploy/dell/dns-local.sh\nnohook resolv.conf\n' >> "$CONF"
fi

cat > /etc/resolv.conf <<'EOF'
# Fixo (deploy/dell/dns-local.sh): AdGuard local, Cloudflare se o AdGuard cair.
nameserver 127.0.0.1
nameserver 1.1.1.1
options timeout:2
EOF

# O dhcpcd (chamado pelo ifupdown) le o nohook na proxima renovacao; nao precisa reiniciar.
cat /etc/resolv.conf
getent ahostsv4 web.whatsapp.com | head -1
