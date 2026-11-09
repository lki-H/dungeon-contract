package gameplay

import (
	"context"
	"crypto/hmac"
	"crypto/rand"
	"crypto/sha256"
	"database/sql"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"strconv"
	"time"

	"game-realtime-gm/backend/internal/pve/run"
	"game-realtime-gm/backend/internal/pve/store"
	"game-realtime-gm/backend/internal/pve/v3"
)

var Fence = errors.New("CONTROL_FENCE")
var Expired = errors.New("expired assignment or admission")

type Service struct {
	Engineering    bool
	CapacityFailed func(context.Context, string) error
	DB             *sql.DB
	Content        *Content
	Runs           *run.Service
	Key            []byte
	ServiceID      string
	InstanceID     string
	Now            func() time.Time
}
type Member struct {
	PlayerID  string `json:"player_id"`
	Slot      int    `json:"slot"`
	LoadoutID string `json:"loadout_id"`
}
type Manifest struct {
	Versions     *VersionBinding             `json:"versions,omitempty"`
	World        map[string]any              `json:"p1_world"`
	WorldHash    string                      `json:"p1_world_hash"`
	Requirements map[string]map[string]int64 `json:"task_requirements"`
	RunID        string                      `json:"run_id"`
	AssignmentID string                      `json:"assignment_id"`
	InstanceID   string                      `json:"instance_id"`
	BootID       string                      `json:"boot_id"`
	Generation   int64                       `json:"source_generation"`
	ConfigHash   string                      `json:"config_hash"`
	Operation    string                      `json:"operation_version"`
	Mode         string                      `json:"mode"`
	TrialVersion string                      `json:"trial_version"`
	Seed         int64                       `json:"seed"`
	Members      []Member                    `json:"members"`
	Config       map[string]any              `json:"config"`
}
type Allocation struct {
	Engineering    bool             `json:"engineering,omitempty"`
	Clock          *S1Clock         `json:"s1_clock,omitempty"`
	P1Result       *P1Result        `json:"p1_stage_result,omitempty"`
	Manifest       Manifest         `json:"manifest"`
	Status         string           `json:"status"`
	Control        int64            `json:"control_version"`
	AppliedControl int64            `json:"applied_control_version"`
	LastWorldFrame int64            `json:"last_world_frame"`
	Elapsed        int64            `json:"elapsed_ms"`
	Facts          int              `json:"fact_count"`
	GapAt          *time.Time       `json:"gap_at,omitempty"`
	StartedAt      *time.Time       `json:"started_at,omitempty"`
	TrialEligible  bool             `json:"trial_eligible"`
	Stats          map[string]Stats `json:"stats"`
	Connections    map[string]int64 `json:"connections"`
	P3FinalSummary bool             `json:"p3_final_summary,omitempty"`
}
type InstanceRequest struct {
	Versions        *VersionBinding `json:"versions,omitempty"`
	InstanceID      string          `json:"instance_id"`
	BootID          string          `json:"boot_id"`
	ProtocolVersion int             `json:"protocol_version"`
	ConfigVersions  []ConfigVersion `json:"config_versions"`
	AssignmentID    string          `json:"assignment_id"`
	Generation      int64           `json:"source_generation"`
	Status          string          `json:"instance_status"`
	LastSequence    int64           `json:"last_applied_sequence"`
}
type ConfigVersion struct {
	ID   string `json:"definition_id"`
	Hash string `json:"hash"`
}

