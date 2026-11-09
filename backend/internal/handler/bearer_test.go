package handler

import (
	"context"
	"net/http"
	"net/http/httptest"
	"testing"

	"game-realtime-gm/backend/internal/auth"
	"game-realtime-gm/backend/internal/pve"
	"game-realtime-gm/backend/internal/pve/gameplay"
	"github.com/gin-gonic/gin"
)

func TestV2HandlersRejectRawJWTBeforeDatabaseAccess(test *testing.T) {
	secret := "test-only-bearer-header-secret"
	token, err := auth.GenerateVersionedToken(secret, 101, "audit", 1)
	if err != nil {
		test.Fatal(err)
	}
	app := &pve.App{Game: &gameplay.Service{}}
	transport := &V2Transport{App: app, Secret: secret}
	for _, scenario := range []struct {
		name    string
		handler gin.HandlerFunc
	}{
		{"query", transport.Auth},
		{"admission", DemoAdmission(app, secret)},
		{"websocket header", transport.WebSocket},
	} {
		test.Run(scenario.name, func(test *testing.T) {
			router := gin.New()
			router.GET("/", scenario.handler)
			request := httptest.NewRequest(http.MethodGet, "/", nil).WithContext(context.Background())
			request.Header.Set("Authorization", token)
			response := httptest.NewRecorder()
			router.ServeHTTP(response, request)
			if response.Code != http.StatusUnauthorized {
				test.Fatalf("raw JWT accepted: status=%d", response.Code)
			}
		})
	}
}
