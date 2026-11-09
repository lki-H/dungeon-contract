// Package dctestnet provides a bounded loopback-only UDP conditioner for input-only game verification.
// It is not imported by the control server or used by ordinary player launches.
package dctestnet

import (
	"container/heap"
	"context"
	"errors"
	"math/rand"
	"net"
	"sync"
	"sync/atomic"
	"syscall"
	"time"
)

type Profile struct {
	Name              string        `json:"name"`
	Delay             time.Duration `json:"one_way_delay_ns"`
	Jitter            time.Duration `json:"jitter_ns"`
	LossPermille      int           `json:"loss_permille"`
	DuplicatePermille int           `json:"duplicate_permille"`
}

func Named(name string) (Profile, error) {
	switch name {
	case "mild":
		return Profile{Name: name, Delay: 50 * time.Millisecond, Jitter: 15 * time.Millisecond, LossPermille: 10}, nil
	case "adverse":
		return Profile{Name: name, Delay: 100 * time.Millisecond, Jitter: 40 * time.Millisecond, LossPermille: 30, DuplicatePermille: 10}, nil
	default:
		return Profile{}, errors.New("unknown local UDP test profile")
	}
}

type Snapshot struct {
	Profile          Profile `json:"profile"`
	Received         uint64  `json:"received_datagrams"`
	Forwarded        uint64  `json:"forwarded_datagrams"`
	IntentionalLoss  uint64  `json:"intentional_loss"`
	Duplicates       uint64  `json:"scheduled_duplicates"`
	Oversized        uint64  `json:"oversized_datagrams"`
	BudgetDrops      uint64  `json:"budget_drops"`
	ForeignPeer      uint64  `json:"foreign_peer_drops"`
	WriteErrors      uint64  `json:"write_errors"`
	PeakQueued       uint64  `json:"peak_queued"`
	PeakQueuedBytes  uint64  `json:"peak_queued_bytes"`
	Upstream         uint64  `json:"upstream_forwarded"`
	Downstream       uint64  `json:"downstream_forwarded"`
	Reordered        uint64  `json:"reordered_datagrams"`
	PeerSessions     uint64  `json:"peer_sessions"`
	PeerResetSignals uint64  `json:"peer_reset_signals"`
}

type Relay struct {
	local      *net.UDPConn
	target     *net.UDPAddr
	profile    Profile
	cancel     context.CancelFunc
	done       chan struct{}
	readers    sync.WaitGroup
	once       sync.Once
	received   atomic.Uint64
	forwarded  atomic.Uint64
	loss       atomic.Uint64
	duplicates atomic.Uint64
	oversized  atomic.Uint64
	budget     atomic.Uint64
	foreign    atomic.Uint64
	writes     atomic.Uint64
	peak       atomic.Uint64
	peakBytes  atomic.Uint64
	upstream   atomic.Uint64
	downstream atomic.Uint64
	reordered  atomic.Uint64
	sessions   atomic.Uint64
	peerResets atomic.Uint64
}

type packet struct {
	bytes    []byte
	at       time.Time
	peer     *net.UDPAddr
	server   *net.UDPConn
	up       bool
	sequence uint64
}
type packets []packet

func (p packets) Len() int { return len(p) }
func (p packets) Less(i, j int) bool {
	return p[i].at.Before(p[j].at) || p[i].at.Equal(p[j].at) && p[i].sequence < p[j].sequence
}
func (p packets) Swap(i, j int) { p[i], p[j] = p[j], p[i] }
func (p *packets) Push(v any)   { *p = append(*p, v.(packet)) }
func (p *packets) Pop() any {
	v := *p
	item := v[len(v)-1]
	v[len(v)-1] = packet{}
	*p = v[:len(v)-1]
	return item
}

func New(ctx context.Context, listen, target string, profile Profile, seed int64) (*Relay, error) {
	if profile.Delay < 0 || profile.Delay > time.Second || profile.Jitter < 0 || profile.Jitter > profile.Delay || profile.LossPermille < 0 || profile.LossPermille > 1000 || profile.DuplicatePermille < 0 || profile.DuplicatePermille > 1000 {
		return nil, errors.New("invalid bounded UDP conditioner profile")
	}
	for _, address := range []string{listen, target} {
		host, _, err := net.SplitHostPort(address)
		if err != nil || host != "127.0.0.1" {
			return nil, errors.New("test relay only accepts exact 127.0.0.1 endpoints")
		}
	}
	localAddress, err := net.ResolveUDPAddr("udp4", listen)
	if err != nil {
		return nil, err
	}
	serverAddress, err := net.ResolveUDPAddr("udp4", target)
	if err != nil {
		return nil, err
	}
	if !localAddress.IP.Equal(net.IPv4(127, 0, 0, 1)) || !serverAddress.IP.Equal(net.IPv4(127, 0, 0, 1)) {
		return nil, errors.New("test relay only accepts exact 127.0.0.1 endpoints")
	}
	local, err := net.ListenUDP("udp4", localAddress)
	if err != nil {
		return nil, err
	}
	if serverAddress.Port == 0 {
		local.Close()
		return nil, errors.New("upstream port must be nonzero")
	}
	ctx, cancel := context.WithCancel(ctx)
	r := &Relay{local: local, target: serverAddress, profile: profile, cancel: cancel, done: make(chan struct{})}
	input := make(chan packet, 256)
	r.readers.Add(1)
	go r.read(ctx, local, true, nil, input)
	go r.run(ctx, input, seed)
	return r, nil
}