func (service *Service) Register(ctx context.Context, input InstanceRequest) error {
	if !input.Versions.validFor(service.Content) {
		return store.Forbidden
	}
	if input.InstanceID != service.InstanceID || !uuidPattern.MatchString(input.BootID) || input.ProtocolVersion != service.Content.ProtocolVersion() || len(input.ConfigVersions) != 1 || input.ConfigVersions[0].ID != service.Content.Rules.Version || input.ConfigVersions[0].Hash != service.Content.Hash {
		return store.Invalid
	}
	return store.Transaction(ctx, service.DB, func(transaction *sql.Tx) error {
		var boot string
		var assignment sql.NullString
		var generation int64
		err := transaction.QueryRowContext(ctx, "SELECT boot_id,current_assignment_id,generation FROM pve_dc_instances WHERE instance_id=? FOR UPDATE", input.InstanceID).Scan(&boot, &assignment, &generation)
		if errors.Is(err, sql.ErrNoRows) {
			_, err = transaction.ExecContext(ctx, "INSERT INTO pve_dc_instances(instance_id,service_id,boot_id,generation,status,lease_until,endpoint_key) VALUES(?,?,?,1,'idle',?,'loopback')", input.InstanceID, service.ServiceID, input.BootID, service.Now().UTC().Add(time.Duration(service.Content.Millis("lease_ms"))*time.Millisecond))
			return err
		}
		if err != nil {
			return err
		}
		if boot != input.BootID {
			if assignment.Valid {
				var id, runStatus string
				if err := transaction.QueryRowContext(ctx, "SELECT a.run_id,r.status FROM pve_dc_allocations a JOIN pve_runs r ON r.id=a.run_id WHERE a.assignment_id=?", assignment.String).Scan(&id, &runStatus); err != nil {
					return err
				}
				if runStatus != "closed" && runStatus != "aborted" {
					state, allocation, err := service.load(ctx, transaction, id)
					if err != nil {
						return err
					}
					if err := service.end(ctx, transaction, state, allocation, "system_abort"); err != nil {
						return err
					}
				}
			}
			generation++
			_, err = transaction.ExecContext(ctx, "UPDATE pve_dc_instances SET boot_id=?,generation=?,status='idle',current_assignment_id=NULL,reserved_proposal_id=NULL,reserve_until=NULL,last_sequence=0 WHERE instance_id=?", input.BootID, generation, input.InstanceID)
			if err != nil {
				return err
			}
		}
		_, err = transaction.ExecContext(ctx, "UPDATE pve_dc_instances SET lease_until=? WHERE instance_id=?", service.Now().UTC().Add(time.Duration(service.Content.Millis("lease_ms"))*time.Millisecond), input.InstanceID)
		return err
	})
}
func (service *Service) Heartbeat(ctx context.Context, input InstanceRequest) error {
	if !input.Versions.validFor(service.Content) {
		return store.Forbidden
	}
	if input.InstanceID != service.InstanceID || !uuidPattern.MatchString(input.BootID) {
		return store.Invalid
	}
	return store.Transaction(ctx, service.DB, func(transaction *sql.Tx) error {
		var boot string
		var assignment sql.NullString
		var generation int64
		var previousLease time.Time
		if err := transaction.QueryRowContext(ctx, "SELECT boot_id,current_assignment_id,generation FROM pve_dc_instances WHERE instance_id=? FOR UPDATE", input.InstanceID).Scan(&boot, &assignment, &generation); err != nil {
			return err
		}
		if boot != input.BootID || assignment.String != input.AssignmentID || (assignment.Valid && input.Generation != generation) {
			return store.Forbidden
		}
		if err := transaction.QueryRowContext(ctx, "SELECT lease_until FROM pve_dc_instances WHERE instance_id=?", input.InstanceID).Scan(&previousLease); err != nil {
			return err
		}
		if assignment.Valid && !service.Now().Before(previousLease) {
			var id string
			if err := transaction.QueryRowContext(ctx, "SELECT run_id FROM pve_dc_allocations WHERE assignment_id=?", assignment.String).Scan(&id); err != nil {
				return err
			}
			state, allocation, err := service.load(ctx, transaction, id)
			if err != nil {
				return err
			}
			allocation.TrialEligible = false
			if service.Now().Sub(previousLease) >= time.Duration(service.Content.Millis("technical_wait_ms"))*time.Millisecond {
				return service.end(ctx, transaction, state, allocation, "system_abort")
			}
			if err := service.save(ctx, transaction, state, allocation); err != nil {
				return err
			}
		}
		_, err := transaction.ExecContext(ctx, "UPDATE pve_dc_instances SET lease_until=?,last_sequence=? WHERE instance_id=?", service.Now().UTC().Add(time.Duration(service.Content.Millis("lease_ms"))*time.Millisecond), input.LastSequence, input.InstanceID)
		return err
	})
}
func (service *Service) ReserveTx(ctx context.Context, transaction *sql.Tx, proposal, operation string) (bool, error) {
	if operation != service.Content.Rules.Operation {
		return true, nil
	}
	result, err := transaction.ExecContext(ctx, "UPDATE pve_dc_instances SET status='reserved',reserved_proposal_id=?,reserve_until=? WHERE instance_id=? AND status='idle' AND lease_until>?", proposal, service.Now().UTC().Add(20*time.Second), service.InstanceID, service.Now().UTC())
	if err != nil {
		return false, err
	}
	rows, err := result.RowsAffected()
	return rows == 1, err
}
func (service *Service) ReleaseTx(ctx context.Context, transaction *sql.Tx, proposal string) error {
	_, err := transaction.ExecContext(ctx, "UPDATE pve_dc_instances SET status='idle',reserved_proposal_id=NULL,reserve_until=NULL WHERE reserved_proposal_id=? AND current_assignment_id IS NULL", proposal)
	return err
}
func (service *Service) AllocateTx(ctx context.Context, transaction *sql.Tx, state *run.State, request run.CreateRequest) error {
	if state.OperationName != service.Content.Rules.Operation {
		return nil
	}
	if state.Rules.Version != service.Content.Rules.Version {
		return store.Forbidden
	}
	if len(state.Participants) < 1 || len(state.Participants) > service.StagePlayerLimit() {
		return store.Invalid
	}
	var boot, proposal string
	var generation int64
	var lease, reserve time.Time
	if err := transaction.QueryRowContext(ctx, "SELECT boot_id,generation,reserved_proposal_id,lease_until,reserve_until FROM pve_dc_instances WHERE instance_id=? AND status='reserved' FOR UPDATE", service.InstanceID).Scan(&boot, &generation, &proposal, &lease, &reserve); err != nil {
		return err
	}
	if proposal != request.ProposalID || !service.Now().Before(lease) || !service.Now().Before(reserve) {
		return Expired
	}
	mode, trial := "normal", ""
	if err := transaction.QueryRowContext(ctx, "SELECT mode,trial_version FROM pve_dc_ticket_preferences WHERE ticket_id=?", request.Tickets[state.Participants[0].PlayerID]).Scan(&mode, &trial); err != nil {
		return err
	}
	seedBytes := make([]byte, 4)
	if _, err := rand.Read(seedBytes); err != nil {
		return err
	}
	seed := int64(seedBytes[0])<<24 | int64(seedBytes[1])<<16 | int64(seedBytes[2])<<8 | int64(seedBytes[3])
	if mode == "trial" {
		seed = integer(service.Content.Section("trial"), "seed")
	}
	manifest := Manifest{RunID: state.ID, AssignmentID: store.ID("assign"), InstanceID: service.InstanceID, BootID: boot, Generation: generation, ConfigHash: service.Content.Hash, Operation: state.Rules.Version, Mode: mode, TrialVersion: trial, Seed: seed, Config: service.Content.Document}
	manifest.Versions = service.Content.V3
	if service.Content.V3 != nil {
		manifest.Operation = service.Content.V3.Operation
	}
	allocation := &Allocation{Manifest: manifest, Status: "loading", TrialEligible: !service.Engineering, Engineering: service.Engineering, Stats: map[string]Stats{}, Connections: map[string]int64{}}
	for index, member := range state.Participants {
		loadout := request.Tasks[member.PlayerID].LoadoutID
		if loadout == "" {
			loadout = "ranger"
		}
		if loadout != "ranger" && loadout != "runesmith" {
			return store.Invalid
		}
		id := strconv.FormatInt(member.PlayerID, 10)
		allocation.Manifest.Members = append(allocation.Manifest.Members, Member{PlayerID: id, Slot: index, LoadoutID: loadout})
		allocation.Stats[id] = Stats{PlayerID: id, Slot: index, LoadoutID: loadout, Choices: []string{}}
	}
	state.Authority = "unity"
	allocation.Manifest.World = service.Content.World
	allocation.Manifest.WorldHash = service.Content.WorldHash
	if service.P4() {
		generated, generatedHash, generationErr := service.Content.P4World(seed)
		if generationErr != nil {
			return generationErr
		}
		allocation.Manifest.World, allocation.Manifest.WorldHash = generated, generatedHash
	}
	allocation.Manifest.Requirements = service.receipt(state, allocation).Requirements
	state.SourceGeneration = generation
	for index := range state.Participants {
		state.Participants[index].Connected = false
		state.Participants[index].ConnectionID = ""
		state.Participants[index].DisconnectedAt = nil
	}
	if _, err := transaction.ExecContext(ctx, "INSERT INTO pve_dc_allocations(assignment_id,run_id,instance_id,boot_id,source_generation,status,config_hash,manifest,state) VALUES(?,?,?,?,?,'loading',?,?,?)", allocation.Manifest.AssignmentID, state.ID, service.InstanceID, boot, generation, service.Content.Hash, store.JSON(allocation.Manifest), store.JSON(allocation)); err != nil {
		return err
	}
	if _, err := transaction.ExecContext(ctx, "UPDATE pve_dc_instances SET status='loading',current_assignment_id=?,reserved_proposal_id=NULL,reserve_until=NULL WHERE instance_id=?", allocation.Manifest.AssignmentID, service.InstanceID); err != nil {
		return err
	}
	return run.Save(ctx, transaction, state)
}
func (service *Service) load(ctx context.Context, transaction *sql.Tx, id string) (*run.State, *Allocation, error) {
	state, err := run.Load(ctx, transaction, id)
	if err != nil {
		return nil, nil, err
	}
	var data []byte
	if err := transaction.QueryRowContext(ctx, "SELECT state FROM pve_dc_allocations WHERE run_id=? FOR UPDATE", id).Scan(&data); err != nil {
		return nil, nil, err
	}
	var allocation Allocation
	if err := json.Unmarshal(data, &allocation); err != nil {
		return nil, nil, err
	}
	if service.Content.V3 != nil && (!allocation.Manifest.Versions.validFor(service.Content) || allocation.Manifest.ConfigHash != service.Content.Hash || state.Rules.Version != service.Content.Rules.Version) {
		return nil, nil, store.Forbidden
	}
	return state, &allocation, nil
}
func (service *Service) save(ctx context.Context, transaction *sql.Tx, state *run.State, allocation *Allocation) error {
	if service.S1() && allocation.StartedAt != nil && allocation.Clock == nil {
		allocation.Clock = &S1Clock{State: "running", Revision: 1}
	}
	if err := run.Save(ctx, transaction, state); err != nil {
		return err
	}
	_, err := transaction.ExecContext(ctx, "UPDATE pve_dc_allocations SET status=?,state=?,control_issued=?,control_applied=?,started_at=?,deadline=?,trial_eligible=? WHERE run_id=?", allocation.Status, store.JSON(allocation), allocation.Control, allocation.AppliedControl, allocation.StartedAt, state.DeadlineAt, allocation.TrialEligible, state.ID)
	if err == nil && service.S1() && allocation.StartedAt != nil {
		return service.saveS1Clock(ctx, transaction, state, allocation)
	}
	if err == nil && service.Content.V3 != nil && allocation.StartedAt != nil {
		_, err = transaction.ExecContext(ctx, "INSERT INTO pve_dc_v3_run_clocks(run_id,operation_version,protocol_version,mode,initial_player_count,clock_state,game_elapsed_ms,wall_elapsed_ms,manual_pause_ms,clock_revision,updated_at) VALUES(?,?,3,?,?,'running',?,?,0,?,?) ON DUPLICATE KEY UPDATE game_elapsed_ms=VALUES(game_elapsed_ms),wall_elapsed_ms=VALUES(wall_elapsed_ms),clock_revision=VALUES(clock_revision),updated_at=VALUES(updated_at)", state.ID, v3.OperationVersion, allocation.Manifest.Mode, len(state.Participants), allocation.Elapsed, allocation.Elapsed, state.AppliedSequence, service.Now().UTC())
	}
	return err
}
func (service *Service) bound(ctx context.Context, transaction *sql.Tx, allocation *Allocation) error {
	var boot string
	var assignment sql.NullString
	var lease time.Time
	if err := transaction.QueryRowContext(ctx, "SELECT boot_id,current_assignment_id,lease_until FROM pve_dc_instances WHERE instance_id=?", allocation.Manifest.InstanceID).Scan(&boot, &assignment, &lease); err != nil {
		return err
	}
	if boot != allocation.Manifest.BootID || assignment.String != allocation.Manifest.AssignmentID || !service.Now().Before(lease) {
		return Expired
	}
	return nil
}
func (service *Service) Claim(ctx context.Context, input InstanceRequest) (*Manifest, error) {
	if !input.Versions.validFor(service.Content) {
		return nil, store.Forbidden
	}
	var manifest *Manifest
	err := store.Transaction(ctx, service.DB, func(transaction *sql.Tx) error {
		var boot string
		var assignment sql.NullString
		var lease time.Time
		if err := transaction.QueryRowContext(ctx, "SELECT boot_id,current_assignment_id,lease_until FROM pve_dc_instances WHERE instance_id=? FOR UPDATE", service.InstanceID).Scan(&boot, &assignment, &lease); err != nil {
			return err
		}
		if boot != input.BootID || !service.Now().Before(lease) {
			return Expired
		}
		if !assignment.Valid {
			return nil
		}
		var data []byte
		if err := transaction.QueryRowContext(ctx, "SELECT manifest FROM pve_dc_allocations WHERE assignment_id=?", assignment.String).Scan(&data); err != nil {
			return err
		}
		var result Manifest
		if err := json.Unmarshal(data, &result); err != nil {
			return err
		}
		if service.Content.V3 != nil && (!result.Versions.validFor(service.Content) || result.ConfigHash != service.Content.Hash) {
			return store.Forbidden
		}
		manifest = &result
		return nil
	})
	return manifest, err
}

