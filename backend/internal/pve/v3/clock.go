package v3

import (
	"errors"
	"time"
)

var (
	ErrInvalidClockState = errors.New("invalid v3 clock state")
	ErrPauseNotAllowed   = errors.New("pause is only allowed for a single-player normal run")
	ErrAlreadyPaused     = errors.New("run is already paused")
	ErrNotPaused         = errors.New("run is not paused")
	ErrNotTechnicalWait  = errors.New("run is not in technical wait")
	ErrInvalidClockTime  = errors.New("clock time is before the run start or active transition")
)

type ClockState string

const (
	ClockRunning       ClockState = "running"
	ClockPaused        ClockState = "paused"
	ClockTechnicalWait ClockState = "technical_wait"
)

type ClockSnapshot struct {
	State          ClockState
	GameElapsedMS  int64
	WallElapsedMS  int64
	ManualPauseMS  int64
	Revision       int64
	PauseID        string
	PauseBeginWall time.Time
}

type Clock struct {
	startedAt      time.Time
	state          ClockState
	manualPauseMS  int64
	revision       int64
	activePauseID  string
	activePauseAt  time.Time
	initialPlayers int
	mode           string
	lastTransition time.Time
	lastFrame      int64
}

func NewClock(startedAt time.Time, initialPlayers int, mode string) (*Clock, error) {
	if startedAt.IsZero() || initialPlayers < 1 || initialPlayers > 4 || mode != "normal" && mode != "trial" {
		return nil, ErrInvalidClockState
	}
	return &Clock{startedAt: startedAt, state: ClockRunning, initialPlayers: initialPlayers, mode: mode, lastTransition: startedAt}, nil
}

func (clock *Clock) Snapshot(now time.Time) ClockSnapshot {
	if now.Before(clock.lastTransition) {
		now = clock.lastTransition
	}
	wallMS := elapsedMS(clock.startedAt, now)
	gameMS := wallMS - clock.manualPauseMS
	if clock.state == ClockPaused {
		gameMS -= elapsedMS(clock.activePauseAt, now)
	}
	if gameMS < 0 {
		gameMS = 0
	}
	return ClockSnapshot{
		State:         clock.state,
		GameElapsedMS: gameMS,
		WallElapsedMS: wallMS,
		ManualPauseMS: clock.manualPauseMS + func() int64 {
			if clock.state == ClockPaused {
				return elapsedMS(clock.activePauseAt, now)
			}
			return 0
		}(),
		Revision:       clock.revision,
		PauseID:        clock.activePauseID,
		PauseBeginWall: clock.activePauseAt.UTC(),
	}
}

func (clock *Clock) Pause(pauseID string, now time.Time, effectiveFrame int64) (ClockSnapshot, error) {
	if pauseID == "" || effectiveFrame < 0 || clock.mode != "normal" || clock.initialPlayers != 1 {
		return ClockSnapshot{}, ErrPauseNotAllowed
	}
	if now.Before(clock.lastTransition) || effectiveFrame < clock.lastFrame {
		return ClockSnapshot{}, ErrInvalidClockTime
	}
	if clock.state == ClockPaused {
		return ClockSnapshot{}, ErrAlreadyPaused
	}
	if clock.state != ClockRunning {
		return ClockSnapshot{}, ErrInvalidClockState
	}
	clock.state = ClockPaused
	clock.revision++
	clock.activePauseID = pauseID
	clock.activePauseAt = now
	clock.lastTransition = now
	clock.lastFrame = effectiveFrame
	return clock.Snapshot(now), nil
}

func (clock *Clock) Resume(pauseID string, now time.Time, effectiveFrame int64) (ClockSnapshot, error) {
	if pauseID == "" || effectiveFrame < 0 {
		return ClockSnapshot{}, ErrInvalidClockState
	}
	if clock.state != ClockPaused || clock.activePauseID != pauseID {
		return ClockSnapshot{}, ErrNotPaused
	}
	if now.Before(clock.lastTransition) || effectiveFrame < clock.lastFrame {
		return ClockSnapshot{}, ErrInvalidClockTime
	}
	clock.manualPauseMS += elapsedMS(clock.activePauseAt, now)
	clock.state = ClockRunning
	clock.revision++
	clock.activePauseID = ""
	clock.activePauseAt = time.Time{}
	clock.lastTransition = now
	clock.lastFrame = effectiveFrame
	return clock.Snapshot(now), nil
}

func (clock *Clock) TechnicalWait(now time.Time) (ClockSnapshot, error) {
	if now.Before(clock.lastTransition) {
		return ClockSnapshot{}, ErrInvalidClockTime
	}
	if clock.state == ClockPaused {
		return ClockSnapshot{}, ErrInvalidClockState
	}
	if clock.state == ClockTechnicalWait {
		return clock.Snapshot(now), nil
	}
	clock.state = ClockTechnicalWait
	clock.revision++
	clock.lastTransition = now
	return clock.Snapshot(now), nil
}

func (clock *Clock) ResumeTechnicalWait(now time.Time, effectiveFrame int64) (ClockSnapshot, error) {
	if effectiveFrame < 0 || clock.state != ClockTechnicalWait {
		return ClockSnapshot{}, ErrNotTechnicalWait
	}
	if now.Before(clock.lastTransition) || effectiveFrame < clock.lastFrame {
		return ClockSnapshot{}, ErrInvalidClockTime
	}
	clock.state = ClockRunning
	clock.revision++
	clock.lastTransition = now
	clock.lastFrame = effectiveFrame
	return clock.Snapshot(now), nil
}

func elapsedMS(start, end time.Time) int64 {
	if end.Before(start) {
		return 0
	}
	return end.Sub(start).Milliseconds()
}
