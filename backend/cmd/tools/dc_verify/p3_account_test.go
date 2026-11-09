package main

import (
	"os"
	"path/filepath"
	"testing"
)

func TestP3LocalAccountPersistsAndNeverOverwritesCredentials(test *testing.T) {
	path := filepath.Join(test.TempDir(), "local.json")
	first, fresh, err := loadP3LocalAccount(path)
	if err != nil || !fresh {
		test.Fatal("initial profile", err)
	}
	if err := saveP3LocalAccount(path, first); err != nil {
		test.Fatal(err)
	}
	second, fresh, err := loadP3LocalAccount(path)
	if err != nil || fresh || first.username != second.username || first.password != second.password {
		test.Fatal("profile lost across launches", err)
	}
	if err := saveP3LocalAccount(path, &client{username: "replacement", password: "replacement"}); err == nil {
		test.Fatal("overwrote saved profile")
	}
	if err := os.WriteFile(path, []byte(`{"username":"partial"}`), 0600); err != nil {
		test.Fatal(err)
	}
	if _, _, err := loadP3LocalAccount(path); err == nil {
		test.Fatal("broken profile silently reset")
	}
}
