package main

import (
	"context"
	"flag"
	"fmt"
	"net"
	"net/http"
	"os"
	"os/signal"
	"strings"
	"syscall"
	"time"

	"game-realtime-gm/backend/internal/dclan"
)

func main() {
	host := flag.String("host", "", "explicit local LAN or Radmin IPv4")
	players := flag.String("allow", "", "comma separated exact player IPv4s, including host player")
	flag.Parse()
	if err := execute(*host, strings.Split(*players, ",")); err != nil {
		fmt.Fprintln(os.Stderr, "Demo LAN gateway:", err)
		os.Exit(1)
	}
}
func execute(host string, peers []string) error {
	if err := dclan.LocalHost(host); err != nil {
		return err
	}
	handler, err := dclan.NewGateway(host, peers)
	if err != nil {
		return err
	}
	listener, err := net.Listen("tcp", net.JoinHostPort(host, "12880"))
	if err != nil {
		return err
	}
	server := &http.Server{Handler: handler, ReadHeaderTimeout: 5 * time.Second, ReadTimeout: 10 * time.Second, IdleTimeout: 60 * time.Second, MaxHeaderBytes: 16384}
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	go func() {
		<-ctx.Done()
		deadline, cancel := context.WithTimeout(context.Background(), 3*time.Second)
		defer cancel()
		_ = server.Shutdown(deadline)
	}()
	fmt.Println("Player gateway ready:", net.JoinHostPort(host, "12880"), "TCP; internal APIs and storage stay on loopback.")
	err = server.Serve(dclan.LimitConnections(listener))
	if err == http.ErrServerClosed {
		return nil
	}
	return err
}
