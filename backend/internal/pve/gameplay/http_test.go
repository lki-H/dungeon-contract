package gameplay

import (
	"net/http/httptest"
	"strings"
	"testing"
)

func TestInternalIdentityAndSchemaBoundaries(test *testing.T) {
	content, err := LoadContent(contentPath())
	if err != nil {
		test.Fatal(err)
	}
	service := &Service{Content: content, InstanceID: "dc_local_01", ServiceID: "dc_local_service"}
	token := strings.Repeat("a", 64)
	handler := service.Handler(token)
	for _, scenario := range []struct {
		name, body, credential, identity string
		status                           int
	}{
		{"unauthenticated", `{}`, "", "", 401},
		{"player credential", `{}`, "player-jwt", "dc_local_service", 401},
		{"wrong identity", `{}`, token, "other_service", 401},
		{"duplicate keys", `{"boot_id":"one","boot_id":"two"}`, token, "dc_local_service", 400},
		{"unknown register field", `{"instance_id":"dc_local_01","boot_id":"00000000-0000-4000-8000-000000000001","protocol_version":1,"config_versions":[],"endpoint":"http://localhost/admin"}`, token, "dc_local_service", 400},
		{"oversized body", strings.Repeat("x", 65537), token, "dc_local_service", 400},
	} {
		test.Run(scenario.name, func(subtest *testing.T) {
			request := httptest.NewRequest("POST", "/internal/dc/v1/instances/register", strings.NewReader(scenario.body))
			request.Header.Set("Authorization", "Bearer "+scenario.credential)
			request.Header.Set("X-DC-Service-ID", scenario.identity)
			response := httptest.NewRecorder()
			handler.ServeHTTP(response, request)
			if response.Code != scenario.status {
				subtest.Fatal(response.Code)
			}
			if strings.Contains(response.Body.String(), token) {
				subtest.Fatal("credential leaked")
			}
		})
	}
	request := httptest.NewRequest("POST", "/internal/dc/v1/instances/register", strings.NewReader(`{}`))
	request.Header.Set("Authorization", token)
	request.Header.Set("X-DC-Service-ID", "dc_local_service")
	response := httptest.NewRecorder()
	handler.ServeHTTP(response, request)
	if response.Code != 401 {
		test.Fatal("raw token without Bearer accepted")
	}
}
