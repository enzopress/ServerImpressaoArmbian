package main

import (
	"regexp"
	"strconv"
	"strings"
)

var (
	copiesPattern = regexp.MustCompile(`(?i)\b(\d{1,2})\s*(?:x|c[oó]pias?)\b`)
	pagesPattern  = regexp.MustCompile(`(?i)\bp(?:[aá]g(?:inas?)?)?\.?\s*(\d+(?:\s*-\s*\d+)?(?:\s*,\s*\d+(?:\s*-\s*\d+)?)*)`)
)

// MaxCopies acompanha o limite do PrintServer (LpArguments.MaxCopies).
const MaxCopies = 20

// ParseCaption lê a legenda: "2x" ou "2 cópias" = cópias; "p 1-3", "pág 2,4" = páginas.
// Sem nada reconhecido: 1 cópia, todas as páginas.
func ParseCaption(caption string) (copies int, pages string) {
	copies = 1
	if m := copiesPattern.FindStringSubmatch(caption); m != nil {
		if n, err := strconv.Atoi(m[1]); err == nil && n >= 1 && n <= MaxCopies {
			copies = n
		}
	}
	if m := pagesPattern.FindStringSubmatch(caption); m != nil {
		pages = strings.ReplaceAll(m[1], " ", "")
	}
	return copies, pages
}
