<#
.SYNOPSIS
    Envia a pasta de um script de setup para o servidor e o executa como root.
.EXAMPLE
    .\deploy\run-remote.ps1 deploy\base\setup-base.sh
    .\deploy\run-remote.ps1 deploy\adguard\setup-adguard.sh -HostName 192.168.15.16
#>
param(
    [Parameter(Mandatory)][string]$Script,
    [string]$HostName = "192.168.15.16",
    [string]$KeyFile = "$env:USERPROFILE\.ssh\id_ed25519_dell"
)

$ErrorActionPreference = "Stop"
$ssh = @("-i", $KeyFile, "-o", "BatchMode=yes", "-o", "ConnectTimeout=10")
$scriptPath = Resolve-Path $Script
$dir = Split-Path $scriptPath -Parent
$name = Split-Path $scriptPath -Leaf
$remoteDir = "/tmp/deploy-" + (Split-Path $dir -Leaf)

& ssh @ssh "root@$HostName" "rm -rf $remoteDir && mkdir -p $remoteDir"
if ($LASTEXITCODE -ne 0) { throw "ssh falhou." }
& scp @ssh -q (Get-ChildItem $dir -File).FullName "root@${HostName}:$remoteDir/"
if ($LASTEXITCODE -ne 0) { throw "scp falhou." }

# Arquivos editados no Windows podem vir com CRLF/BOM: limpa antes de rodar.
& ssh @ssh "root@$HostName" "cd $remoteDir && sed -i -e '1s/^\xEF\xBB\xBF//' -e 's/\r$//' * && bash ./$name; rc=`$?; rm -rf $remoteDir; exit `$rc"
if ($LASTEXITCODE -ne 0) { throw "$name falhou no servidor." }
