// wa-print: imprime no servidor de impressão os PDFs e fotos enviados num grupo do WhatsApp.
//
// O servidor vira um "aparelho conectado" da conta (como o WhatsApp Web). Só a conversa
// configurada é processada; o resto é descartado sem gravar nem logar.
// Estado e vinculação em http://127.0.0.1:8085 (lido pela página /WhatsApp do PrintServer).
package main

import (
	"context"
	"database/sql"
	"encoding/json"
	"errors"
	"fmt"
	"log"
	"net/http"
	"os"
	"os/signal"
	"path/filepath"
	"strconv"
	"strings"
	"sync"
	"syscall"
	"time"

	qrcode "github.com/skip2/go-qrcode"
	"go.mau.fi/whatsmeow"
	"go.mau.fi/whatsmeow/proto/waE2E"
	"go.mau.fi/whatsmeow/store"
	"go.mau.fi/whatsmeow/store/sqlstore"
	"go.mau.fi/whatsmeow/types"
	"go.mau.fi/whatsmeow/types/events"
	waLog "go.mau.fi/whatsmeow/util/log"
	"google.golang.org/protobuf/proto"
	_ "modernc.org/sqlite"
)

type config struct {
	stateDir    string
	listen      string
	chatName    string
	chatJID     string
	apiURL      string
	apiKey      string
	maxBytes    uint64
	maxPerHour  int
}

func env(name, def string) string {
	if v := strings.TrimSpace(os.Getenv(name)); v != "" {
		return v
	}
	return def
}

func envInt(name string, def int) int {
	if n, err := strconv.Atoi(env(name, "")); err == nil && n > 0 {
		return n
	}
	return def
}

func loadConfig() config {
	return config{
		// O systemd define STATE_DIRECTORY com StateDirectory=wa-print.
		stateDir:   env("STATE_DIRECTORY", env("WA_STATE_DIR", "/var/lib/wa-print")),
		listen:     env("WA_LISTEN", "127.0.0.1:8085"),
		chatName:   env("WA_CHAT_NAME", "Impressora"),
		chatJID:    env("WA_CHAT_JID", ""),
		apiURL:     env("PRINT_API_URL", "http://127.0.0.1/api/print"),
		apiKey:     env("PRINT_API_KEY", ""),
		maxBytes:   uint64(envInt("WA_MAX_MB", 20)) * 1024 * 1024,
		maxPerHour: envInt("WA_MAX_PER_HOUR", 10),
	}
}

type recent struct {
	Time   time.Time `json:"time"`
	File   string    `json:"file"`
	Result string    `json:"result"`
}

type app struct {
	cfg       config
	container *sqlstore.Container
	client    *whatsmeow.Client
	printer *PrintClient
	seen    *sql.DB
	limiter *RateLimiter
	start   time.Time

	mu      sync.Mutex
	chat    types.JID
	recents []recent
	pairing bool
	deleted bool   // a sessão foi apagada (desvinculada): a próxima vinculação começa uma nova
	qrCode  string // QR atual da vinculação por QR ("" fora dela)

	printMu sync.Mutex // um trabalho por vez
}

func main() {
	cfg := loadConfig()
	if cfg.apiKey == "" {
		log.Fatal("PRINT_API_KEY não definida")
	}
	if err := os.MkdirAll(cfg.stateDir, 0o700); err != nil {
		log.Fatal(err)
	}
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	// A sessão dá acesso à conta: só o dono do serviço lê (0600 pelo umask do systemd).
	sessionDSN := "file:" + filepath.Join(cfg.stateDir, "session.db") + "?_pragma=foreign_keys(1)&_pragma=busy_timeout(5000)"
	container, err := sqlstore.New(ctx, "sqlite", sessionDSN, waLog.Stdout("db", "WARN", false))
	if err != nil {
		log.Fatalf("sessão: %v", err)
	}
	device, err := container.GetFirstDevice(ctx)
	if err != nil {
		log.Fatalf("sessão: %v", err)
	}
	store.SetOSInfo("Impressora", [3]uint32{1, 0, 0})

	seen, err := openSeen(filepath.Join(cfg.stateDir, "processed.db"))
	if err != nil {
		log.Fatalf("processados: %v", err)
	}

	a := &app{
		cfg:       cfg,
		container: container,
		client:    whatsmeow.NewClient(device, waLog.Stdout("wa", "WARN", false)),
		printer: &PrintClient{URL: cfg.apiURL, Key: cfg.apiKey},
		seen:    seen,
		limiter: NewRateLimiter(cfg.maxPerHour, time.Hour),
		start:   time.Now(),
	}
	a.client.AddEventHandler(a.onEvent)

	if a.client.Store.ID != nil {
		if err := a.client.Connect(); err != nil {
			log.Printf("conectar: %v", err)
		}
	} else {
		log.Print("não vinculado: vincule em http://impressora.casa/WhatsApp")
	}
	go a.watchChat(ctx)

	srv := &http.Server{Addr: cfg.listen, Handler: a.routes(), ReadHeaderTimeout: 5 * time.Second}
	go func() {
		if err := srv.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			log.Fatalf("http: %v", err)
		}
	}()
	log.Printf("wa-print ouvindo em %s, conversa %q", cfg.listen, cfg.chatName)

	<-ctx.Done()
	shutdown, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	_ = srv.Shutdown(shutdown)
	a.client.Disconnect()
}