type Admission struct {
	Versions  *VersionBinding
	Hash      string
	Operation string
	Player    int64
	Session   int64
	RunID     string
}
type AdmissionResult struct {
	Versions   *VersionBinding `json:"versions,omitempty"`
	Ticket     string          `json:"ticket"`
	Expires    time.Time       `json:"expires_at"`
	Endpoint   string          `json:"endpoint"`
	Instance   string          `json:"instance_id"`
	Generation int64           `json:"source_generation"`
	Connection int64           `json:"connection_generation"`
	Hash       string          `json:"config_hash"`
}

func (service *Service) ticket(nonce string, input Admission, allocation *Allocation, connection int64) string {
	binding := map[string]any{"nonce": nonce, "key_version": 1, "player_id": strconv.FormatInt(input.Player, 10), "run_id": input.RunID, "instance_id": allocation.Manifest.InstanceID, "source_generation": allocation.Manifest.Generation, "session_version": input.Session, "connection_generation": connection}
	if allocation.Manifest.Versions != nil {
		binding["versions"] = allocation.Manifest.Versions
		binding["config_hash"] = allocation.Manifest.ConfigHash
		binding["assignment_id"] = allocation.Manifest.AssignmentID
		binding["boot_id"] = allocation.Manifest.BootID
	}
	data, _ := DecodeJSON(store.JSON(binding))
	mac := hmac.New(sha256.New, service.Key)
	mac.Write(Canonical(data))
	return base64.RawURLEncoding.EncodeToString(mac.Sum(nil))
}
func (service *Service) Issue(ctx context.Context, input Admission) (*AdmissionResult, error) {
	if !input.Versions.validFor(service.Content) || service.Content.V3 != nil && input.Hash != service.Content.Hash {
		return nil, store.Forbidden
	}
	if !uuidPattern.MatchString(input.Operation) {
		return nil, store.Invalid
	}
	var result *AdmissionResult
	err := store.Transaction(ctx, service.DB, func(transaction *sql.Tx) error {
		state, allocation, err := service.load(ctx, transaction, input.RunID)
		if err != nil {
			return err
		}
		member := state.Member(&input.Player)
		if member == nil || member.Status == "left" || (state.Status != "loading" && state.Status != "running") {
			return store.Forbidden
		}
		if err := checkPlayer(ctx, transaction, input.Player, input.Session); err != nil {
			return err
		}
		if err := service.bound(ctx, transaction, allocation); err != nil {
			return err
		}
		var nonce, status, id string
		var connection int64
		var expiry time.Time
		err = transaction.QueryRowContext(ctx, "SELECT run_id,nonce,connection_generation,expires_at,status FROM pve_dc_admissions WHERE player_id=? AND operation_id=?", input.Player, input.Operation).Scan(&id, &nonce, &connection, &expiry, &status)
		if err == nil {
			if id != input.RunID {
				return store.Conflict
			}
			if status != "issued" || !service.Now().Before(expiry) {
				return Expired
			}
		} else if errors.Is(err, sql.ErrNoRows) {
			if err := transaction.QueryRowContext(ctx, "SELECT COALESCE(MAX(connection_generation),0)+1 FROM pve_dc_admissions WHERE player_id=? AND run_id=?", input.Player, input.RunID).Scan(&connection); err != nil {
				return err
			}
			if service.Content.V3 != nil && (connection < 1 || connection > 4294967295) {
				return store.Invalid
			}
			random := make([]byte, 32)
			if _, err := rand.Read(random); err != nil {
				return err
			}
			nonce = base64.RawURLEncoding.EncodeToString(random)
			expiry = service.Now().UTC().Add(time.Duration(service.Content.Millis("admission_ttl_ms")) * time.Millisecond)
			if _, err := transaction.ExecContext(ctx, "UPDATE pve_dc_admissions SET status='superseded' WHERE run_id=? AND player_id=? AND status='issued'", input.RunID, input.Player); err != nil {
				return err
			}
			ticket := service.ticket(nonce, input, allocation, connection)
			if _, err := transaction.ExecContext(ctx, "INSERT INTO pve_dc_admissions(admission_id,operation_id,player_id,run_id,assignment_id,connection_generation,session_version,token_hash,nonce,key_version,expires_at,status) VALUES(?,?,?,?,?,?,?,?,?,1,?,'issued')", store.ID("admit"), input.Operation, input.Player, input.RunID, allocation.Manifest.AssignmentID, connection, input.Session, store.Hash([]byte(ticket)), nonce, expiry); err != nil {
				return err
			}
		} else {
			return err
		}
		result = &AdmissionResult{Versions: allocation.Manifest.Versions, Ticket: service.ticket(nonce, input, allocation, connection), Expires: expiry, Endpoint: "127.0.0.1:12777", Instance: allocation.Manifest.InstanceID, Generation: allocation.Manifest.Generation, Connection: connection, Hash: allocation.Manifest.ConfigHash}
		return nil
	})
	return result, err
}
func checkPlayer(ctx context.Context, transaction *sql.Tx, player, session int64) error {
	var status string
	if err := transaction.QueryRowContext(ctx, "SELECT status FROM players WHERE id=?", player).Scan(&status); err != nil {
		return err
	}
	if status != "normal" {
		return store.Forbidden
	}
	var revoked time.Time
	err := transaction.QueryRowContext(ctx, "SELECT revoked_through FROM pve_session_revocations WHERE player_id=?", player).Scan(&revoked)
	if errors.Is(err, sql.ErrNoRows) {
		if session != 0 {
			return store.Forbidden
		}
		return nil
	}
	if err != nil {
		return err
	}
	if revoked.UnixMilli() != session {
		return store.Forbidden
	}
	return nil
}

