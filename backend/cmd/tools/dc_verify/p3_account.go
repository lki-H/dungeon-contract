package main

import (
	"bytes"
	"crypto/rand"
	"encoding/json"
	"errors"
	"io"
	"os"
)

type p3LocalIdentity struct {
	Username string `json:"username"`
	Password string `json:"password"`
}

func loadP3LocalAccount(path string) (*client, bool, error) {
	data, err := os.ReadFile(path)
	if errors.Is(err, os.ErrNotExist) {
		return &client{username: "dclocal_" + rand.Text(), password: rand.Text() + rand.Text()}, true, nil
	}
	if err != nil || len(data) > 4096 {
		return nil, false, errors.New("P3 local profile is unreadable; existing credentials are preserved")
	}
	var identity p3LocalIdentity
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.DisallowUnknownFields()
	if decoder.Decode(&identity) != nil || decoder.Decode(new(any)) != io.EOF || len(identity.Username) < 8 || len(identity.Username) > 64 || len(identity.Password) < 16 || len(identity.Password) > 128 {
		return nil, false, errors.New("P3 local profile is invalid; existing credentials are preserved")
	}
	return &client{username: identity.Username, password: identity.Password}, false, nil
}

func saveP3LocalAccount(path string, account *client) error {
	file, err := os.OpenFile(path, os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0600)
	if err != nil {
		return errors.New("P3 local profile could not be created without overwriting existing credentials")
	}
	defer file.Close()
	if err := json.NewEncoder(file).Encode(p3LocalIdentity{Username: account.username, Password: account.password}); err != nil {
		return errors.New("P3 local profile could not be saved")
	}
	if err := file.Sync(); err != nil {
		return errors.New("P3 local profile could not be flushed")
	}
	return nil
}
