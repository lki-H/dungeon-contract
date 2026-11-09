package main

import (
	"os/exec"
	"syscall"
)

func hideValidationWindow(command *exec.Cmd) {
	command.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
}