type ConsumeRequest struct {
	Versions *VersionBinding `json:"versions,omitempty"`
	Hash     string          `json:"config_hash,omitempty"`
	Ticket   string          `json:"ticket"`
	Nonce    string          `json:"client_nonce"`
	Instance string          `json:"instance_id"`
	Boot     string          `json:"boot_id"`
}

func (service *Service) Consume(ctx context.Context, input ConsumeRequest) (map[string]any, error) {
	if !input.Versions.validFor(service.Content) || service.Content.V3 != nil && input.Hash != service.Content.Hash {
		return nil, store.Forbidden
	}
	if len(input.Ticket) != 43 || !uuidPattern.MatchString(input.Nonce) || input.Instance != service.InstanceID {
		return nil, store.Invalid
	}
	var result map[string]any
	err := store.Transaction(ctx, service.DB, func(transaction *sql.Tx) error {
		var id, runID, assignment, status string
		var consumed sql.NullString
		var player, connection, session int64
		var expiry time.Time
		if err := transaction.QueryRowContext(ctx, "SELECT admission_id,run_id,assignment_id,player_id,connection_generation,session_version,expires_at,status,consumed_nonce FROM pve_dc_admissions WHERE token_hash=?", store.Hash([]byte(input.Ticket))).Scan(&id, &runID, &assignment, &player, &connection, &session, &expiry, &status, &consumed); err != nil {
			return store.Forbidden
		}
		state, allocation, err := service.load(ctx, transaction, runID)
		if err != nil {
			return err
		}
		if input.Boot != allocation.Manifest.BootID || assignment != allocation.Manifest.AssignmentID || !service.Now().Before(expiry) {
			return Expired
		}
		if service.Content.V3 != nil {
			binding := allocation.Manifest.Versions
			expected := service.Content.V3
			if err := v3.ValidateAdmissionAgainst(v3.Admission{OperationVersion: binding.Operation, ProtocolVersion: binding.Protocol, ContentVersion: binding.Content, WorldVersion: binding.World, TrialVersion: allocation.Manifest.TrialVersion, ConfigHash: allocation.Manifest.ConfigHash, Mode: allocation.Manifest.Mode, InitialPlayers: len(allocation.Manifest.Members), PlayerID: strconv.FormatInt(player, 10), RunID: runID, ConnectionGen: connection}, v3.Admission{OperationVersion: expected.Operation, ProtocolVersion: expected.Protocol, ContentVersion: expected.Content, WorldVersion: expected.World, TrialVersion: text(service.Content.Section("trial"), "version"), ConfigHash: service.Content.Hash}); err != nil {
				return store.Forbidden
			}
		}
		if err := service.bound(ctx, transaction, allocation); err != nil {
			return err
		}
		if err := checkPlayer(ctx, transaction, player, session); err != nil {
			return err
		}
		member := state.Member(&player)
		if member == nil || member.Status == "left" || (state.Status != "loading" && state.Status != "running") {
			return store.Forbidden
		}
		if status == "consumed" {
			if consumed.String != input.Nonce {
				return store.Conflict
			}
		} else if status == "issued" {
			if _, err := transaction.ExecContext(ctx, "UPDATE pve_dc_admissions SET status='consumed',consumed_nonce=? WHERE admission_id=?", input.Nonce, id); err != nil {
				return err
			}
		} else {
			return Expired
		}
		playerText := strconv.FormatInt(player, 10)
		if prior := allocation.Connections[playerText]; prior > connection {
			return Expired
		}
		allocation.Connections[playerText] = connection
		member.Connected = true
		member.DisconnectedAt = nil
		slot := 0
		for _, candidate := range allocation.Manifest.Members {
			if candidate.PlayerID == playerText {
				slot = candidate.Slot
			}
		}
		result = map[string]any{"player_id": playerText, "slot": slot, "connection_generation": connection, "run_id": runID, "assignment_id": assignment, "generation": allocation.Manifest.Generation, "config_hash": allocation.Manifest.ConfigHash}
		if allocation.Manifest.Versions != nil {
			result["versions"] = allocation.Manifest.Versions
		}
		return service.save(ctx, transaction, state, allocation)
	})
	return result, err
}

