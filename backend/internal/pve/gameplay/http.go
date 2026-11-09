package gameplay

import (
	"context"
	"crypto/subtle"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"strconv"
	"strings"
	"time"

	"game-realtime-gm/backend/internal/pve/store"
)

func (service *Service) Handler(token string) http.Handler {
	mux := http.NewServeMux()
	respond := func(writer http.ResponseWriter, value any, err error) {
		writer.Header().Set("Content-Type", "application/json")
		if err != nil {
			status, code := http.StatusBadRequest, 40001
			switch {
			case errors.Is(err, Fence):
				status, code = 409, 40982
			case errors.Is(err, store.Conflict):
				status, code = 409, 40981
			case errors.Is(err, store.Forbidden):
				status, code = 403, 40381
			case errors.Is(err, Expired):
				status, code = 410, 41081
			case errors.Is(err, store.NotFound):
				status, code = 404, 40481
			case !errors.Is(err, store.Invalid):
				status, code = 503, 50381
			}
			writer.WriteHeader(status)
			json.NewEncoder(writer).Encode(map[string]any{"code": code, "message": map[bool]string{true: "CONTROL_FENCE", false: "game service request rejected"}[errors.Is(err, Fence)]})
			return
		}
		json.NewEncoder(writer).Encode(value)
	}
	decode := func(request *http.Request, value any) error {
		data, err := io.ReadAll(io.LimitReader(request.Body, 65537))
		if err != nil || len(data) > 65536 {
			return store.Invalid
		}
		decoded, err := DecodeJSON(data)
		if err != nil {
			return store.Invalid
		}
		pattern := strings.TrimPrefix(request.Pattern, "POST ")
		pattern = strings.ReplaceAll(strings.ReplaceAll(pattern, "{instance}", "{instance_id}"), "{run}", "{run_id}")
		paths := service.Content.OpenAPI["paths"].(map[string]any)
		definition := paths[pattern].(map[string]any)["post"].(map[string]any)["requestBody"].(map[string]any)["content"].(map[string]any)["application/json"].(map[string]any)["schema"].(map[string]any)
		if reference, exists := definition["$ref"].(string); exists {
			definition = service.Content.OpenAPI["components"].(map[string]any)["schemas"].(map[string]any)[strings.TrimPrefix(reference, "#/components/schemas/")].(map[string]any)
		}
		if err := ValidateSchema(decoded, definition); err != nil {
			return store.Invalid
		}
		decoder := json.NewDecoder(strings.NewReader(string(data)))
		decoder.DisallowUnknownFields()
		if decoder.Decode(value) != nil {
			return store.Invalid
		}
		return nil
	}
	mux.HandleFunc("POST /internal/dc/v1/instances/register", func(writer http.ResponseWriter, request *http.Request) {
		var input InstanceRequest
		err := decode(request, &input)
		if err == nil {
			err = service.Register(request.Context(), input)
		}
		var generation int64
		if err == nil {
			err = service.DB.QueryRowContext(request.Context(), "SELECT generation FROM pve_dc_instances WHERE instance_id=?", input.InstanceID).Scan(&generation)
		}
		respond(writer, map[string]any{"registered": err == nil, "instance_id": input.InstanceID, "source_generation": generation}, err)
	})
	mux.HandleFunc("POST /internal/dc/v1/instances/{instance}/heartbeat", func(writer http.ResponseWriter, request *http.Request) {
		var input InstanceRequest
		err := decode(request, &input)
		input.InstanceID = request.PathValue("instance")
		if err == nil {
			err = service.Heartbeat(request.Context(), input)
		}
		respond(writer, map[string]any{"heartbeat": err == nil}, err)
	})
	mux.HandleFunc("POST /internal/dc/v1/instances/{instance}/claim", func(writer http.ResponseWriter, request *http.Request) {
		var input InstanceRequest
		err := decode(request, &input)
		if request.PathValue("instance") != service.InstanceID {
			err = store.Forbidden
		}
		var result *Manifest
		if err == nil {
			result, err = service.Claim(request.Context(), input)
		}
		respond(writer, map[string]any{"assignment": result}, err)
	})
	mux.HandleFunc("POST /internal/dc/v1/admissions/consume", func(writer http.ResponseWriter, request *http.Request) {
		var input ConsumeRequest
		err := decode(request, &input)
		var result map[string]any
		if err == nil {
			result, err = service.Consume(request.Context(), input)
		}
		respond(writer, result, err)
	})
	mux.HandleFunc("POST /internal/dc/v1/runs/{run}/start", func(writer http.ResponseWriter, request *http.Request) {
		var input StartRequest
		err := decode(request, &input)
		var result map[string]any
		if err == nil {
			result, err = service.Start(request.Context(), request.PathValue("run"), input)
		}
		respond(writer, result, err)
	})
	mux.HandleFunc("GET /internal/dc/v1/runs/{run}/commands", func(writer http.ResponseWriter, request *http.Request) {
		after, err := strconv.ParseInt(request.URL.Query().Get("after_control_version"), 10, 64)
		var result map[string]any
		if err == nil {
			result, err = service.Commands(request.Context(), request.PathValue("run"), after)
		} else {
			err = store.Invalid
		}
		respond(writer, result, err)
	})
	mux.HandleFunc("POST /internal/dc/v1/runs/{run}/frames", func(writer http.ResponseWriter, request *http.Request) {
		data, err := io.ReadAll(io.LimitReader(request.Body, 65537))
		var result *Receipt
		if err == nil {
			var frame Frame
			frame, err = service.ParseFrame(data)
			if err == nil && frame.RunID != request.PathValue("run") {
				err = store.Forbidden
			}
			if err == nil {
				result, err = service.Frame(request.Context(), data)
			}
		}
		respond(writer, result, err)
	})
	mux.HandleFunc("POST /internal/dc/v1/runs/{run}/abort", func(writer http.ResponseWriter, request *http.Request) {
		var input struct {
			Operation  string `json:"operation_id"`
			Assignment string `json:"assignment_id"`
			Generation int64  `json:"source_generation"`
			Reason     string `json:"reason"`
		}
		err := decode(request, &input)
		if input.Reason != "technical_failure" || !uuidPattern.MatchString(input.Operation) {
			err = store.Invalid
		}
		if err == nil {
			var assignment string
			if queryErr := service.DB.QueryRowContext(request.Context(), "SELECT assignment_id FROM pve_dc_allocations WHERE run_id=?", request.PathValue("run")).Scan(&assignment); queryErr != nil || assignment != input.Assignment {
				respond(writer, nil, store.Forbidden)
				return
			}
			err = service.Abort(request.Context(), request.PathValue("run"), input.Generation)
		}
		respond(writer, map[string]any{"aborted": err == nil}, err)
	})
	if service.S1() {
		mux.HandleFunc("POST /internal/dc/v1/runs/{run}/presence", func(writer http.ResponseWriter, request *http.Request) {
			var input S1Presence
			err := decode(request, &input)
			if err == nil {
				err = service.PresenceS1(request.Context(), request.PathValue("run"), input)
			}
			respond(writer, map[string]any{"presence": err == nil}, err)
		})
		mux.HandleFunc("POST /internal/dc/v1/runs/{run}/control-acks", func(writer http.ResponseWriter, request *http.Request) {
			var input S1Ack
			err := decode(request, &input)
			var result map[string]any
			if err == nil {
				result, err = service.ClockAck(request.Context(), request.PathValue("run"), input)
			}
			respond(writer, result, err)
		})
	}
	if !service.P3() && (service.Content.Rules.Version == "dc_ruins.v3.p1" || service.S1()) {
		mux.HandleFunc("POST /internal/dc/v1/runs/{run}/p1-result", func(writer http.ResponseWriter, request *http.Request) {
			var input P1Result
			err := decode(request, &input)
			var result map[string]any
			if err == nil {
				result, err = service.FinishP1(request.Context(), request.PathValue("run"), input)
			}
			respond(writer, result, err)
		})
	}
	return http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		authorization := request.Header.Get("Authorization")
		presented := strings.TrimPrefix(authorization, "Bearer ")
		if !strings.HasPrefix(authorization, "Bearer ") || len(token) < 32 || subtle.ConstantTimeCompare([]byte(token), []byte(presented)) != 1 || request.Header.Get("X-DC-Service-ID") != service.ServiceID {
			writer.WriteHeader(http.StatusUnauthorized)
			return
		}
		ctx, cancel := context.WithTimeout(request.Context(), 2*time.Second)
		defer cancel()
		mux.ServeHTTP(writer, request.WithContext(ctx))
	})
}
