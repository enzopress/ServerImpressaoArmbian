# PrintServerMX9

Página web para imprimir na **HP Laser MFP 135a** pela rede de casa, rodando num TV box
**MX9** (RK3228A, ARMv7, 1 GB) com Armbian Debian 13 e CUPS.

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
| `deploy/` | `setup-mx9.sh` (prepara o aparelho), `deploy.ps1` (publica), `printserver.service` |
| `whatsapp/` | `wa-print` (Go): imprime PDFs e fotos enviados num grupo do WhatsApp |
| `deploy/whatsapp/` | `deploy.ps1` (compila e instala o wa-print), `wa-print.service` |

## Desenvolver no Windows

```powershell
dotnet test
dotnet run --project src/PrintServer.Web
```

Em `Development` o app usa um CUPS em memória (`PrintServer:UseFakeCups`), em
http://localhost:5119.

## Publicar no MX9

Pré-requisito: chave SSH do PC autorizada no root do MX9 (`~/.ssh/id_ed25519_mx9`).

```powershell
.\deploy\deploy.ps1                 # publica linux-arm self-contained e reinicia o serviço
.\deploy\deploy.ps1 -ReadyToRun     # opcional: inicia mais rápido no Cortex-A7
```

O serviço roda como o usuário `printserver`, na porta 80: http://192.168.15.2/

## Preparar um MX9 do zero

```powershell
Get-Content deploy\setup-mx9.sh -Raw | ssh root@192.168.15.2 "bash -s"
```

### Sobre o driver da 135a

A 135a não oferece IPP-over-USB e imprime lixo com PWG Raster e com o `splix`. O HPLIP não
suporta o modelo. O que funciona é o **HP ULD 1.00.39.12**, que não tem versão para ARM de
32 bits: o filtro `rastertospl` da versão i386 roda pelo `qemu-user` (binfmt), com as
bibliotecas i386 do multiarch. Leva uns 12 s por página.

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
liga o MX9 ao seu WhatsApp como um **aparelho conectado** e imprime o que for enviado num grupo:

- **PDF** imprime como documento; **foto (JPG/PNG)** imprime ajustada à folha.
- Legenda opcional: `2x` (ou `2 cópias`) = 2 cópias; `p 1-3` (ou `pág 2,4`) = páginas do PDF.
- Outros documentos (Word, Excel...) recebem ❌ pedindo PDF. Texto, figurinha, áudio e vídeo são ignorados.
- Limites: 20 MB por arquivo e 10 impressões por hora. Quando dá certo, a mensagem ganha a reação 🖨️.
- Mensagens de **outras conversas** são descartadas sem gravar nem registrar em log. Ao vincular, o
  histórico antigo não é impresso, e um reinício não reimprime nada.

**Instalar e vincular:**
1. No WhatsApp, crie o grupo **Impressora** (outro nome: `-ChatName`).
2. `.\deploy\whatsapp\deploy.ps1`: compila para ARMv7 e instala o serviço `wa-print`. Na primeira vez gera a
   chave da API no próprio MX9 (em `/etc/printserver.env` e `/etc/wa-print.env`, 0600).
3. Abra `http://impressora.casa/WhatsApp`, informe o número e digite o código no celular:
   WhatsApp → Aparelhos conectados → Conectar aparelho → Conectar com número de telefone.

**Desvincular:** no celular, em Aparelhos conectados, remova "Impressora MX9". A sessão fica em
`/var/lib/wa-print/session.db`, que dá acesso à conta: ela não entra no backup, e numa reinstalação
basta vincular de novo.

> Uso não oficial do WhatsApp: viola os termos do serviço, com risco (baixo) de bloqueio do número.

Desenvolvimento: `cd whatsapp; go test ./...` (o Go é instalado com `winget install --id GoLang.Go`).

Sem login nesta versão: use só na rede local e não abra a porta no roteador.