type StartRequest struct {
	Versions   *VersionBinding `json:"versions,omitempty"`
	ReadyOnly  bool            `json:"ready_only,omitempty"`
	Assignment string          `json:"assignment_id"`
	Generation int64           `json:"source_generation"`
	Players    []LoadedMember  `json:"loaded_members"`
	Hash       string          `json:"config_hash"`
}
type LoadedMember struct {
	Player     string `json:"player_id"`
	Slot       int    `json:"slot"`
	Connection int64  `json:"connection_generation"`
}

func (service *Service) Start(ctx context.Context, id string, input StartRequest) (map[string]any, error) {
	if !input.Versions.validFor(service.Content) {
		return nil, store.Forbidden
	}
	var result map[string]any
	err := store.Transaction(ctx, service.DB, func(transaction *sql.Tx) error {
		state, allocation, err := service.load(ctx, transaction, id)
		if err != nil {
			return err
		}
		if err := service.bound(ctx, transaction, allocation); err != nil {
			return err
		}
		if input.Assignment != allocation.Manifest.AssignmentID || input.Generation != allocation.Manifest.Generation || input.Hash != allocation.Manifest.ConfigHash {
			return store.Forbidden
		}
		if state.Status == "running" {
			result = map[string]any{"start_operation": "start:" + id, "run_id": id}
			return nil
		}
		if state.Status != "loading" || !service.Now().Before(state.DeadlineAt) || len(input.Players) > len(state.Participants) || (!input.ReadyOnly && len(input.Players) != len(state.Participants)) {
			return store.Conflict
		}
		seen := map[string]bool{}
		for _, loaded := range input.Players {
			player := loaded.Player
			if seen[player] || allocation.Connections[player] == 0 || allocation.Connections[player] != loaded.Connection || loaded.Slot < 0 || loaded.Slot >= len(allocation.Manifest.Members) || allocation.Manifest.Members[loaded.Slot].PlayerID != player {
				return store.Forbidden
			}
			seen[player] = true
		}
		if input.ReadyOnly {
			for index := range state.Participants {
				member := &state.Participants[index]
				member.Status = "loading"
				if seen[strconv.FormatInt(member.PlayerID, 10)] {
					member.Status = "participating"
				}
			}
			result = map[string]any{"ready": true, "run_id": id}
			return service.save(ctx, transaction, state, allocation)
		}
		now := service.Now().UTC()
		for index := range state.Participants {
			state.Participants[index].Status = "participating"
		}
		state.Status = "running"
		state.StartedAt = &now
		state.DeadlineAt = now.Add(time.Duration(service.Content.Rules.Duration) * time.Second)
		allocation.Status = "running"
		allocation.StartedAt = &now
		result = map[string]any{"start_operation": "start:" + id, "run_id": id}
		if _, err := transaction.ExecContext(ctx, "UPDATE pve_dc_instances SET status='running' WHERE instance_id=?", service.InstanceID); err != nil {
			return err
		}
		return service.save(ctx, transaction, state, allocation)
	})
	return result, err
}

