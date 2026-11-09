package dclan

import (
	"net/http"
	"net/http/httptest"
	"net/url"
	"strings"
	"testing"
	"time"

	"github.com/gorilla/websocket"
)

func TestGatewayWebSocketUpgradePreservesNativeAuthentication(test *testing.T) {
	upstream := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		if request.Header.Get("Authorization") != "Bearer native-player" || request.Header.Get("X-DC-Service-ID") != "" {
			http.Error(writer, "auth", 401)
			return
		}
		upgrader := websocket.Upgrader{}
		connection, err := upgrader.Upgrade(writer, request, nil)
		if err != nil {
			return
		}
		defer connection.Close()
		_, data, err := connection.ReadMessage()
		if err == nil {
			_ = connection.WriteMessage(websocket.TextMessage, data)
		}
	}))
	defer upstream.Close()
	target, _ := url.Parse(upstream.URL)
	handler, err := gateway("127.0.0.2", []string{"127.0.0.1"}, target)
	if err != nil {
		test.Fatal(err)
	}
	server := httptest.NewServer(handler)
	defer server.Close()
	header := http.Header{}
	header.Set("Authorization", "Bearer native-player")
	header.Set("X-DC-Service-ID", "forged")
	connection, _, err := (&websocket.Dialer{HandshakeTimeout: 3 * time.Second}).Dial(strings.Replace(server.URL, "http://", "ws://", 1)+"/ws", header)
	if err != nil {
		test.Fatal(err)
	}
	defer connection.Close()
	_ = connection.SetReadDeadline(time.Now().Add(3 * time.Second))
	if err = connection.WriteMessage(websocket.TextMessage, []byte("bounded-player-message")); err != nil {
		test.Fatal(err)
	}
	_, data, err := connection.ReadMessage()
	if err != nil || string(data) != "bounded-player-message" {
		test.Fatal("upgrade tunnel", err)
	}
}
