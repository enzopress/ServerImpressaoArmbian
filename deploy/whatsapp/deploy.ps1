<#
.SYNOPSIS
    Compila o wa-print (Go) e instala no servidor de impressao como servico systemd.
.DESCRIPTION
    Na primeira vez gera a chave da API no proprio servidor (openssl) e grava em
    /etc/printserver.env (PrintServer__ApiKey) e /etc/wa-print.env (PRINT_API_KEY):
    a chave nao passa pelo PC nem pelo git. Reinicia o printserver para ler a chave.
.EXAMPLE
    .\deploy\whatsapp\deploy.ps1 -Target Dell
    .\deploy\whatsapp\deploy.ps1 -Target MX9 -ChatName "Impressora"
#>
param(
    [ValidateSet("Dell", "MX9")][string]$Target = "Dell",
    [string]$HostName,
    [string]$KeyFile,
    [string]$ChatName = "Impressora"
)

# Dell Inspiron 23 5348: x86_64. MX9: ARMv7.
$targets = @{
    Dell = @{ Host = "192.168.15.16"; Key = "$env:USERPROFILE\.ssh\id_ed25519_dell"; Arch = "amd64"; Arm = "" }
    MX9  = @{ Host = "192.168.15.2";  Key = "$env:USERPROFILE\.ssh\id_ed25519_mx9";  Arch = "arm";   Arm = "7" }
}
$cfg = $targets[$Target]
if (-not $HostName) { $HostName = $cfg.Host }
if (-not $KeyFile) { $KeyFile = $cfg.Key }

$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$out = Join-Path $root "out"
$binary = Join-Path $out "wa-print"
$ssh = @("-i", $KeyFile, "-o", "BatchMode=yes", "-o", "ConnectTimeout=10")

$go = Get-Command go -ErrorAction SilentlyContinue
if (-not $go -and (Test-Path "C:\Program Files\Go\bin\go.exe")) { $env:Path = "C:\Program Files\Go\bin;$env:Path" }
elseif (-not $go) { throw "Go nao encontrado. Instale com: winget install --id GoLang.Go" }

Write-Host "==> Compilando (linux/$($cfg.Arch))..."
New-Item -ItemType Directory -Force $out | Out-Null
Push-Location (Join-Path $root "whatsapp")
try {
    $env:GOOS = "linux"; $env:GOARCH = $cfg.Arch; $env:CGO_ENABLED = "0"
    if ($cfg.Arm) { $env:GOARM = $cfg.Arm }
    & go build -trimpath -ldflags "-s -w" -o $binary .
    if ($LASTEXITCODE -ne 0) { throw "go build falhou." }
} finally {
    Remove-Item Env:GOOS, Env:GOARCH, Env:GOARM, Env:CGO_ENABLED -ErrorAction SilentlyContinue
    Pop-Location
}

Write-Host "==> Enviando para $HostName..."
& scp @ssh -q $binary (Join-Path $PSScriptRoot "wa-print.service") "root@${HostName}:/tmp/"
if ($LASTEXITCODE -ne 0) { throw "scp falhou." }

$remote = @'
set -e
id waprint >/dev/null 2>&1 || useradd --system --no-create-home --home-dir /var/lib/wa-print --shell /usr/sbin/nologin waprint

# Chave da API: criada uma vez, aqui mesmo no servidor.
if ! grep -q '^PRINT_API_KEY=.\+' /etc/wa-print.env 2>/dev/null; then
  KEY=$(openssl rand -hex 32)
  printf 'WA_CHAT_NAME=%s\nPRINT_API_KEY=%s\n' "__CHAT__" "$KEY" > /etc/wa-print.env
  touch /etc/printserver.env
  sed -i '/^PrintServer__ApiKey=/d' /etc/printserver.env
  echo "PrintServer__ApiKey=$KEY" >> /etc/printserver.env
  RESTART_PRINTSERVER=1
fi
sed -i 's/^WA_CHAT_NAME=.*/WA_CHAT_NAME=__CHAT__/' /etc/wa-print.env
chmod 600 /etc/wa-print.env /etc/printserver.env

# O printserver.service precisa carregar /etc/printserver.env.
if ! grep -q 'EnvironmentFile=-/etc/printserver.env' /etc/systemd/system/printserver.service; then
  sed -i '/^\[Service\]/a EnvironmentFile=-/etc/printserver.env' /etc/systemd/system/printserver.service
  RESTART_PRINTSERVER=1
fi

install -d /opt/wa-print
install -m 0755 /tmp/wa-print /opt/wa-print/wa-print.new
mv /opt/wa-print/wa-print.new /opt/wa-print/wa-print
install -m 0644 /tmp/wa-print.service /etc/systemd/system/wa-print.service
rm -f /tmp/wa-print /tmp/wa-print.service
systemctl daemon-reload
[ -n "${RESTART_PRINTSERVER:-}" ] && systemctl restart printserver
systemctl enable wa-print >/dev/null 2>&1
systemctl restart wa-print
sleep 4
systemctl is-active wa-print
wget -qO- http://127.0.0.1:8085/status; echo
'@
$remote = $remote.Replace("__CHAT__", $ChatName)
$remote | & ssh @ssh "root@$HostName" "sed '1s/^\xEF\xBB\xBF//' | tr -d '\r' | bash -s"
if ($LASTEXITCODE -ne 0) { throw "Instalacao remota falhou." }

Write-Host "==> Pronto. Vincule em http://impressora.casa/WhatsApp (ou http://${HostName}/WhatsApp)"
