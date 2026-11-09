package main

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestPortableWindowVersionDefaultAndFallback(test *testing.T) {
	for _, entry := range []struct {
		runtime       string
		width, height int
	}{{"v4", 1280, 720}, {"v5", 1536, 864}} {
		width, height, err := windowDimensions(entry.runtime, 0, 0)
		if err != nil || width != entry.width || height != entry.height {
			test.Fatal("runtime window default", entry.runtime, err)
		}
	}
	if width, height, err := windowDimensions("v5", 960, 540); err != nil || width != 960 || height != 540 {
		test.Fatal("work area fallback", err)
	}
	for _, size := range [][2]int{{0, 540}, {960, 0}, {8000, 4500}, {960, 600}} {
		if _, _, err := windowDimensions("v5", size[0], size[1]); err == nil {
			test.Fatal("invalid window", size)
		}
	}
}

func TestPrivateIdentityPersistsAndRefusesReplacement(test *testing.T) {
	path := filepath.Join(test.TempDir(), "account.json")
	account, fresh, err := loadIdentity(path)
	if err != nil || !fresh || len(account.Password) < 32 {
		test.Fatal("new identity", err)
	}
	if err = exclusiveJSON(path, account); err != nil {
		test.Fatal(err)
	}
	again, fresh, err := loadIdentity(path)
	if err != nil || fresh || again != account {
		test.Fatal("identity reuse", err)
	}
	if exclusiveJSON(path, identity{Username: "replacement", Password: "replacement"}) == nil {
		test.Fatal("overwrote real identity")
	}
	for _, data := range []string{`{"username":"valid_name","password":"` + strings.Repeat("a", 32) + `","unexpected":true}`, `{"username":"valid_name","password":"` + strings.Repeat("a", 32) + `"} {}`, strings.Repeat("x", 4097)} {
		if err = os.WriteFile(path, []byte(data), 0600); err != nil {
			test.Fatal(err)
		}
		if _, _, err = loadIdentity(path); err == nil {
			test.Fatal("malformed identity accepted")
		}
		current, _ := os.ReadFile(path)
		if string(current) != data {
			test.Fatal("malformed file changed")
		}
	}
}
