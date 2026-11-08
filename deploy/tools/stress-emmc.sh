#!/bin/bash
# Teste de estresse do eMMC/CPU do MX9 (detecta leitura/gravacao corrompida).
#   nohup bash stress-emmc.sh [rodadas] > /root/stress.log 2>&1 &
# Resultado no fim do log: "RESULTADO: OK" ou "RESULTADO: FALHOU".
set -u
ROUNDS="${1:-3}"
WORK=/root/stress-tmp
FAIL=0
mmc_resets() { dmesg | grep -c 'mmc_host mmc2: Bus speed'; }
temp() { echo "$(( $(cat /sys/class/thermal/thermal_zone0/temp) / 1000 ))C"; }
drop() { sync; echo 3 > /proc/sys/vm/drop_caches; }
log() { echo "$(date +%T) [temp $(temp) | resets mmc2 $(mmc_resets)] $*"; }

BASE_RESETS=$(mmc_resets)
mkdir -p "$WORK"
log "inicio: $ROUNDS rodadas"

log "1) pacotes integros (dpkg -V)"
# Arquivos que o proprio Armbian personaliza (nao sao corrupcao).
ARMBIAN='/usr/bin/tzselect|/etc/armbian-release|/usr/share/doc/base-files/changelog.gz|/etc/os-release|/etc/issue.net|/etc/issue$'
BAD=$(dpkg -V 2>/dev/null | grep -v ' c /' | grep -v '^missing' | grep -vE "$ARMBIAN" | wc -l)
log "   arquivos de pacote alterados: $BAD"
[ "$BAD" -gt 0 ] && { dpkg -V 2>/dev/null | grep -v ' c /' | grep -v '^missing' | grep -vE "$ARMBIAN" | head; FAIL=1; }

log "2) leitura repetida de /usr (deve dar sempre o mesmo hash)"
drop; REF=$(find /usr -xdev -type f -print0 | sort -z | xargs -0 cat 2>/dev/null | sha256sum)
for i in $(seq 1 "$ROUNDS"); do
  drop; H=$(find /usr -xdev -type f -print0 | sort -z | xargs -0 cat 2>/dev/null | sha256sum)
  if [ "$H" = "$REF" ]; then log "   leitura $i: igual"; else log "   leitura $i: DIFERENTE"; FAIL=1; fi
done

log "3) gravacao e releitura de 512 MB (com CPU em carga nos 4 nucleos)"
for c in 1 2 3; do ( timeout 600 sh -c 'while :; do head -c 50M /dev/urandom | sha256sum >/dev/null; done' & ); done
for i in $(seq 1 "$ROUNDS"); do
  head -c 512M /dev/urandom > "$WORK/data.bin"
  W=$(sha256sum < "$WORK/data.bin"); drop
  R=$(sha256sum < "$WORK/data.bin")
  if [ "$W" = "$R" ]; then log "   gravacao $i: ok"; else log "   gravacao $i: CORROMPIDA"; FAIL=1; fi
done
pkill -f 'head -c 50M /dev/urandom' 2>/dev/null; pkill -f 'while :; do head' 2>/dev/null
rm -rf "$WORK"

NEW=$(( $(mmc_resets) - BASE_RESETS ))
log "reinicios do eMMC durante o teste: $NEW"
[ "$NEW" -gt 4 ] && FAIL=1
dmesg | grep -iE 'mmc2.*(error|timeout|crc)|blk_update|I/O error|EXT4-fs error' | tail -5

if [ "$FAIL" -eq 0 ]; then log "RESULTADO: OK"; else log "RESULTADO: FALHOU"; fi
