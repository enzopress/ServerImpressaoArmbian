package main

import (
	"testing"
	"time"
)

func TestClassify(t *testing.T) {
	cases := []struct {
		name          string
		doc, img      bool
		mime, file    string
		want          Kind
	}{
		{"pdf", true, false, "application/pdf", "boleto.pdf", KindPDF},
		{"pdf mime generico", true, false, "application/octet-stream", "boleto.PDF", KindPDF},
		{"foto", false, true, "image/jpeg", "", KindImage},
		{"png como documento", true, false, "image/png", "print.png", KindImage},
		{"figurinha webp", false, true, "image/webp", "", KindIgnore},
		{"docx", true, false, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "a.docx", KindUnsupported},
		{"texto", false, false, "", "", KindIgnore},
	}
	for _, c := range cases {
		if got := Classify(c.doc, c.img, c.mime, c.file); got != c.want {
			t.Errorf("%s: got %v, want %v", c.name, got, c.want)
		}
	}
}

func TestTooOld(t *testing.T) {
	start := time.Date(2026, 11, 2, 10, 0, 0, 0, time.UTC)
	if !TooOld(start.Add(-time.Hour), start) {
		t.Error("mensagem de 1 h antes deveria ser ignorada")
	}
	if TooOld(start.Add(-5*time.Minute), start) {
		t.Error("mensagem de 5 min antes (reinicio rapido) deveria valer")
	}
	if TooOld(start.Add(time.Minute), start) {
		t.Error("mensagem nova deveria valer")
	}
}

func TestRateLimiter(t *testing.T) {
	r := NewRateLimiter(2, time.Hour)
	now := time.Now()
	if !r.Allow(now) || !r.Allow(now) {
		t.Fatal("os dois primeiros deveriam passar")
	}
	if r.Allow(now) {
		t.Error("o terceiro na mesma hora deveria ser barrado")
	}
	if !r.Allow(now.Add(61 * time.Minute)) {
		t.Error("depois da janela deveria liberar")
	}
}

func TestParseCaption(t *testing.T) {
	cases := []struct {
		in     string
		copies int
		pages  string
	}{
		{"", 1, ""},
		{"2x", 2, ""},
		{"3 cópias", 3, ""},
		{"p 1-3", 1, "1-3"},
		{"pág 2, 4", 1, "2,4"},
		{"2x páginas 1-2,5", 2, "1-2,5"},
		{"99x", 1, ""},
		{"boleto de outubro", 1, ""},
	}
	for _, c := range cases {
		copies, pages := ParseCaption(c.in)
		if copies != c.copies || pages != c.pages {
			t.Errorf("%q: got (%d, %q), want (%d, %q)", c.in, copies, pages, c.copies, c.pages)
		}
	}
}