func (service *Service) LeaveTx(ctx context.Context, transaction *sql.Tx, state *run.State, player int64, operation string) error {
	if state.Authority != "unity" {
		return run.LeaveTx(ctx, transaction, state, player, service.Now().UTC())
	}
	member := state.Member(&player)
	if member == nil {
		return store.Forbidden
	}
	if state.Status == "ending" || state.Status == "closed" || member.Status == "left" {
		return nil
	}
	if state.Status != "running" {
		return store.Conflict
	}
	_, allocation, err := service.load(ctx, transaction, state.ID)
	if err != nil {
		return err
	}
	var prior int
	if err := transaction.QueryRowContext(ctx, "SELECT COUNT(*) FROM pve_dc_control_commands WHERE run_id=? AND operation_id=?", state.ID, operation).Scan(&prior); err != nil {
		return err
	}
	if prior > 0 {
		return nil
	}
	allocation.Control++
	if service.S1() && allocation.Clock != nil && allocation.Clock.State == "paused" {
		allocation.Control--
		if err := service.forceResumeS1(ctx, transaction, state, allocation, player); err != nil {
			return err
		}
		allocation.Control++
	}
	if _, err := transaction.ExecContext(ctx, "DELETE FROM pve_dc_frame_receipts WHERE run_id=? AND status='received'", state.ID); err != nil {
		return err
	}
	allocation.GapAt = nil
	if _, err := transaction.ExecContext(ctx, "INSERT INTO pve_dc_control_commands(run_id,control_version,operation_id,command_type,player_id,payload,status) VALUES(?,?,?,'leave',?,?,'issued')", state.ID, allocation.Control, operation, player, store.JSON(map[string]any{"player_id": strconv.FormatInt(player, 10)})); err != nil {
		return err
	}
	return service.save(ctx, transaction, state, allocation)
}
func (service *Service) Commands(ctx context.Context, id string, after int64) (map[string]any, error) {
	var result map[string]any
	err := store.Transaction(ctx, service.DB, func(transaction *sql.Tx) error {
		_, allocation, err := service.load(ctx, transaction, id)
		if err != nil {
			return err
		}
		if err := service.bound(ctx, transaction, allocation); err != nil {
			return err
		}
		if after < 0 || after > allocation.Control {
			return store.Invalid
		}
		rows, err := transaction.QueryContext(ctx, "SELECT control_version,command_type,operation_id,payload FROM pve_dc_control_commands WHERE run_id=? AND control_version>? ORDER BY control_version LIMIT 32", id, after)
		if err != nil {
			return err
		}
		defer rows.Close()
		items := []map[string]any{}
		for rows.Next() {
			var version int64
			var operation string
			var kind string
			var payload json.RawMessage
			if err := rows.Scan(&version, &kind, &operation, &payload); err != nil {
				return err
			}
			items = append(items, map[string]any{"control_version": version, "operation_id": operation, "type": kind, "payload": payload})
		}
		result = map[string]any{"commands": items, "control_version": allocation.Control}
		return rows.Err()
	})
	return result, err
}
func (service *Service) end(ctx context.Context, transaction *sql.Tx, state *run.State, allocation *Allocation, reason string) error {
	if state.Status == "closed" || state.Status == "ending" || state.Status == "aborted" {
		return nil
	}
	if service.S1() && allocation.Clock != nil && allocation.Clock.State == "paused" && allocation.Clock.PauseAt != nil {
		wall := service.Now().Sub(*allocation.StartedAt).Milliseconds()
		allocation.Clock.ManualPause += service.Now().Sub(*allocation.Clock.PauseAt).Milliseconds()
		if _, err := transaction.ExecContext(ctx, "UPDATE pve_dc_v4_pause_intervals SET end_wall_ms=?,status='aborted' WHERE run_id=? AND pause_id=? AND status='open'", wall, state.ID, allocation.Clock.PauseID); err != nil {
			return err
		}
		allocation.Clock.State = "running"
		allocation.Clock.PauseID = ""
		allocation.Clock.PauseAt = nil
		allocation.Clock.Revision++
	}
	run.EndAuthority(state, reason, service.Now().UTC())
	if service.P3() {
		if err := service.persistP3Prefix(ctx, transaction, state, allocation); err != nil {
			return err
		}
	}
	allocation.Status = "draining"
	allocation.TrialEligible = false
	if err := run.QueueSettlement(ctx, transaction, state); err != nil {
		return err
	}
	return service.save(ctx, transaction, state, allocation)
}
func (service *Service) Abort(ctx context.Context, id string, generation int64) error {
	return store.Transaction(ctx, service.DB, func(transaction *sql.Tx) error {
		state, allocation, err := service.load(ctx, transaction, id)
		if err != nil {
			return err
		}
		if generation != allocation.Manifest.Generation {
			return store.Forbidden
		}
		return service.end(ctx, transaction, state, allocation, "system_abort")
	})
}
func (service *Service) Tick(ctx context.Context) error {
	if err := store.Transaction(ctx, service.DB, func(transaction *sql.Tx) error {
		if _, err := transaction.ExecContext(ctx, "UPDATE pve_dc_allocations a JOIN pve_runs r ON r.id=a.run_id SET a.status='closed' WHERE a.status IN ('loading','running','draining') AND r.status IN ('closed','aborted') AND COALESCE(JSON_UNQUOTE(JSON_EXTRACT(a.manifest,'$.config.control_rule_version')),JSON_UNQUOTE(JSON_EXTRACT(a.manifest,'$.operation_version')))=?", service.Content.Rules.Version); err != nil {
			return err
		}
		_, err := transaction.ExecContext(ctx, "UPDATE pve_dc_instances i JOIN pve_dc_allocations a ON a.assignment_id=i.current_assignment_id SET i.status='idle',i.current_assignment_id=NULL WHERE a.status='closed'")
		return err
	}); err != nil {
		return err
	}
	if err := service.PublishClosedTrials(ctx); err != nil {
		return err
	}
	if service.CapacityFailed != nil {
		rows, err := service.DB.QueryContext(ctx, "SELECT reserved_proposal_id FROM pve_dc_instances WHERE status='reserved' AND (lease_until<=? OR reserve_until<=?)", service.Now().UTC(), service.Now().UTC())
		if err != nil {
			return err
		}
		proposals := []string{}
		for rows.Next() {
			var proposal string
			if err := rows.Scan(&proposal); err != nil {
				rows.Close()
				return err
			}
			proposals = append(proposals, proposal)
		}
		err = rows.Err()
		rows.Close()
		if err != nil {
			return err
		}
		for _, proposal := range proposals {
			if err := service.CapacityFailed(ctx, proposal); err != nil {
				return err
			}
		}
	}
	rows, err := service.DB.QueryContext(ctx, "SELECT run_id FROM pve_dc_allocations WHERE status IN ('loading','running','draining') AND COALESCE(JSON_UNQUOTE(JSON_EXTRACT(manifest,'$.config.control_rule_version')),JSON_UNQUOTE(JSON_EXTRACT(manifest,'$.operation_version')))=? LIMIT 100", service.Content.Rules.Version)
	if err != nil {
		return err
	}
	ids := []string{}
	for rows.Next() {
		var id string
		if err := rows.Scan(&id); err != nil {
			rows.Close()
			return err
		}
		ids = append(ids, id)
	}
	err = rows.Err()
	rows.Close()
	if err != nil {
		return err
	}
	for _, id := range ids {
		if err := store.Transaction(ctx, service.DB, func(transaction *sql.Tx) error {
			state, allocation, err := service.load(ctx, transaction, id)
			if err != nil {
				return err
			}
			if state.Status == "closed" || state.Status == "aborted" {
				allocation.Status = "closed"
				if _, err := transaction.ExecContext(ctx, "UPDATE pve_dc_instances SET status='idle',current_assignment_id=NULL WHERE current_assignment_id=?", allocation.Manifest.AssignmentID); err != nil {
					return err
				}
				return service.save(ctx, transaction, state, allocation)
			}
			if allocation.GapAt != nil && service.Now().Sub(*allocation.GapAt) >= time.Duration(service.Content.Millis("event_gap_ms"))*time.Millisecond {
				return service.end(ctx, transaction, state, allocation, "event_gap")
			}
			for _, spawn := range state.Spawns {
				if spawn.Status == "pending_spawn" && !(service.S1() && allocation.Clock != nil && allocation.Clock.State == "paused") && !service.Now().Before(spawn.Deadline) {
					return service.end(ctx, transaction, state, allocation, "spawn_unknown")
				}
			}
			if service.S1() && allocation.StartedAt != nil && service.Now().Sub(*allocation.StartedAt) >= time.Hour {
				return service.end(ctx, transaction, state, allocation, "system_abort")
			}
			if state.Status == "running" && !(service.S1() && allocation.Clock != nil && allocation.Clock.State == "paused") && !service.Now().Before(state.DeadlineAt.Add(time.Duration(integer(service.Content.Section("operation"), "terminal_grace_ms"))*time.Millisecond)) {
				return service.end(ctx, transaction, state, allocation, "timeout")
			}
			if err := service.bound(ctx, transaction, allocation); err != nil {
				allocation.TrialEligible = false
				var lease time.Time
				if err := transaction.QueryRowContext(ctx, "SELECT lease_until FROM pve_dc_instances WHERE instance_id=?", service.InstanceID).Scan(&lease); err != nil {
					return err
				}
				if service.Now().Sub(lease) >= time.Duration(service.Content.Millis("technical_wait_ms"))*time.Millisecond {
					return service.end(ctx, transaction, state, allocation, "system_abort")
				}
			}
			for index := range state.Participants {
				member := &state.Participants[index]
				if member.Status == "participating" {
					var status string
					if err := transaction.QueryRowContext(ctx, "SELECT status FROM players WHERE id=?", member.PlayerID).Scan(&status); err != nil {
						return err
					}
					var session int64
					err := transaction.QueryRowContext(ctx, "SELECT session_version FROM pve_dc_admissions WHERE run_id=? AND player_id=? AND status='consumed' ORDER BY connection_generation DESC LIMIT 1", state.ID, member.PlayerID).Scan(&session)
					if err != nil && !errors.Is(err, sql.ErrNoRows) {
						return err
					}
					if status != "normal" || (err == nil && checkPlayer(ctx, transaction, member.PlayerID, session) != nil) {
						operation := fmt.Sprintf("revoke:%d:%d", member.PlayerID, allocation.Connections[strconv.FormatInt(member.PlayerID, 10)])
						if err := service.LeaveTx(ctx, transaction, state, member.PlayerID, operation); err != nil {
							return err
						}
						return nil
					}
				}
				if member.Status == "participating" && !member.Connected && member.DisconnectedAt != nil && service.Now().Sub(*member.DisconnectedAt) >= time.Duration(state.Rules.Reconnect)*time.Second {
					operation := fmt.Sprintf("disconnect:%d:%d", member.PlayerID, allocation.Connections[strconv.FormatInt(member.PlayerID, 10)])
					if err := service.LeaveTx(ctx, transaction, state, member.PlayerID, operation); err != nil {
						return err
					}
					return nil
				}
			}
			return service.save(ctx, transaction, state, allocation)
		}); err != nil {
			return err
		}
	}
	_, err = service.DB.ExecContext(ctx, "UPDATE pve_dc_instances SET status='idle',reserved_proposal_id=NULL,reserve_until=NULL WHERE status='reserved' AND reserve_until<=?", service.Now().UTC())
	if err != nil {
		return err
	}
	if err := service.TransferProgress(ctx); err != nil && !errors.Is(err, ErrProgressTransferBusy) {
		return err
	}
	return nil
}
