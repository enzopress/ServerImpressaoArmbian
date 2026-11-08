#!/bin/bash
# Prepara o Dell Inspiron 23 5348 (Debian 13, amd64) para a HP Laser MFP 135a.
# Versao x86 do setup-mx9.sh: aqui o HP ULD roda nativo (x86_64), sem qemu nem multiarch i386.
# Rodar como root, com a impressora ligada no USB do Dell:
#   .\deploy\run-remote.ps1 deploy\dell\setup-dell.sh -HostName 192.168.15.16 -KeyFile $env:USERPROFILE\.ssh\id_ed25519_dell
set -euo pipefail

ULD_URL="https://ftp.hp.com/pub/softlib/software13/printers/MFP170/uld-hp_V1.00.39.12_00.15.tar.gz"
ULD_SHA256="cebb9b7b6125e7406634bb9c2a98b01477d1e11d66c7c90474669de9927bc91d"
QUEUE="HP135a"
PPD="/usr/share/ppd/uld-hp/HP_Laser_MFP_13x_Series.ppd"
export DEBIAN_FRONTEND=noninteractive

if [ "$(dpkg --print-architecture)" != "amd64" ]; then
  echo "Este script e para amd64. No MX9 use deploy/setup-mx9.sh." >&2
  exit 1
fi

echo "==> Pacotes"
apt-get update -q
apt-get install -y -q --no-install-recommends \
  cups cups-filters cups-bsd cups-client avahi-daemon avahi-utils ipp-usb \
  wget ca-certificates \
  libcupsimage2t64  # o rastertospl da HP precisa da libcupsimage.so.2 (sem ela: "Unable to send raster data")

echo "==> HP ULD (x86_64 nativo)"
mkdir -p /opt/hp-uld-src && cd /opt/hp-uld-src
[ -f uld.tar.gz ] || wget -q -O uld.tar.gz "$ULD_URL"
echo "$ULD_SHA256  uld.tar.gz" | sha256sum -c -
rm -rf uld && tar xzf uld.tar.gz
if [ ! -f uld/x86_64/rastertospl ]; then
  echo "uld/x86_64/rastertospl nao encontrado no pacote da HP." >&2
  exit 1
fi
install -d /opt/smfp-common/printer/lib /usr/share/ppd/uld-hp
install -m 0755 uld/x86_64/rastertospl /usr/lib/cups/filter/rastertospl
install -m 0644 uld/x86_64/libscmssc.so /opt/smfp-common/printer/lib/libscmssc.so
install -m 0644 uld/noarch/share/ppd/HP_Laser_MFP_13x_Series.ppd "$PPD"

echo "==> Fila $QUEUE"
systemctl enable --now cups
URI=$(lpinfo -v | awk '/usb:\/\/HP\/Laser%20MFP%20131/ {print $2; exit}')
if [ -z "$URI" ]; then
  echo "Impressora nao encontrada no USB. Ligue a HP 135a no Dell e rode de novo." >&2
  exit 1
fi
lpadmin -p "$QUEUE" -E -v "$URI" -P "$PPD" -D "HP Laser MFP 135a" -L "Dell" \
  -o printer-is-shared=true -o PageSize=A4 -o printer-error-policy=abort-job
lpadmin -d "$QUEUE"
cupsctl --remote-admin --remote-any --share-printers

echo "==> Usuario do servico web"
id printserver >/dev/null 2>&1 || \
  useradd --system --no-create-home --home-dir /opt/printserver --shell /usr/sbin/nologin printserver
install -d -m 0755 /opt/printserver

echo "==> Pronto. Teste: lp -d $QUEUE /usr/share/cups/data/testprint"