// --- mensagens já processadas (não reimprimir depois de reiniciar) ---

func openSeen(path string) (*sql.DB, error) {
	db, err := sql.Open("sqlite", "file:"+path+"?_pragma=busy_timeout(5000)")
	if err != nil {
		return nil, err
	}
	_, err = db.Exec(`CREATE TABLE IF NOT EXISTS processed (id TEXT PRIMARY KEY, at INTEGER NOT NULL)`)
	return db, err
}

// markSeen devolve false se a mensagem já tinha sido processada.
func (a *app) markSeen(id string) bool {
	res, err := a.seen.Exec(`INSERT OR IGNORE INTO processed (id, at) VALUES (?, ?)`, id, time.Now().Unix())
	if err != nil {
		log.Printf("processados: %v", err)
		return true
	}
	n, _ := res.RowsAffected()
	// Limpa o que tem mais de 30 dias.
	_, _ = a.seen.Exec(`DELETE FROM processed WHERE at < ?`, time.Now().Add(-30*24*time.Hour).Unix())
	return n == 1
}

// --- conversa vigiada ---

func (a *app) currentChat() types.JID {
	a.mu.Lock()
	defer a.mu.Unlock()
	return a.chat
}

// resolveChat acha o JID do grupo pelo nome (ou usa WA_CHAT_JID).
func (a *app) resolveChat(ctx context.Context) {
	if a.cfg.chatJID != "" {
		if jid, err := types.ParseJID(a.cfg.chatJID); err == nil {
			a.mu.Lock()
			a.chat = jid
			a.mu.Unlock()
		}
		return
	}
	if !a.client.IsLoggedIn() {
		return
	}
	groups, err := a.client.GetJoinedGroups(ctx)
	if err != nil {
		log.Printf("grupos: %v", err)
		return
	}
	for _, g := range groups {
		if strings.EqualFold(strings.TrimSpace(g.Name), a.cfg.chatName) {
			a.mu.Lock()
			changed := a.chat != g.JID
			a.chat = g.JID
			a.mu.Unlock()
			if changed {
				log.Printf("conversa %q encontrada", a.cfg.chatName)
			}
			return
		}
	}
}

// watchChat procura o grupo a cada minuto enquanto não acha (ex.: grupo criado depois).
func (a *app) watchChat(ctx context.Context) {
	t := time.NewTicker(time.Minute)
	defer t.Stop()
	for {
		select {
		case <-ctx.Done():
			return
		case <-t.C:
			if a.currentChat().IsEmpty() && a.client.IsConnected() {
				a.resolveChat(ctx)
			}
		}
	}
}

// --- eventos do WhatsApp ---

func (a *app) onEvent(evt any) {
	switch e := evt.(type) {
	case *events.Connected:
		log.Print("conectado ao WhatsApp")
		go a.resolveChat(context.Background())
	case *events.PairSuccess:
		log.Printf("vinculado ao número %s", e.ID.User)
	case *events.PairPasskeyRequest:
		// Desde 06/2026 o WhatsApp pode exigir passkey (WebAuthn de whatsapp.com) na vinculação
		// por número; um servidor não consegue responder. A vinculação por QR não passa por isso.
		log.Print("o WhatsApp pediu passkey: vincule pelo QR code em /WhatsApp")
		a.client.Disconnect()
	case *events.LoggedOut:
		log.Print("desvinculado pelo celular")
		a.mu.Lock()
		a.chat = types.EmptyJID
		a.deleted = true // o whatsmeow apagou o aparelho do banco
		a.mu.Unlock()
	case *events.Message:
		a.onMessage(e)
	}
}

