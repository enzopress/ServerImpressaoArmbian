<#
.SYNOPSIS
    Copia a configuracao do servidor para o PC (backup manual).
.DESCRIPTION
    Gera um .tgz no servidor com CUPS, servicos, firewall, watchdog, atualizacoes, rede e AdGuard,
    e salva em $Destination com data e hora. Mantem os $Keep mais recentes.
    O estado do Tailscale nao entra: numa restauracao, faca login de novo.
.EXAMPLE
    .\deploy\backup.ps1
#>
param(
    [string]$HostName = "192.168.15.16",
    [string]$KeyFile = "$env:USERPROFILE\.ssh\id_ed25519_dell",
    [string]$Destination = "$env:USERPROFILE\Backups\Servidor",
    [int]$Keep = 10
)

$ErrorActionPreference = "Stop"
$ssh = @("-i", $KeyFile, "-o", "BatchMode=yes", "-o", "ConnectTimeout=10")
$stamp = Get-Date -Format "yyyyMMdd-HHmm"
$file = Join-Path $Destination "servidor-$stamp.tgz"
New-Item -ItemType Directory -Force $Destination | Out-Null

# Caminhos que nao existirem (ex.: AdGuard antes da instalacao) sao ignorados.
$paths = @(
    "/etc/cups/printers.conf", "/etc/cups/ppd", "/etc/cups/cupsd.conf",
    "/etc/systemd/system/printserver.service",
    "/etc/nftables.conf", "/etc/systemd/system.conf.d", "/etc/sysctl.d/90-mx9.conf",
    "/etc/apt/apt.conf.d/52unattended-mx9", "/etc/systemd/system/apt-daily-upgrade.timer.d",
    "/etc/network/interfaces", "/etc/dhcpcd.conf", "/etc/resolv.conf",
    "/root/.ssh/authorized_keys", "/opt/AdGuardHome/AdGuardHome.yaml"
) -join " "

Write-Host "==> Gerando backup no servidor..."
$remote = "cd / && for p in $paths; do [ -e `"`$p`" ] && echo `"`${p#/}`"; done | tar -czf - -T -"
# Gera no servidor e baixa com scp: redirecionar o stdout no PowerShell 5 corromperia o binario.
& ssh @ssh "root@$HostName" "$remote > /tmp/servidor-backup.tgz"
if ($LASTEXITCODE -ne 0) { throw "Falha ao gerar o backup." }
& scp @ssh -q "root@${HostName}:/tmp/servidor-backup.tgz" $file
if ($LASTEXITCODE -ne 0) { throw "Falha ao copiar o backup." }
& ssh @ssh "root@$HostName" "rm -f /tmp/servidor-backup.tgz"

$count = (& tar.exe -tzf $file | Measure-Object).Count
"==> {0} ({1:N0} KB, {2} itens)" -f $file, ((Get-Item $file).Length / 1KB), $count | Write-Host

Get-ChildItem $Destination -Filter "servidor-*.tgz" | Sort-Object Name -Descending |
    Select-Object -Skip $Keep | Remove-Item

$reboot = & ssh @ssh "root@$HostName" "[ -f /var/run/reboot-required ] && cat /var/run/reboot-required.pkgs 2>/dev/null | tr '\n' ' '"
if ($reboot) { Write-Warning "O servidor precisa reiniciar (atualizacao: $reboot)." }
