package main

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"mime/multipart"
	"net/http"
	"strconv"
	"time"
)

// PrintClient envia arquivos ao POST /api/print do PrintServer (mesma validação da página).
type PrintClient struct {
	URL  string
	Key  string
	HTTP *http.Client
}

type printResponse struct {
	Success bool   `json:"success"`
	JobID   string `json:"jobId"`
	Error   string `json:"error"`
}

// Print envia o arquivo e devolve o id do trabalho no CUPS.
func (p *PrintClient) Print(ctx context.Context, fileName string, data []byte, copies int, pages string) (string, error) {
	var body bytes.Buffer
	w := multipart.NewWriter(&body)
	part, err := w.CreateFormFile("file", fileName)
	if err != nil {
		return "", err
	}
	if _, err := part.Write(data); err != nil {
		return "", err
	}
	_ = w.WriteField("copies", strconv.Itoa(copies))
	if pages != "" {
		_ = w.WriteField("pages", pages)
	}
	if err := w.Close(); err != nil {
		return "", err
	}

	req, err := http.NewRequestWithContext(ctx, http.MethodPost, p.URL, &body)
	if err != nil {
		return "", err
	}
	req.Header.Set("Content-Type", w.FormDataContentType())
	req.Header.Set("X-Api-Key", p.Key)

	client := p.HTTP
	if client == nil {
		client = &http.Client{Timeout: 60 * time.Second}
	}
	resp, err := client.Do(req)
	if err != nil {
		return "", fmt.Errorf("servidor de impressão fora do ar")
	}
	defer resp.Body.Close()
	raw, _ := io.ReadAll(io.LimitReader(resp.Body, 64*1024))

	var r printResponse
	if err := json.Unmarshal(raw, &r); err != nil {
		return "", fmt.Errorf("resposta inesperada do servidor de impressão (HTTP %d)", resp.StatusCode)
	}
	if !r.Success {
		if r.Error == "" {
			r.Error = fmt.Sprintf("HTTP %d", resp.StatusCode)
		}
		return "", fmt.Errorf("%s", r.Error)
	}
	return r.JobID, nil
}
