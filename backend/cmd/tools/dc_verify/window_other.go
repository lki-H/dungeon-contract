//go:build !windows

package main

import "os/exec"

func hideValidationWindow(command *exec.Cmd) {}
