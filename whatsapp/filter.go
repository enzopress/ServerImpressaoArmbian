package main

import (
	"path/filepath"
	"strings"
	"sync"
	"time"
)

// Kind diz o que fazer com uma mídia recebida.
type Kind int

const (
	KindIgnore      Kind = iota // texto, figurinha, áudio, vídeo...: não responde
	KindPDF                     // imprime como documento
	KindImage                   // imprime ajustada à página
	KindUnsupported             // documento de outro tipo: responde pedindo PDF
)

// Classify decide pelo tipo MIME (e pela extensão, quando o MIME vem genérico).
// A validação real do conteúdo (magic bytes) é feita pelo PrintServer.
func Classify(isDocument, isImage bool, mime, fileName string) Kind {
	mime = strings.ToLower(strings.TrimSpace(strings.SplitN(mime, ";", 2)[0]))
	ext := strings.ToLower(filepath.Ext(fileName))
	switch {
	case isImage:
		if mime == "image/jpeg" || mime == "image/png" {
			return KindImage
		}
		return KindIgnore
	case isDocument:
		if mime == "application/pdf" || ext == ".pdf" {
			return KindPDF
		}
		if mime == "image/jpeg" || mime == "image/png" || ext == ".jpg" || ext == ".jpeg" || ext == ".png" {
			return KindImage
		}
		return KindUnsupported
	default:
		return KindIgnore
	}
}

// MaxAge: mensagens mais antigas que o início do serviço menos esta folga não são impressas
// (evita imprimir o histórico ao vincular, mas pega o que chegou num reinício rápido).
const MaxAge = 10 * time.Minute

// TooOld informa se a mensagem é anterior ao início do serviço (menos a folga).
func TooOld(sent, serviceStart time.Time) bool {
	return sent.Before(serviceStart.Add(-MaxAge))
}

// RateLimiter limita quantos trabalhos a conversa pode mandar por janela de tempo.
type RateLimiter struct {
	mu     sync.Mutex
	max    int
	window time.Duration
	times  []time.Time
}

func NewRateLimiter(max int, window time.Duration) *RateLimiter {
	return &RateLimiter{max: max, window: window}
}

// Allow registra o pedido e diz se ele cabe no limite.
func (r *RateLimiter) Allow(now time.Time) bool {
	r.mu.Lock()
	defer r.mu.Unlock()
	kept := r.times[:0]
	for _, t := range r.times {
		if now.Sub(t) < r.window {
			kept = append(kept, t)
		}
	}
	r.times = kept
	if len(r.times) >= r.max {
		return false
	}
	r.times = append(r.times, now)
	return true
}