func (r *Relay) Address() string { return r.local.LocalAddr().String() }
func (r *Relay) Stats() Snapshot {
	return Snapshot{r.profile, r.received.Load(), r.forwarded.Load(), r.loss.Load(), r.duplicates.Load(), r.oversized.Load(), r.budget.Load(), r.foreign.Load(), r.writes.Load(), r.peak.Load(), r.peakBytes.Load(), r.upstream.Load(), r.downstream.Load(), r.reordered.Load(), r.sessions.Load(), r.peerResets.Load()}
}
func (r *Relay) Close() error {
	r.once.Do(func() { r.cancel(); r.local.Close(); <-r.done; r.readers.Wait() })
	return nil
}

func (r *Relay) read(ctx context.Context, connection *net.UDPConn, up bool, client *net.UDPAddr, input chan<- packet) {
	defer r.readers.Done()
	buffer := make([]byte, 65536)
	for {
		_ = connection.SetReadDeadline(time.Now().Add(200 * time.Millisecond))
		n, peer, err := connection.ReadFromUDP(buffer)
		if err != nil {
			if ctx.Err() != nil {
				return
			}
			if errors.Is(err, syscall.ECONNRESET) || errors.Is(err, syscall.ECONNREFUSED) {
				r.peerResets.Add(1)
				continue
			}
			if value, ok := err.(net.Error); ok && value.Timeout() {
				continue
			}
			return
		}
		r.received.Add(1)
		if n > 2048 {
			r.oversized.Add(1)
			continue
		}
		if !up {
			peer = client
		}
		p := packet{bytes: append([]byte(nil), buffer[:n]...), peer: peer, server: connection, up: up}
		select {
		case input <- p:
		case <-ctx.Done():
			return
		default:
			r.budget.Add(1)
		}
	}
}

func (r *Relay) run(ctx context.Context, input chan packet, seed int64) {
	sessions := make(map[string]*net.UDPConn)
	defer func() {
		for _, socket := range sessions {
			socket.Close()
		}
		r.local.Close()
		close(r.done)
	}()
	random := rand.New(rand.NewSource(seed))
	queue := packets{}
	queuedBytes := 0
	var sequence uint64
	var lastUp, lastDown uint64
	timer := time.NewTicker(time.Millisecond)
	defer timer.Stop()
	add := func(p packet) {
		if len(queue) >= 1024 || queuedBytes+len(p.bytes) > 2*1024*1024 {
			r.budget.Add(1)
			return
		}
		heap.Push(&queue, p)
		queuedBytes += len(p.bytes)
		if uint64(len(queue)) > r.peak.Load() {
			r.peak.Store(uint64(len(queue)))
		}
		if uint64(queuedBytes) > r.peakBytes.Load() {
			r.peakBytes.Store(uint64(queuedBytes))
		}
	}
	for {
		select {
		case <-ctx.Done():
			return
		case p := <-input:
			if p.up {
				if !p.peer.IP.Equal(net.IPv4(127, 0, 0, 1)) {
					r.foreign.Add(1)
					continue
				}
				socket, ok := sessions[p.peer.String()]
				if !ok {
					if len(sessions) >= 4 {
						r.budget.Add(1)
						continue
					}
					var err error
					socket, err = net.DialUDP("udp4", nil, r.target)
					if err != nil {
						r.writes.Add(1)
						continue
					}
					sessions[p.peer.String()] = socket
					r.sessions.Add(1)
					r.readers.Add(1)
					go r.read(ctx, socket, false, p.peer, input)
				}
				p.server = socket
			}
			if random.Intn(1000) < r.profile.LossPermille {
				r.loss.Add(1)
				continue
			}
			sequence++
			p.sequence = sequence
			delay := r.profile.Delay
			if r.profile.Jitter > 0 {
				delay += time.Duration(random.Int63n(int64(2*r.profile.Jitter)+1)) - r.profile.Jitter
			}
			p.at = time.Now().Add(delay)
			add(p)
			if random.Intn(1000) < r.profile.DuplicatePermille {
				r.duplicates.Add(1)
				duplicate := p
				duplicate.at = duplicate.at.Add(time.Millisecond)
				add(duplicate)
			}
		case now := <-timer.C:
			for len(queue) > 0 && !queue[0].at.After(now) {
				p := heap.Pop(&queue).(packet)
				queuedBytes -= len(p.bytes)
				var err error
				if p.up {
					_, err = p.server.Write(p.bytes)
					if p.sequence < lastUp {
						r.reordered.Add(1)
					} else {
						lastUp = p.sequence
					}
					r.upstream.Add(1)
				} else {
					_, err = r.local.WriteToUDP(p.bytes, p.peer)
					if p.sequence < lastDown {
						r.reordered.Add(1)
					} else {
						lastDown = p.sequence
					}
					r.downstream.Add(1)
				}
				if err != nil {
					if ctx.Err() == nil {
						r.writes.Add(1)
					}
				} else {
					r.forwarded.Add(1)
				}
			}
		}
	}
}
