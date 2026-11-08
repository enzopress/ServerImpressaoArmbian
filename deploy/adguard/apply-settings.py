#!/usr/bin/env python3
"""Aplica o adguard-settings.yaml sobre o AdGuardHome.yaml (mantendo users e o resto).

Para o AdGuard, grava o YAML (com copia .bak) e inicia de novo.
"""
import shutil
import subprocess
import sys
from pathlib import Path

import yaml

BASE = Path("/opt/AdGuardHome")
CONFIG = BASE / "AdGuardHome.yaml"
SETTINGS = BASE / "adguard-settings.yaml"

if not CONFIG.exists():
    sys.exit("AdGuardHome.yaml nao existe: conclua o assistente em http://<ip>:3000 primeiro.")

s = yaml.safe_load(SETTINGS.read_text())
cfg = yaml.safe_load(CONFIG.read_text())

cfg.setdefault("http", {})["address"] = s["http_address"]

dns = cfg.setdefault("dns", {})
dns["upstream_dns"] = s["upstream_dns"]
dns["bootstrap_dns"] = s["bootstrap_dns"]
dns["upstream_mode"] = s["upstream_mode"]
dns["cache_enabled"] = s["cache_enabled"]
dns["cache_size"] = s["cache_size"]

cfg.setdefault("querylog", {})["interval"] = s["querylog_interval"]
cfg.setdefault("statistics", {})["interval"] = s["statistics_interval"]

# Listas: acrescenta as que faltam, sem duplicar nem remover as do usuario.
filters = cfg.setdefault("filters", []) or []
known = {f.get("url") for f in filters}
next_id = max([f.get("id", 0) for f in filters] + [0]) + 1
for f in s["filters"]:
    if f["url"] not in known:
        filters.append({"enabled": True, "url": f["url"], "name": f["name"], "id": next_id})
        next_id += 1
cfg["filters"] = filters

# Nomes locais: a secao mudou de dns: para filtering: nas versoes recentes.
# Os nomes que apontam para o servidor sao substituidos pela lista do arquivo (assim um nome
# antigo some); os demais, criados pelo painel, ficam.
section = cfg["filtering"] if "filtering" in cfg else dns
rewrites = [x for x in (section.get("rewrites") or []) if x.get("answer") not in s["server_ips"]]
for r in s["rewrites"]:
    rewrites.append({"domain": r["domain"], "answer": r["answer"], "enabled": True})
section["rewrites"] = rewrites

subprocess.run(["systemctl", "stop", "AdGuardHome"], check=True)
shutil.copy2(CONFIG, CONFIG.with_suffix(".yaml.bak"))
CONFIG.write_text(yaml.safe_dump(cfg, sort_keys=False, allow_unicode=True))
subprocess.run(["systemctl", "start", "AdGuardHome"], check=True)
print("Configuracoes aplicadas; backup em AdGuardHome.yaml.bak")
