// Confirmação antes de cancelar (funciona também nas tabelas recarregadas).
document.addEventListener("submit", (event) => {
    const message = event.target.dataset.confirm;
    if (message && !window.confirm(message)) {
        event.preventDefault();
    }
});

// Atualiza a tabela da fila a cada N ms sem recarregar a página.
const refreshTarget = document.querySelector("[data-refresh-url]");
if (refreshTarget) {
    const url = refreshTarget.dataset.refreshUrl;
    const interval = Number(refreshTarget.dataset.refreshMs) || 5000;
    setInterval(async () => {
        if (document.hidden) return;
        try {
            const response = await fetch(url, { headers: { "X-Requested-With": "fetch" } });
            if (response.ok) refreshTarget.innerHTML = await response.text();
        } catch {
            // Sem rede por um instante: tenta de novo no próximo ciclo.
        }
    }, interval);
}
