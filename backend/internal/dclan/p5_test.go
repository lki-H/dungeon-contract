package dclan

import (
	"net/http/httptest"
	"testing"
)

func TestP5LobbyRouteBoundary(test *testing.T) {
	for _, path := range []string{"/api/v2/me/activity", "/api/v2/friends", "/api/v2/friends/requests", "/api/v2/tasks", "/api/v2/parties/party_A/snapshot", "/api/v2/me/run-records?page=1&page_size=5", "/api/v2/trials/dc_ruins_trial/leaderboard?trial_version=trial.v5.1&initial_player_count=2"} {
		if !playerRoute(httptest.NewRequest("GET", path, nil)) {
			test.Fatal("player route denied", path)
		}
		if playerRoute(httptest.NewRequest("POST", path, nil)) {
			test.Fatal("write route exposed", path)
		}
	}
	for _, path := range []string{"/api/v2/trials?page=1&page=2", "/api/v2/trials?token=abc", "/api/v2/friends?redirect=http://example.com", "/api/v2/admin/users", "/internal/dc/v1/runs", "/api/v2/messages", "/ready?page=1", "/api/v2/parties/A%2FB"} {
		if playerRoute(httptest.NewRequest("GET", path, nil)) {
			test.Fatal("forbidden route exposed", path)
		}
	}
}
