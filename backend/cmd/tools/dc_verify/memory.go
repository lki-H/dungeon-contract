package main

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"time"

	"game-realtime-gm/backend/internal/pve/store"
)

func (runner *harness) startMemoryObserver(cancelRun context.CancelFunc) error {
	command := exec.Command("pwsh", "-NoProfile", "-File", "E:/game-realtime-gm-demo/deploy/dc-"+modernRuntime()+"-run-memory.ps1", "-Evidence", runner.output, "-OwnerPid", fmt.Sprint(os.Getpid()))
	hideValidationWindow(command)
	for _, entry := range os.Environ() {
		name := strings.ToUpper(strings.SplitN(entry, "=", 2)[0])
		if strings.Contains(name, "PASSWORD") || strings.Contains(name, "SECRET") || strings.Contains(name, "TOKEN") || strings.Contains(name, "API_KEY") || name == "DC_ADMISSION_KEY" {
			continue
		}
		command.Env = append(command.Env, entry)
	}
	log, err := os.OpenFile(filepath.Join(runner.output, "memory-observer.log"), os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0600)
	if err != nil {
		return err
	}
	defer log.Close()
	command.Stdout = log
	command.Stderr = log
	if err := command.Start(); err != nil {
		return errors.New("could not start isolated memory observer")
	}
	runner.memoryObserver = command
	ctx, cancel := context.WithCancel(context.Background())
	runner.memoryWatchCancel = cancel
	go func() {
		ticker := time.NewTicker(time.Second)
		defer ticker.Stop()
		for {
			select {
			case <-ctx.Done():
				return
			case <-ticker.C:
				if _, err := os.Stat(filepath.Join(runner.output, "memory-stop.json")); err == nil {
					cancelRun()
					return
				}
			}
		}
	}()
	return nil
}

func (runner *harness) finishMemoryObserver() error {
	if runner.memoryObserver == nil {
		return nil
	}
	if runner.memoryWatchCancel != nil {
		runner.memoryWatchCancel()
	}
	_ = os.WriteFile(filepath.Join(runner.output, "memory-done"), nil, 0600)
	command := runner.memoryObserver
	runner.memoryObserver = nil
	done := make(chan error, 1)
	go func() { done <- command.Wait() }()
	select {
	case err := <-done:
		if err != nil {
			return errors.New("memory observer failed; inspect isolated observer log")
		}
	case <-time.After(8 * time.Second):
		_ = command.Process.Kill()
		<-done
		return errors.New("memory observer cleanup exceeded deadline")
	}
	data, err := os.ReadFile(filepath.Join(runner.output, "process-memory.json"))
	if err != nil {
		return errors.New("memory observer report missing")
	}
	var report struct {
		Status   string `json:"status"`
		Rejected int    `json:"identity_rejections"`
		Samples  int    `json:"sample_count"`
	}
	if json.Unmarshal(data, &report) != nil || report.Status != "completed" || report.Rejected != 0 || report.Samples == 0 {
		return errors.New("memory guard or identity verification prevented valid completion")
	}
	return nil
}

func (runner *harness) writeProcessRoster(binary, role string, pid int, before, after time.Time) error {
	path, err := filepath.Abs(binary)
	if err != nil {
		return err
	}
	runner.processRoster = append(runner.processRoster, map[string]any{"role": role, "pid": pid, "binary": path, "started_before": before.UTC(), "started_after": after.UTC()})
	return os.WriteFile(filepath.Join(runner.output, "process-roster.json"), store.JSON(runner.processRoster), 0600)
}
