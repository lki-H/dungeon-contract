package v3

import (
	"errors"
	"strconv"
)

const (
	OperationVersion = "dc_ruins.v3"
	ProtocolVersion  = 3
	ContentVersion   = "dc_content.v3.draft1"
	WorldVersion     = "dc_rooms.v3.draft1"
	TrialVersion     = "trial.v3.draft1"
)

var (
	ErrVersionMismatch  = errors.New("v3 version mismatch")
	ErrInvalidAdmission = errors.New("invalid v3 admission")
)

type Admission struct {
	OperationVersion string
	ProtocolVersion  int
	ContentVersion   string
	WorldVersion     string
	TrialVersion     string
	ConfigHash       string
	Mode             string
	InitialPlayers   int
	PlayerID         string
	RunID            string
	ConnectionGen    int64
}

func ValidateAdmission(admission Admission, expectedConfigHash string) error {
	return ValidateAdmissionAgainst(admission, Admission{OperationVersion: OperationVersion, ProtocolVersion: ProtocolVersion, ContentVersion: ContentVersion, WorldVersion: WorldVersion, TrialVersion: TrialVersion, ConfigHash: expectedConfigHash})
}

func ValidateAdmissionAgainst(admission Admission, expected Admission) error {
	if (expected.ProtocolVersion != 3 && expected.ProtocolVersion != 4 && expected.ProtocolVersion != 5 && expected.ProtocolVersion != 6 && expected.ProtocolVersion != 7 && expected.ProtocolVersion != 8) || ValidateConfigHash(admission.ConfigHash) != nil || ValidateConfigHash(expected.ConfigHash) != nil || admission.OperationVersion != expected.OperationVersion || admission.ProtocolVersion != expected.ProtocolVersion || admission.ContentVersion != expected.ContentVersion || admission.WorldVersion != expected.WorldVersion || admission.ConfigHash != expected.ConfigHash {
		return ErrVersionMismatch
	}
	if admission.Mode != "normal" && admission.Mode != "trial" {
		return ErrInvalidAdmission
	}
	if admission.Mode == "normal" && admission.TrialVersion != "" || admission.Mode == "trial" && admission.TrialVersion != expected.TrialVersion {
		return ErrVersionMismatch
	}
	if admission.InitialPlayers < 1 || admission.InitialPlayers > 4 || !validPlayerID(admission.PlayerID) || !validRunID(admission.RunID) || admission.ConnectionGen < 1 || admission.ConnectionGen > 4294967295 {
		return ErrInvalidAdmission
	}
	return nil
}

func ValidateConfigHash(hash string) error {
	if len(hash) != 64 {
		return ErrVersionMismatch
	}
	for _, character := range hash {
		if character < '0' || character > '9' && character < 'a' || character > 'f' {
			return ErrVersionMismatch
		}
	}
	return nil
}

func validPlayerID(value string) bool {
	if len(value) == 0 || len(value) > 19 || value[0] == '0' {
		return false
	}
	for _, character := range value {
		if character < '0' || character > '9' {
			return false
		}
	}
	playerID, err := strconv.ParseInt(value, 10, 64)
	return err == nil && playerID > 0
}

func validRunID(value string) bool {
	if len(value) == 0 || len(value) > 64 {
		return false
	}
	for _, character := range value {
		if !(character >= 'a' && character <= 'z') && !(character >= 'A' && character <= 'Z') && !(character >= '0' && character <= '9') && character != '_' && character != '-' {
			return false
		}
	}
	return true
}
