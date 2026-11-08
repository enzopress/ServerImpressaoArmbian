# Autoriza a chave SSH do PC (id_ed25519_dell) no root do Dell (Debian, 192.168.15.16).
# O Debian nao aceita senha do root por SSH: entra com o seu usuario e vira root com "su".
# Pede duas senhas: a do seu usuario (SSH) e a do root (su).
param([string]$HostName = "192.168.15.16")

$user = Read-Host "Usuario que voce criou na instalacao do Debian"
$pub = (Get-Content "$env:USERPROFILE\.ssh\id_ed25519_dell.pub" -Raw).Trim()
$cmd = "mkdir -p /root/.ssh && chmod 700 /root/.ssh && echo '$pub' >> /root/.ssh/authorized_keys && chmod 600 /root/.ssh/authorized_keys && echo CHAVE_AUTORIZADA"
if ($user -eq "root") {
    # Exige antes, no console do Dell: PermitRootLogin yes (temporario, removido depois do deploy).
    Write-Host "Senha do root:"
    ssh -o StrictHostKeyChecking=accept-new -o PubkeyAuthentication=no "root@$HostName" $cmd
} else {
    Write-Host "1) senha do usuario $user   2) senha do root"
    # Em base64 para o comando chegar inteiro ao su (o Windows descarta aspas duplas aninhadas).
    $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($cmd))
    ssh -t -o StrictHostKeyChecking=accept-new -o PubkeyAuthentication=no "$user@$HostName" "su - root -c 'echo $b64 | base64 -d | sh'"
}
