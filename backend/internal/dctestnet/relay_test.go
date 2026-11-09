package dctestnet

import (
	"context"
	"net"
	"testing"
	"time"
)

func echo(t *testing.T) *net.UDPConn {
	t.Helper()
	server, err := net.ListenUDP("udp4", &net.UDPAddr{IP: net.IPv4(127, 0, 0, 1)})
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { server.Close() })
	go func() {
		b := make([]byte, 65536)
		for {
			n, p, err := server.ReadFromUDP(b)
			if err != nil {
				return
			}
			_, _ = server.WriteToUDP(b[:n], p)
		}
	}()
	return server
}

func TestActualRoundTripDelayAndFreshClientPort(t *testing.T) {
	server := echo(t)
	r, err := New(context.Background(), "127.0.0.1:0", server.LocalAddr().String(), Profile{Name: "delay", Delay: 20 * time.Millisecond}, 7)
	if err != nil {
		t.Fatal(err)
	}
	defer r.Close()
	for i := 0; i < 2; i++ {
		address, _ := net.ResolveUDPAddr("udp4", r.Address())
		client, err := net.DialUDP("udp4", nil, address)
		if err != nil {
			t.Fatal(err)
		}
		start := time.Now()
		_, _ = client.Write([]byte{byte(i)})
		_ = client.SetReadDeadline(time.Now().Add(2 * time.Second))
		b := make([]byte, 8)
		n, err := client.Read(b)
		client.Close()
		if err != nil || n != 1 || b[0] != byte(i) {
			t.Fatalf("round trip %d failed: %v", i, err)
		}
		if time.Since(start) < 35*time.Millisecond {
			t.Fatal("conditioned path returned before the configured two-way delay")
		}
	}
	s := r.Stats()
	if s.PeerSessions != 2 || s.Upstream != 2 || s.Downstream != 2 || s.BudgetDrops != 0 || s.WriteErrors != 0 {
		t.Fatalf("unexpected transport counters: %+v", s)
	}
}

func TestLossAndOversizedPacketsAreCountedWithoutForwarding(t *testing.T) {
	server := echo(t)
	r, err := New(context.Background(), "127.0.0.1:0", server.LocalAddr().String(), Profile{Name: "drop", LossPermille: 1000}, 17)
	if err != nil {
		t.Fatal(err)
	}
	defer r.Close()
	address, _ := net.ResolveUDPAddr("udp4", r.Address())
	client, err := net.DialUDP("udp4", nil, address)
	if err != nil {
		t.Fatal(err)
	}
	defer client.Close()
	_, _ = client.Write([]byte("lost"))
	_, _ = client.Write(make([]byte, 4096))
	deadline := time.Now().Add(time.Second)
	for time.Now().Before(deadline) {
		s := r.Stats()
		if s.IntentionalLoss == 1 && s.Oversized == 1 {
			if s.Forwarded != 0 {
				t.Fatal("rejected packet was forwarded")
			}
			return
		}
		time.Sleep(time.Millisecond)
	}
	t.Fatal("expected loss/size accounting not observed")
}

func TestLoopbackBoundaryAndCancellation(t *testing.T) {
	for _, host := range []string{"0.0.0.0:0", "127.0.0.2:0", "example.com:0", "[::1]:0"} {
		if r, err := New(context.Background(), host, "127.0.0.1:12777", Profile{}, 1); err == nil {
			r.Close()
			t.Fatalf("non-exact loopback accepted: %s", host)
		}
	}
	ctx, cancel := context.WithCancel(context.Background())
	r, err := New(ctx, "127.0.0.1:0", "127.0.0.1:12777", Profile{}, 1)
	if err != nil {
		t.Fatal(err)
	}
	cancel()
	done := make(chan struct{})
	go func() { r.Close(); r.Close(); close(done) }()
	select {
	case <-done:
	case <-time.After(time.Second):
		t.Fatal("relay cancellation leaked workers")
	}
	if _, err := Named("arbitrary"); err == nil {
		t.Fatal("unknown profile accepted")
	}
}

func TestLateReplyToClosedPeerDoesNotKillNextSession(t *testing.T) {
	server := echo(t)
	relay, err := New(context.Background(), "127.0.0.1:0", server.LocalAddr().String(), Profile{Name: "late", Delay: 20 * time.Millisecond}, 19)
	if err != nil {
		t.Fatal(err)
	}
	defer relay.Close()
	address, _ := net.ResolveUDPAddr("udp4", relay.Address())
	first, err := net.DialUDP("udp4", nil, address)
	if err != nil {
		t.Fatal(err)
	}
	_, _ = first.Write([]byte("late"))
	first.Close()
	time.Sleep(120 * time.Millisecond)
	second, err := net.DialUDP("udp4", nil, address)
	if err != nil {
		t.Fatal(err)
	}
	defer second.Close()
	_, _ = second.Write([]byte("new"))
	_ = second.SetReadDeadline(time.Now().Add(2 * time.Second))
	buffer := make([]byte, 16)
	n, err := second.Read(buffer)
	if err != nil || string(buffer[:n]) != "new" {
		t.Fatalf("fresh peer after an old endpoint closed: %v", err)
	}
}
