package dclan

import (
	"io"
	"net/http"
	"net/http/httptest"
	"net/url"
	"strings"
	"testing"
)

func TestExplicitHostPolicy(test *testing.T) {
	for _, host := range []string{"127.0.0.1", "127.0.0.2", "192.168.1.20", "10.1.2.3", "172.16.1.1", "172.31.255.2", "26.1.2.3"} {
		if _, err := Host(host); err != nil {
			test.Fatal(host, err)
		}
	}
	for _, host := range []string{"", "0.0.0.0", "8.8.8.8", "172.32.0.1", "192.0.2.1", "localhost", "http://26.1.2.3", "26.1.2.3:12880", "::1", "::ffff:127.0.0.1", "127.1", " 26.1.2.3", "255.255.255.255"} {
		if _, err := Host(host); err == nil {
			test.Fatal("unsafe target accepted", host)
		}
	}
	if err := LocalHost("26.254.253.252"); err == nil {
		test.Fatal("unassigned local VPN address")
	}
}

func TestGatewayPeerRoutesAndHeaders(test *testing.T) {
	calls := 0
	upstream := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		calls++
		for _, name := range []string{"Forwarded", "X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto", "X-DC-Service-ID"} {
			if request.Header.Get(name) != "" {
				test.Error("spoofed header passed", name)
			}
		}
		if request.Header.Get("Authorization") != "Bearer player-session" {
			test.Error("player authentication removed")
		}
		if strings.HasSuffix(request.URL.Path, "/admissions") {
			_, _ = writer.Write([]byte(`{"endpoint":"127.0.0.1:12777"}`))
		} else {
			_, _ = writer.Write([]byte(`{"ready":true}`))
		}
	}))
	defer upstream.Close()
	target, _ := url.Parse(upstream.URL)
	handler, err := gateway("26.1.2.3", []string{"26.2.3.4"}, target)
	if err != nil {
		test.Fatal(err)
	}
	for _, row := range []struct {
		method, path, peer string
		status             int
	}{
		{"GET", "/ready", "26.2.3.4:6000", 200},
		{"GET", "/api/v2/runs/run_VALID_1/results", "26.2.3.4:6000", 200},
		{"POST", "/api/v2/runs/run_VALID_1/admissions", "26.2.3.4:6000", 200},
		{"GET", "/ready", "26.9.9.9:6000", 403},
		{"POST", "/internal/dc/v1/instances/register", "26.2.3.4:6000", 404},
		{"POST", "/api/admin/login", "26.2.3.4:6000", 404},
		{"GET", "/api/players", "26.2.3.4:6000", 404},
		{"POST", "/api/v2/runs/run_VALID_1/results", "26.2.3.4:6000", 404},
		{"GET", "/ws?token=secret", "26.2.3.4:6000", 404},
		{"GET", "/api/v2/runs/run_VALID_1%2Fresults", "26.2.3.4:6000", 404},
	} {
		request := httptest.NewRequest(row.method, row.path, nil)
		request.RemoteAddr = row.peer
		for _, name := range []string{"Forwarded", "X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto", "X-DC-Service-ID"} {
			request.Header.Set(name, "26.2.3.4")
		}
		request.Header.Set("Authorization", "Bearer player-session")
		recorder := httptest.NewRecorder()
		handler.ServeHTTP(recorder, request)
		if recorder.Code != row.status {
			test.Fatalf("%s %s got %d want %d", row.method, row.path, recorder.Code, row.status)
		}
	}
	if calls != 3 {
		test.Fatal("denied request reached upstream", calls)
	}
	if _, err := NewGateway("26.1.2.3", nil); err == nil {
		test.Fatal("empty allow list")
	}
	if _, err := NewGateway("26.1.2.3", []string{"26.2.3.4", "26.2.3.4"}); err == nil {
		test.Fatal("duplicate allow list")
	}
}

func TestGatewayAdmissionMapsOnlyPinnedEndpoint(test *testing.T) {
	for _, endpoint := range []string{"127.0.0.1:12777", "8.8.8.8:12777"} {
		upstream := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
			writer.Header().Set("Content-Type", "application/json")
			_, _ = writer.Write([]byte(`{"endpoint":"` + endpoint + `","ticket":"opaque-test-value","config_hash":"frozen"}`))
		}))
		target, _ := url.Parse(upstream.URL)
		handler, err := gateway("26.1.2.3", []string{"26.2.3.4"}, target)
		if err != nil {
			test.Fatal(err)
		}
		request := httptest.NewRequest("POST", "/api/v2/runs/run_VALID_1/admissions", strings.NewReader("{}"))
		request.RemoteAddr = "26.2.3.4:6000"
		recorder := httptest.NewRecorder()
		handler.ServeHTTP(recorder, request)
		data, _ := io.ReadAll(recorder.Result().Body)
		if endpoint == "127.0.0.1:12777" {
			if recorder.Code != 200 || !strings.Contains(string(data), `"endpoint":"26.1.2.3:12777"`) || !strings.Contains(string(data), "opaque-test-value") {
				test.Fatal("mapping changed credential or endpoint")
			}
		} else if recorder.Code != 502 {
			test.Fatal("unexpected upstream target allowed")
		}
		upstream.Close()
	}
}