func (a *app) onMessage(e *events.Message) {
	chat := a.currentChat()
	// Qualquer outra conversa: descarta sem gravar nem logar.
	if chat.IsEmpty() || e.Info.Chat != chat {
		return
	}
	if TooOld(e.Info.Timestamp, a.start) {
		return
	}

	msg := e.Message
	doc := msg.GetDocumentMessage()
	if doc == nil {
		doc = msg.GetDocumentWithCaptionMessage().GetMessage().GetDocumentMessage()
	}
	img := msg.GetImageMessage()

	var (
		kind     Kind
		fileName string
		caption  string
		size     uint64
		media    whatsmeow.DownloadableMessage
	)
	switch {
	case doc != nil:
		kind = Classify(true, false, doc.GetMimetype(), doc.GetFileName())
		fileName, caption, size, media = doc.GetFileName(), doc.GetCaption(), doc.GetFileLength(), doc
		if fileName == "" {
			fileName = "documento.pdf"
		}
	case img != nil:
		kind = Classify(false, true, img.GetMimetype(), "")
		ext := ".jpg"
		if strings.Contains(img.GetMimetype(), "png") {
			ext = ".png"
		}
		fileName = "foto-" + e.Info.Timestamp.Local().Format("20060102-150405") + ext
		caption, size, media = img.GetCaption(), img.GetFileLength(), img
	default:
		return
	}
	if kind == KindIgnore || !a.markSeen(e.Info.ID) {
		return
	}

	switch {
	case kind == KindUnsupported:
		a.fail(e, fileName, "Só imprimo PDF e fotos (JPG/PNG). Salve o arquivo como PDF e envie de novo.")
		return
	case size > a.cfg.maxBytes:
		a.fail(e, fileName, fmt.Sprintf("Arquivo grande demais (máximo %d MB).", a.cfg.maxBytes/1024/1024))
		return
	case !a.limiter.Allow(time.Now()):
		a.fail(e, fileName, fmt.Sprintf("Limite de %d impressões por hora atingido. Tente mais tarde.", a.cfg.maxPerHour))
		return
	}

	copies, pages := ParseCaption(caption)
	if kind == KindImage {
		pages = ""
	}
	go a.print(e, media, fileName, copies, pages)
}

func (a *app) print(e *events.Message, media whatsmeow.DownloadableMessage, fileName string, copies int, pages string) {
	a.printMu.Lock()
	defer a.printMu.Unlock()
	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Minute)
	defer cancel()

	data, err := a.client.Download(ctx, media)
	if err != nil {
		log.Printf("download %s: %v", fileName, err)
		a.fail(e, fileName, "Não consegui baixar o arquivo do WhatsApp. Envie de novo.")
		return
	}
	job, err := a.printer.Print(ctx, fileName, data, copies, pages)
	if err != nil {
		log.Printf("imprimir %s: %v", fileName, err)
		a.fail(e, fileName, "Não imprimi: "+err.Error())
		return
	}
	log.Printf("%s enviado para impressão (%s, %d cópia(s))", fileName, job, copies)
	a.react(e, "🖨️")
	a.addRecent(fileName, "impresso ("+job+")")
}

// --- respostas ---

func (a *app) react(e *events.Message, emoji string) {
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()
	reaction := a.client.BuildReaction(e.Info.Chat, e.Info.Sender, e.Info.ID, emoji)
	if _, err := a.client.SendMessage(ctx, e.Info.Chat, reaction); err != nil {
		log.Printf("reação: %v", err)
	}
}

func (a *app) fail(e *events.Message, fileName, reason string) {
	a.react(e, "❌")
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()
	reply := &waE2E.Message{Conversation: proto.String("❌ " + fileName + ": " + reason)}
	if _, err := a.client.SendMessage(ctx, e.Info.Chat, reply); err != nil {
		log.Printf("resposta: %v", err)
	}
	a.addRecent(fileName, "erro: "+reason)
}

func (a *app) addRecent(fileName, result string) {
	a.mu.Lock()
	defer a.mu.Unlock()
	a.recents = append([]recent{{Time: time.Now(), File: fileName, Result: result}}, a.recents...)
	if len(a.recents) > 10 {
		a.recents = a.recents[:10]
	}
}

// --- vinculação: QR code (recomendada) ou número (código de 8 caracteres) ---

