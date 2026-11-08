package main

import (
	"context"
	"io"
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestPrintSendsFileKeyAndOptions(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Header.Get("X-Api-Key") != "k" {
			w.WriteHeader(http.StatusUnauthorized)
			return
		}
		f, h, err := r.FormFile("file")
		if err != nil {
			t.Fatal(err)
		}
		data, _ := io.ReadAll(f)
		if h.Filename != "a.pdf" || string(data) != "%PDF-x" || r.FormValue("copies") != "2" || r.FormValue("pages") != "1-3" {
			t.Errorf("campos errados: %s %q copies=%s pages=%s", h.Filename, data, r.FormValue("copies"), r.FormValue("pages"))
		}
		_, _ = w.Write([]byte(`{"success":true,"jobId":"HP135a-7","error":null}`))
	}))
	defer srv.Close()

	p := &PrintClient{URL: srv.URL, Key: "k"}
	job, err := p.Print(context.Background(), "a.pdf", []byte("%PDF-x"), 2, "1-3")
	if err != nil || job != "HP135a-7" {
		t.Fatalf("got %q, %v", job, err)
	}
}

func TestPrintReturnsServerError(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusUnprocessableEntity)
		_, _ = w.Write([]byte(`{"success":false,"jobId":null,"error":"O conteúdo do arquivo não corresponde à extensão."}`))
	}))
	defer srv.Close()

	p := &PrintClient{URL: srv.URL, Key: "k"}
	if _, err := p.Print(context.Background(), "a.pdf", []byte("x"), 1, ""); err == nil || err.Error() != "O conteúdo do arquivo não corresponde à extensão." {
		t.Fatalf("erro esperado do servidor, got %v", err)
	}
}
