<#
.SYNOPSIS
    Envia a pasta de um script de setup para o MX9 e o executa como root.
.EXAMPLE
    .\deploy\run-remote.ps1 deploy\base\setup-base.sh
    .\deploy\run-remote.ps1 deploy\adguard\setup-adguard.sh -HostName 192.168.15.2
#>
param(
    [Parameter(Mandatory)][string]$Script,
    [string]$HostName = "192.168.15.2",
    [string]$KeyFile = "$env:USERPROFILE\.ssh\id_ed25519_mx9"
)

$ErrorActionPreference = "Stop"
$ssh = @("-i", $KeyFile, "-o", "BatchMode=yes", "-o", "ConnectTimeout=10")
$scriptPath = Resolve-Path $Script
$dir = Split-Path $scriptPath -Parent
$name = Split-Path $scriptPath -Leaf
$remoteDir = "/tmp/mx9-" + (Split-Path $dir -Leaf)

& ssh @ssh "root@$HostName" "rm -rf $remoteDir && mkdir -p $remoteDir"
if ($LASTEXITCODE -ne 0) { throw "ssh falhou." }
& scp @ssh -q (Get-ChildItem $dir -File).FullName "root@${HostName}:$remoteDir/"
if ($LASTEXITCODE -ne 0) { throw "scp falhou." }

# Arquivos editados no Windows podem vir com CRLF/BOM: limpa antes de rodar.
& ssh @ssh "root@$HostName" "cd $remoteDir && sed -i -e '1s/^\xEF\xBB\xBF//' -e 's/\r$//' * && bash ./$name; rc=`$?; rm -rf $remoteDir; exit `$rc"
if ($LASTEXITCODE -ne 0) { throw "$name falhou no MX9." }