// startPairing conecta sem sessão e espera o primeiro QR. Os QRs seguintes ficam em a.qrCode
// até a vinculação terminar (sucesso ou expirar).
func (a *app) startPairing() error {
	a.mu.Lock()
	if a.pairing {
		a.mu.Unlock()
		return errors.New("já existe uma vinculação em andamento; aguarde 1 minuto")
	}
	a.pairing = true
	a.mu.Unlock()
	release := func() {
		a.mu.Lock()
		a.pairing = false
		a.qrCode = ""
		a.mu.Unlock()
	}

	if a.client.Store.ID != nil {
		release()
		return errors.New("já está vinculado")
	}
	if a.client.IsConnected() {
		a.client.Disconnect()
	}
	// Depois de uma desvinculação o aparelho foi apagado ("invalid use of deleted device"):
	// cria uma sessão nova. Sem vínculo não há mensagens chegando, então trocar o cliente é seguro.
	a.mu.Lock()
	deleted := a.deleted
	a.mu.Unlock()
	if deleted {
		a.client.RemoveEventHandlers()
		a.client = whatsmeow.NewClient(a.container.NewDevice(), waLog.Stdout("wa", "WARN", false))
		a.client.AddEventHandler(a.onEvent)
		a.mu.Lock()
		a.deleted = false
		a.mu.Unlock()
		log.Print("sessão anterior apagada: iniciando uma nova")
	}
	qr, err := a.client.GetQRChannel(context.Background())
	if err != nil {
		release()
		return err
	}
	if err := a.client.Connect(); err != nil {
		release()
		return err
	}
	select {
	case item := <-qr:
		if item.Event != whatsmeow.QRChannelEventCode {
			release()
			return fmt.Errorf("vinculação: %s", item.Event)
		}
		a.setQR(item.Code)
	case <-time.After(30 * time.Second):
		release()
		return errors.New("o WhatsApp não respondeu; tente de novo")
	}
	go func() {
		defer release()
		for item := range qr {
			switch item.Event {
			case whatsmeow.QRChannelEventCode:
				a.setQR(item.Code)
			case "success":
				log.Print("vinculação concluída")
				return
			case "timeout":
				log.Print("vinculação expirou sem confirmação")
				a.client.Disconnect()
				return
			default:
				log.Printf("vinculação: %s", item.Event)
			}
		}
	}()
	return nil
}

func (a *app) setQR(code string) {
	a.mu.Lock()
	a.qrCode = code
	a.mu.Unlock()
}

// pair vincula pelo número: o código por número só vale depois que o servidor manda o primeiro QR.
func (a *app) pair(ctx context.Context, phone string) (string, error) {
	if err := a.startPairing(); err != nil {
		return "", err
	}
	return a.client.PairPhone(ctx, phone, true, whatsmeow.PairClientChrome, "Chrome (Linux)")
}

// --- API local (127.0.0.1) ---

func (a *app) routes() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /status", func(w http.ResponseWriter, r *http.Request) {
		a.mu.Lock()
		rec := append([]recent{}, a.recents...)
		chatFound := !a.chat.IsEmpty()
		qrActive := a.qrCode != ""
		a.mu.Unlock()
		phone := ""
		if id := a.client.Store.ID; id != nil {
			phone = "+" + id.User
		}
		writeJSON(w, http.StatusOK, map[string]any{
			"online":    true,
			"connected": a.client.IsConnected() && a.client.IsLoggedIn(),
			"loggedIn":  a.client.Store.ID != nil,
			"phone":     phone,
			"chatName":  a.cfg.chatName,
			"chatFound": chatFound,
			"qrActive":  qrActive,
			"recent":    rec,
		})
	})
	mux.HandleFunc("POST /pair-qr", func(w http.ResponseWriter, r *http.Request) {
		a.mu.Lock()
		active := a.qrCode != ""
		a.mu.Unlock()
		if active { // já tem QR aberto: a página só passa a mostrá-lo
			writeJSON(w, http.StatusOK, map[string]any{"ok": true})
			return
		}
		if err := a.startPairing(); err != nil {
			writeJSON(w, http.StatusConflict, map[string]any{"error": err.Error()})
			return
		}
		writeJSON(w, http.StatusOK, map[string]any{"ok": true})
	})
	// QR atual em PNG; muda a cada ~20 s enquanto a vinculação está aberta.
	mux.HandleFunc("GET /qr.png", func(w http.ResponseWriter, r *http.Request) {
		a.mu.Lock()
		code := a.qrCode
		a.mu.Unlock()
		if code == "" {
			http.NotFound(w, r)
			return
		}
		png, err := qrcode.Encode(code, qrcode.Medium, 320)
		if err != nil {
			http.Error(w, err.Error(), http.StatusInternalServerError)
			return
		}
		w.Header().Set("Content-Type", "image/png")
		w.Header().Set("Cache-Control", "no-store")
		_, _ = w.Write(png)
	})
	mux.HandleFunc("POST /pair", func(w http.ResponseWriter, r *http.Request) {
		var body struct {
			Phone string `json:"phone"`
		}
		if err := json.NewDecoder(http.MaxBytesReader(w, r.Body, 1024)).Decode(&body); err != nil || len(body.Phone) < 10 {
			writeJSON(w, http.StatusBadRequest, map[string]any{"error": "número inválido"})
			return
		}
		ctx, cancel := context.WithTimeout(r.Context(), 45*time.Second)
		defer cancel()
		code, err := a.pair(ctx, body.Phone)
		if err != nil {
			writeJSON(w, http.StatusConflict, map[string]any{"error": err.Error()})
			return
		}
		writeJSON(w, http.StatusOK, map[string]any{"code": code})
	})
	return mux
}

func writeJSON(w http.ResponseWriter, status int, v any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(v)
}
