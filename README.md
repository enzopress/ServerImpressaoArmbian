# PrintServerMX9

Página web para imprimir na **HP Laser MFP 135a** pela rede de casa, rodando num
**Dell Inspiron 23 5348** (Intel i5-4440S, x86_64, 16 GB, SSD de 1 TB) com Debian 13 e CUPS.

> Esta é a branch **`amd64`**, a do servidor atual. A branch **`arm`** guarda a versão para o TV box
> MX9 (RK3228A, ARMv7), que foi aposentado porque o eMMC dele corrompe dados.

- **Imprimir:** envie PDF, PNG, JPG ou TXT (até 20 MB), escolha cópias e páginas.
- **Fila:** status da impressora e trabalhos na fila, atualizados a cada 5 s. Cancela os
  trabalhos enviados pela página.
- `/health`: verifica se o CUPS e a fila estão respondendo.

A impressora também é anunciada na rede pelo Avahi (AirPrint/IPP), então celulares e o
Windows conseguem imprimir direto, sem esta página.

## Estrutura

| Pasta | Conteúdo |
|---|---|
| `src/PrintServer.Web` | ASP.NET Core Razor Pages (.NET 10) |
| `src/PrintServer.Web/Cups` | Integração com o CUPS via `lp`, `lpstat` e `cancel` (sem shell) |
| `src/PrintServer.Web/Printing` | Validação do upload e envio |
| `tests/PrintServer.Tests` | xUnit: parser do `lpstat`, argumentos do `lp`, upload e páginas |
| `deploy/` | `deploy.ps1` (publica), `printserver.service`, `backup.ps1`, `run-remote.ps1` |
| `deploy/dell/` | `setup-dell.sh` (prepara o servidor), chave SSH, DNS local e Docker |
| `whatsapp/` | `wa-print` (Go): imprime PDFs e fotos enviados num grupo do WhatsApp |
| `deploy/whatsapp/` | `deploy.ps1` (compila e instala o wa-print), `wa-print.service` |

## Desenvolver no Windows

```powershell
dotnet test
dotnet run --project src/PrintServer.Web
```

Em `Development` o app usa um CUPS em memória (`PrintServer:UseFakeCups`), em
http://localhost:5119.

## Publicar no servidor

Pré-requisito: chave SSH do PC autorizada no root do servidor (`~/.ssh/id_ed25519_dell`).

```powershell
.\deploy\deploy.ps1                 # publica linux-x64 self-contained e reinicia o serviço
```

O serviço roda como o usuário `printserver`, na porta 80: http://192.168.15.16/ ou
http://impressora.casa/ (para quem usa o AdGuard como DNS).

## Preparar o servidor do zero

```powershell
.\deploy\dell\autorizar-chave-dell.ps1
.\deploy\run-remote.ps1 deploy\dell\setup-dell.sh
```

### Sobre o driver da 135a

A 135a não oferece IPP-over-USB e imprime lixo com PWG Raster e com o `splix`. O HPLIP não
suporta o modelo. O que funciona é o **HP ULD 1.00.39.12**, que no servidor roda nativo em x86_64.

## Configuração (`appsettings.json`, seção `PrintServer`)

| Chave | Padrão | |
|---|---|---|
| `PrinterName` | `HP135a` | Nome da fila no CUPS |
| `MaxUploadMb` | `20` | Tamanho máximo do arquivo |
| `AllowedExtensions` | `.pdf .png .jpg .jpeg .txt` | Tipos aceitos (o conteúdo também é conferido) |
| `SpoolDir` | `/var/lib/printserver/uploads` | Pasta temporária dos uploads |
| `UseFakeCups` | `false` | CUPS em memória, para desenvolvimento |
| `ApiKey` | vazio | Chave do `POST /api/print` (vazio desliga). Em produção vem de `/etc/printserver.env` (`PrintServer__ApiKey`) |
| `WaPrintUrl` | `http://127.0.0.1:8085` | Endereço local do wa-print, lido pela página WhatsApp |

## Imprimir pelo WhatsApp

O `wa-print` (pasta `whatsapp/`, em Go com a biblioteca [whatsmeow](https://github.com/tulir/whatsmeow))
liga o servidor ao seu WhatsApp como um **aparelho conectado** e imprime o que for enviado num grupo:

- **PDF** imprime como documento; **foto (JPG/PNG)** imprime ajustada à folha.
- Legenda opcional: `2x` (ou `2 cópias`) = 2 cópias; `p 1-3` (ou `pág 2,4`) = páginas do PDF.
- Outros documentos (Word, Excel...) recebem ❌ pedindo PDF. Texto, figurinha, áudio e vídeo são ignorados.
- Limites: 20 MB por arquivo e 10 impressões por hora. Quando dá certo, a mensagem ganha a reação 🖨️.
- Mensagens de **outras conversas** são descartadas sem gravar nem registrar em log. Ao vincular, o
  histórico antigo não é impresso, e um reinício não reimprime nada.

**Instalar e vincular:**
1. No WhatsApp, crie o grupo **Impressora** (outro nome: `-ChatName`).
2. `.\deploy\whatsapp\deploy.ps1`: compila para amd64 e instala o serviço `wa-print`. Na primeira vez gera a
   chave da API no próprio servidor (em `/etc/printserver.env` e `/etc/wa-print.env`, 0600).
3. Abra `http://impressora.casa/WhatsApp`, clique em **Vincular com QR code** e, no celular, vá em
   WhatsApp → Aparelhos conectados → Conectar aparelho e aponte a câmera. A vinculação por número
   continua disponível na página, mas não funciona em contas com passkey.

**Desvincular:** no celular, em Aparelhos conectados, remova "Impressora". A sessão fica em
`/var/lib/wa-print/session.db`, que dá acesso à conta: ela não entra no backup, e numa reinstalação
basta vincular de novo.

> Uso não oficial do WhatsApp: viola os termos do serviço, com risco (baixo) de bloqueio do número.

Desenvolvimento: `cd whatsapp; go test ./...` (o Go é instalado com `winget install --id GoLang.Go`).

Sem login nesta versão: use só na rede local e não abra a porta no roteador.
