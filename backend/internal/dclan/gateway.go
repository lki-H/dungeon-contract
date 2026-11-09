package dclan

import (
	"bytes"
	"encoding/json"
	"errors"
	"io"
	"net"
	"net/http"
	"net/http/httputil"
	"net/netip"
	"net/url"
	"regexp"
	"strings"
	"sync"
	"time"
)

func Host(value string) (netip.Addr, error) {
	address, err := netip.ParseAddr(value)
	if err != nil || !address.Is4() || address.String() != value || !(address.IsPrivate() || address.IsLoopback() || netip.MustParsePrefix("26.0.0.0/8").Contains(address)) {
		return netip.Addr{}, errors.New("use an explicit local IPv4, RFC1918 LAN IPv4 or Radmin 26.x IPv4; wildcard, DNS and public addresses are refused")
	}
	return address, nil
}

func LocalHost(value string) error {
	address, err := Host(value)
	if err != nil {
		return err
	}
	if address.IsLoopback() {
		return nil
	}
	interfaces, err := net.InterfaceAddrs()
	if err != nil {
		return err
	}
	for _, item := range interfaces {
		prefix, parseErr := netip.ParsePrefix(item.String())
		if parseErr == nil && prefix.Addr() == address {
			return nil
		}
	}
	return errors.New("the selected server IPv4 is not assigned to this computer; start VPN or correct the address")
}

var runPath = regexp.MustCompile(`^/api/v2/runs/[A-Za-z0-9_-]{1,64}(/admissions|/results)?$`)
var lobbyPath = regexp.MustCompile(`^/api/v2/(parties/[A-Za-z0-9_-]{1,64}(/snapshot)?|match/proposals/[A-Za-z0-9_-]{1,64}|regroup/[A-Za-z0-9_-]{1,64}|trials/dc_ruins_trial/leaderboard)$`)

func playerRoute(request *http.Request) bool {
	if request.URL.EscapedPath() != request.URL.Path || len(request.URL.RawQuery) > 512 {
		return false
	}
	path := request.URL.Path
	lobby := lobbyPath.MatchString(path) || path == "/api/v2/me/activity" || path == "/api/v2/friends" || path == "/api/v2/friends/requests" || path == "/api/v2/tasks" || path == "/api/v2/trials" || path == "/api/v2/me/run-records"
	if request.URL.RawQuery != "" {
		if !lobby || request.Method != http.MethodGet {
			return false
		}
		query, err := url.ParseQuery(request.URL.RawQuery)
		if err != nil {
			return false
		}
		for key, values := range query {
			if len(values) != 1 || len(values[0]) > 64 || key != "page" && key != "page_size" && key != "trial_version" && key != "initial_player_count" {
				return false
			}
		}
	}
	if lobby {
		return request.Method == http.MethodGet
	}
	if request.Method == http.MethodGet && (path == "/ready" || path == "/health" || path == "/ws") {
		return true
	}
	if request.Method == http.MethodPost && (path == "/api/register" || path == "/api/login") {
		return true
	}
	if !runPath.MatchString(path) {
		return false
	}
	return request.Method == http.MethodGet && !strings.HasSuffix(path, "/admissions") || request.Method == http.MethodPost && strings.HasSuffix(path, "/admissions")
}

func NewGateway(host string, peers []string) (http.Handler, error) {
	target, _ := url.Parse("http://127.0.0.1:12880")
	return gateway(host, peers, target)
}

func gateway(host string, peers []string, target *url.URL) (http.Handler, error) {
	if _, err := Host(host); err != nil {
		return nil, err
	}
	if len(peers) < 1 || len(peers) > 5 {
		return nil, errors.New("supply one to five explicit player IPv4 addresses")
	}
	allowed := map[netip.Addr]bool{}
	for _, peer := range peers {
		address, err := Host(peer)
		if err != nil {
			return nil, err
		}
		if allowed[address] {
			return nil, errors.New("duplicate allowed player IPv4")
		}
		allowed[address] = true
	}
	proxy := &httputil.ReverseProxy{
		Transport: &http.Transport{Proxy: nil, DialContext: (&net.Dialer{Timeout: 3 * time.Second, KeepAlive: 30 * time.Second}).DialContext, ResponseHeaderTimeout: 10 * time.Second, IdleConnTimeout: 60 * time.Second, MaxIdleConns: 32, MaxIdleConnsPerHost: 16, MaxConnsPerHost: 32},
		Rewrite: func(request *httputil.ProxyRequest) {
			request.SetURL(target)
			request.Out.Host = target.Host
			for _, header := range []string{"Forwarded", "X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto", "X-DC-Service-ID"} {
				request.Out.Header.Del(header)
			}
		},
		ModifyResponse: func(response *http.Response) error {
			response.Header.Set("Cache-Control", "no-store")
			if response.StatusCode != http.StatusOK || response.Request.Method != http.MethodPost || !strings.HasSuffix(response.Request.URL.Path, "/admissions") {
				return nil
			}
			data, err := io.ReadAll(io.LimitReader(response.Body, 65537))
			response.Body.Close()
			if err != nil || len(data) > 65536 {
				return errors.New("admission response size")
			}
			var value map[string]json.RawMessage
			if json.Unmarshal(data, &value) != nil || string(value["endpoint"]) != `"127.0.0.1:12777"` {
				return errors.New("unexpected authoritative battle endpoint")
			}
			value["endpoint"], _ = json.Marshal(net.JoinHostPort(host, "12777"))
			data, err = json.Marshal(value)
			if err != nil {
				return err
			}
			response.Body = io.NopCloser(bytes.NewReader(data))
			response.ContentLength = int64(len(data))
			response.Header.Del("Content-Length")
			return nil
		},
		ErrorHandler: func(writer http.ResponseWriter, request *http.Request, err error) {
			http.Error(writer, "Demo local control is not ready", http.StatusBadGateway)
		},
	}
	return http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		peer, _, err := net.SplitHostPort(request.RemoteAddr)
		address, parseErr := netip.ParseAddr(peer)
		if err != nil || parseErr != nil || !allowed[address.Unmap()] {
			http.Error(writer, "player IPv4 is not allowed by the host", http.StatusForbidden)
			return
		}
		if !playerRoute(request) {
			http.NotFound(writer, request)
			return
		}
		if request.URL.Path == "/ws" && !strings.HasPrefix(request.Header.Get("Authorization"), "Bearer ") {
			http.Error(writer, "use a native authenticated player connection", http.StatusUnauthorized)
			return
		}
		request.Body = http.MaxBytesReader(writer, request.Body, 65536)
		proxy.ServeHTTP(writer, request)
	}), nil
}

type boundedListener struct {
	net.Listener
	permits chan struct{}
}
type boundedConnection struct {
	net.Conn
	once    sync.Once
	release func()
}

func (connection *boundedConnection) Close() error {
	err := connection.Conn.Close()
	connection.once.Do(connection.release)
	return err
}
func (listener *boundedListener) Accept() (net.Conn, error) {
	for {
		connection, err := listener.Listener.Accept()
		if err != nil {
			return nil, err
		}
		select {
		case listener.permits <- struct{}{}:
			return &boundedConnection{Conn: connection, release: func() { <-listener.permits }}, nil
		default:
			connection.Close()
		}
	}
}
func LimitConnections(listener net.Listener) net.Listener {
	return &boundedListener{Listener: listener, permits: make(chan struct{}, 32)}
}
