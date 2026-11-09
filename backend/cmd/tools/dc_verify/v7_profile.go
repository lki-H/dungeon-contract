package main

import (
	"os"
	"path/filepath"
)

func modernRuntimeConfigured() bool {
	n := filepath.Base(os.Getenv("DC_CONFIG_PATH"))
	return n == "demo-v8-p5.json" || n == "demo-v6-p5.json" || n == "demo-v7-p5.json"
}
func modernRuntime() string {
	if filepath.Base(os.Getenv("DC_CONFIG_PATH")) == "demo-v8-p5.json" {
		return "v8"
	}
	if filepath.Base(os.Getenv("DC_CONFIG_PATH")) == "demo-v7-p5.json" {
		return "v7"
	}
	return "v6"
}
func modernProtocol() int {
	if modernRuntime() == "v8" {
		return 8
	}
	if modernRuntime() == "v7" {
		return 7
	}
	return 6
}
func modernProfile() string {
	if modernRuntime() == "v8" {
		return "dc_ruins.v8.p5.1"
	}
	if modernRuntime() == "v7" {
		return "dc_ruins.v7.p5.2"
	}
	return "dc_ruins.v6.p5.2"
}
func modernTrial() string {
	if modernRuntime() == "v8" {
		return "trial.v8.p5.1"
	}
	if modernRuntime() == "v7" {
		return "trial.v7.p5.2"
	}
	return "trial.v6.p5.2"
}
