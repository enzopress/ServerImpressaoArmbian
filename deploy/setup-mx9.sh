#!/bin/bash
# Prepara um MX9 (Armbian rk322x, Debian 13, armhf) para a HP Laser MFP 135a.
# Reproduz o que foi feito a mao na Fase 0. Rodar como root, com a impressora ligada no USB:
#   ssh root@192.168.15.2 'bash -s' < deploy/setup-mx9.sh
#
# A 135a nao aceita PWG Raster nem splix (imprime lixo) e o HPLIP nao a suporta.
# O unico driver que funciona e o HP ULD, que nao tem versao armhf: o filtro i386
# roda via qemu-user (binfmt), com as bibliotecas i386 do multiarch.
set -euo pipefail

ULD_URL="https://ftp.hp.com/pub/softlib/software13/printers/MFP170/uld-hp_V1.00.39.12_00.15.tar.gz"
ULD_SHA256="cebb9b7b6125e7406634bb9c2a98b01477d1e11d66c7c90474669de9927bc91d"
QUEUE="HP135a"
PPD="/usr/share/ppd/uld-hp/HP_Laser_MFP_13x_Series.ppd"
export DEBIAN_FRONTEND=noninteractive

echo "==> Pacotes"
apt-get update -q
apt-get install -y -q --no-install-recommends \
  cups cups-filters cups-bsd cups-client avahi-daemon avahi-utils ipp-usb \
  qemu-user qemu-user-binfmt wget ca-certificates

echo "==> Bibliotecas i386 para o filtro da HP"
dpkg --add-architecture i386
apt-get update -q
apt-get install -y -q --no-install-recommends \
  libc6:i386 libcups2t64:i386 libcupsimage2t64:i386 libstdc++6:i386 libgcc-s1:i386

echo "==> HP ULD"
mkdir -p /opt/hp-uld-src && cd /opt/hp-uld-src
[ -f uld.tar.gz ] || wget -q -O uld.tar.gz "$ULD_URL"
echo "$ULD_SHA256  uld.tar.gz" | sha256sum -c -
rm -rf uld && tar xzf uld.tar.gz
install -d /opt/smfp-common/printer/lib /usr/share/ppd/uld-hp
install -m 0755 uld/i386/rastertospl /usr/lib/cups/filter/rastertospl
install -m 0644 uld/i386/libscmssc.so /opt/smfp-common/printer/lib/libscmssc.so
install -m 0644 uld/noarch/share/ppd/HP_Laser_MFP_13x_Series.ppd "$PPD"

echo "==> Fila $QUEUE"
systemctl enable --now cups
URI=$(lpinfo -v | awk '/usb:\/\/HP\/Laser%20MFP%20131/ {print $2; exit}')
if [ -z "$URI" ]; then
  echo "Impressora nao encontrada no USB. Ligue a HP 135a e rode de novo." >&2
  exit 1
fi
lpadmin -p "$QUEUE" -E -v "$URI" -P "$PPD" -D "HP Laser MFP 135a" -L "MX9" \
  -o printer-is-shared=true -o PageSize=A4 -o printer-error-policy=abort-job
lpadmin -d "$QUEUE"
cupsctl --remote-admin --remote-any --share-printers

echo "==> Usuario do servico web"
id printserver >/dev/null 2>&1 || \
  useradd --system --no-create-home --home-dir /opt/printserver --shell /usr/sbin/nologin printserver
install -d -m 0755 /opt/printserver

echo "==> Pronto. Teste: lp -d $QUEUE /usr/share/cups/data/testprint"
