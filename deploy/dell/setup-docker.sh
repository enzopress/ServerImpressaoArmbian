#!/bin/bash
# Docker Engine (repositorio oficial da Docker) no Dell, Debian 13.
#   scp deploy/dell/setup-docker.sh root@192.168.15.16:/tmp/ && ssh root@192.168.15.16 bash /tmp/setup-docker.sh
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive

echo "==> Removendo pacotes conflitantes (se houver)"
for p in docker.io docker-doc docker-compose podman-docker containerd runc; do
  dpkg -s "$p" >/dev/null 2>&1 && apt-get remove -y -q "$p" || true
done

echo "==> Repositorio oficial"
apt-get update -q
apt-get install -y -q --no-install-recommends ca-certificates curl
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc
chmod a+r /etc/apt/keyrings/docker.asc
. /etc/os-release
cat > /etc/apt/sources.list.d/docker.sources <<EOF
Types: deb
URIs: https://download.docker.com/linux/debian
Suites: $VERSION_CODENAME
Components: stable
Architectures: $(dpkg --print-architecture)
Signed-By: /etc/apt/keyrings/docker.asc
EOF

echo "==> Docker Engine + Compose"
apt-get update -q
apt-get install -y -q docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

# Logs dos containers com limite de tamanho (o padrao cresce sem limite).
install -d /etc/docker
[ -f /etc/docker/daemon.json ] || cat > /etc/docker/daemon.json <<'EOF'
{
  "log-driver": "json-file",
  "log-opts": { "max-size": "10m", "max-file": "3" }
}
EOF

systemctl enable --now docker containerd
systemctl restart docker
docker --version
docker compose version
