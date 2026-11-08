<#
.SYNOPSIS
    Publica o PrintServer.Web e instala como servico systemd (MX9 por padrao, ou o Dell).
.EXAMPLE
    .\deploy\deploy.ps1
    .\deploy\deploy.ps1 -HostName 192.168.15.2 -ReadyToRun
    .\deploy\deploy.ps1 -Target Dell
#>
param(
    [ValidateSet("MX9", "Dell")][string]$Target = "MX9",
    [string]$HostName,
    [string]$KeyFile,
    [switch]$ReadyToRun
)

# MX9: ARMv7 (linux-arm). Dell Inspiron 23 5348: x86_64 (linux-x64).
$targets = @{
    MX9  = @{ Host = "192.168.15.2";  Key = "$env:USERPROFILE\.ssh\id_ed25519_mx9";  Runtime = "linux-arm" }
    Dell = @{ Host = "192.168.15.16"; Key = "$env:USERPROFILE\.ssh\id_ed25519_dell"; Runtime = "linux-x64" }
}
$cfg = $targets[$Target]
if (-not $HostName) { $HostName = $cfg.Host }
if (-not $KeyFile) { $KeyFile = $cfg.Key }
$runtime = $cfg.Runtime

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "out"
$publish = Join-Path $out "publish"
$archive = Join-Path $out "printserver.tgz"
$ssh = @("-i", $KeyFile, "-o", "BatchMode=yes", "-o", "ConnectTimeout=10")

Write-Host "==> Publicando ($runtime, self-contained)..."
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
$publishArgs = @(
    "publish", (Join-Path $root "src/PrintServer.Web"),
    "-c", "Release", "-r", $runtime, "--self-contained", "true",
    "-o", $publish, "-nologo", "-v", "q"
)
if ($ReadyToRun) { $publishArgs += "-p:PublishReadyToRun=true" }
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou." }

Write-Host "==> Compactando..."
if (Test-Path $archive) { Remove-Item $archive }
& tar.exe -czf $archive -C $publish .
if ($LASTEXITCODE -ne 0) { throw "tar falhou." }
"{0:N1} MB" -f ((Get-Item $archive).Length / 1MB) | Write-Host

Write-Host "==> Enviando para $HostName..."
& scp @ssh $archive (Join-Path $PSScriptRoot "printserver.service") "root@${HostName}:/tmp/"
if ($LASTEXITCODE -ne 0) { throw "scp falhou." }

# Instala numa pasta nova e troca de uma vez, para nao deixar o servico com arquivos pela metade.
$remote = @'
set -e
install -m 0644 /tmp/printserver.service /etc/systemd/system/printserver.service
rm -rf /opt/printserver.new && mkdir -p /opt/printserver.new
tar -xzf /tmp/printserver.tgz -C /opt/printserver.new
chmod +x /opt/printserver.new/PrintServer.Web
systemctl stop printserver 2>/dev/null || true
rm -rf /opt/printserver.old
[ -d /opt/printserver ] && mv /opt/printserver /opt/printserver.old
mv /opt/printserver.new /opt/printserver
rm -f /tmp/printserver.tgz /tmp/printserver.service
systemctl daemon-reload
systemctl enable --now printserver >/dev/null 2>&1
systemctl restart printserver
for i in $(seq 1 30); do
  sleep 2
  if out=$(wget -qO- http://127.0.0.1/health 2>/dev/null); then
    echo "health: $out (apos $((i*2)) s)"
    ps -o rss=,etime= -C PrintServer.Web | awk '{printf "memoria: %.0f MB, rodando ha %s\n", $1/1024, $2}'
    exit 0
  fi
done
echo "O servico nao respondeu em 60 s:"
systemctl status printserver --no-pager -n 20
exit 1
'@
$remote -replace "`r", "" | & ssh @ssh "root@$HostName" "sed '1s/^\xEF\xBB\xBF//' | bash -s"
if ($LASTEXITCODE -ne 0) { throw "Instalacao remota falhou." }

Write-Host "==> Pronto: http://$HostName/"
