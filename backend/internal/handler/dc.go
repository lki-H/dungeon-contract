package handler

import (
	"bytes"
	"encoding/json"
	"io"
	"net/http"

	"game-realtime-gm/backend/internal/auth"
	"game-realtime-gm/backend/internal/pve"
	"game-realtime-gm/backend/internal/pve/gameplay"
	"github.com/gin-gonic/gin"
)

func DemoAdmission(app *pve.App, secret string) gin.HandlerFunc {
	return func(context *gin.Context) {
		if app.Game == nil {
			context.Status(404)
			return
		}
		claims, err := auth.ParseBearerToken(secret, context.GetHeader("Authorization"))
		if err != nil || pve.Authorize(context.Request.Context(), app.DB, claims) != nil {
			context.Status(http.StatusUnauthorized)
			return
		}
		var input struct {
			Operation string                   `json:"operation_id"`
			Versions  *gameplay.VersionBinding `json:"versions,omitempty"`
			Hash      string                   `json:"config_hash,omitempty"`
		}
		data, readErr := io.ReadAll(io.LimitReader(context.Request.Body, 1025))
		_, parseErr := gameplay.DecodeJSON(data)
		decoder := json.NewDecoder(bytes.NewReader(data))
		decoder.DisallowUnknownFields()
		if readErr != nil || len(data) > 1024 || parseErr != nil || decoder.Decode(&input) != nil {
			context.Status(400)
			return
		}
		result, err := app.Game.Issue(context.Request.Context(), gameplay.Admission{Versions: input.Versions, Hash: input.Hash, Operation: input.Operation, Player: claims.PlayerID, Session: claims.SessionVersion, RunID: context.Param("run_id")})
		if err != nil {
			context.JSON(403, gin.H{"code": 40381, "message": "admission unavailable"})
			return
		}
		context.Header("Cache-Control", "no-store")
		context.JSON(200, result)
	}
}
