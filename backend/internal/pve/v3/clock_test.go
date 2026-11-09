package v3

import (
	"testing"
	"time"
)

func TestClockSeparatesGameAndWallTime(test *testing.T) {
	start := time.Date(2026, 10, 12, 12, 0, 0, 0, time.UTC)
	clock, err := NewClock(start, 1, "normal")
	if err != nil {
		test.Fatal(err)
	}
	if _, err := clock.Pause("pause-1", start.Add(2*time.Second), 60); err != nil {
		test.Fatal(err)
	}
	paused := clock.Snapshot(start.Add(12 * time.Second))
	if paused.State != ClockPaused || paused.WallElapsedMS != 12000 || paused.GameElapsedMS != 2000 || paused.ManualPauseMS != 10000 {
		test.Fatalf("unexpected paused clock: %+v", paused)
	}
	resumed, err := clock.Resume("pause-1", start.Add(17*time.Second), 150)
	if err != nil {
		test.Fatal(err)
	}
	if resumed.State != ClockRunning || resumed.WallElapsedMS != 17000 || resumed.GameElapsedMS != 2000 || resumed.ManualPauseMS != 15000 {
		test.Fatalf("unexpected resumed clock: %+v", resumed)
	}
	later := clock.Snapshot(start.Add(22 * time.Second))
	if later.GameElapsedMS != 7000 || later.ManualPauseMS != 15000 {
		test.Fatalf("unexpected post-resume clock: %+v", later)
	}
}

func TestClockRejectsPauseForTrialOrMultiplayer(test *testing.T) {
	start := time.Now().UTC()
	for _, scenario := range []struct {
		name  string
		count int
		mode  string
	}{
		{name: "trial", count: 1, mode: "trial"},
		{name: "multiplayer", count: 2, mode: "normal"},
	} {
		test.Run(scenario.name, func(test *testing.T) {
			clock, err := NewClock(start, scenario.count, scenario.mode)
			if err != nil {
				test.Fatal(err)
			}
			if _, err := clock.Pause("pause-1", start, 1); err != ErrPauseNotAllowed {
				test.Fatalf("expected ErrPauseNotAllowed, got %v", err)
			}
		})
	}
}

func TestClockRejectsStaleResumeAndDuplicatePause(test *testing.T) {
	start := time.Now().UTC()
	clock, err := NewClock(start, 1, "normal")
	if err != nil {
		test.Fatal(err)
	}
	if _, err := clock.Pause("pause-1", start, 1); err != nil {
		test.Fatal(err)
	}
	if _, err := clock.Pause("pause-2", start, 2); err != ErrAlreadyPaused {
		test.Fatalf("expected ErrAlreadyPaused, got %v", err)
	}
	if _, err := clock.Resume("pause-2", start.Add(time.Second), 3); err != ErrNotPaused {
		test.Fatalf("expected ErrNotPaused, got %v", err)
	}
}

func TestClockRejectsTimeBeforeRunStart(test *testing.T) {
	start := time.Date(2026, 10, 12, 12, 0, 0, 0, time.UTC)
	clock, err := NewClock(start, 1, "normal")
	if err != nil {
		test.Fatal(err)
	}
	if _, err := clock.Pause("pause-1", start.Add(-time.Millisecond), 1); err != ErrInvalidClockTime {
		test.Fatalf("expected ErrInvalidClockTime for pause, got %v", err)
	}
	if _, err := clock.TechnicalWait(start.Add(-time.Millisecond)); err != ErrInvalidClockTime {
		test.Fatalf("expected ErrInvalidClockTime for technical wait, got %v", err)
	}
}

func TestTechnicalWaitIsIdempotentAndRecoverable(test *testing.T) {
	start := time.Date(2026, 10, 12, 12, 0, 0, 0, time.UTC)
	clock, err := NewClock(start, 1, "normal")
	if err != nil {
		test.Fatal(err)
	}
	first, err := clock.TechnicalWait(start.Add(time.Second))
	if err != nil {
		test.Fatal(err)
	}
	second, err := clock.TechnicalWait(start.Add(2 * time.Second))
	if err != nil {
		test.Fatal(err)
	}
	if first.Revision != second.Revision || second.State != ClockTechnicalWait || second.GameElapsedMS != 2000 {
		test.Fatalf("technical wait should be idempotent without stopping the clock: first=%+v second=%+v", first, second)
	}
	resumed, err := clock.ResumeTechnicalWait(start.Add(3*time.Second), 90)
	if err != nil {
		test.Fatal(err)
	}
	if resumed.State != ClockRunning || resumed.Revision != first.Revision+1 || resumed.GameElapsedMS != 3000 {
		test.Fatalf("unexpected technical wait recovery: %+v", resumed)
	}
	if _, err := clock.ResumeTechnicalWait(start.Add(4*time.Second), 120); err != ErrNotTechnicalWait {
		test.Fatalf("expected ErrNotTechnicalWait after recovery, got %v", err)
	}
}

func TestClockRejectsStaleTimeAndFramesWithoutChangingState(test *testing.T) {
	start := time.Date(2026, 10, 13, 12, 0, 0, 0, time.UTC)
	clock, err := NewClock(start, 1, "normal")
	if err != nil {
		test.Fatal(err)
	}
	if _, err := clock.Pause("pause-1", start.Add(2*time.Second), 60); err != nil {
		test.Fatal(err)
	}
	for _, scenario := range []struct {
		name  string
		now   time.Time
		frame int64
	}{
		{"earlier time", start.Add(time.Second), 61},
		{"earlier frame", start.Add(3 * time.Second), 59},
	} {
		test.Run(scenario.name, func(test *testing.T) {
			if _, err := clock.Resume("pause-1", scenario.now, scenario.frame); err != ErrInvalidClockTime {
				test.Fatalf("stale resume accepted: %v", err)
			}
			if state := clock.Snapshot(start.Add(time.Second)); state.State != ClockPaused || state.GameElapsedMS+state.ManualPauseMS != state.WallElapsedMS {
				test.Fatalf("invalid snapshot after rejected resume: %+v", state)
			}
		})
	}
	if _, err := clock.Resume("pause-1", start.Add(4*time.Second), 61); err != nil {
		test.Fatal(err)
	}
	if _, err := clock.Pause("pause-2", start.Add(3*time.Second), 62); err != ErrInvalidClockTime {
		test.Fatalf("pause before previous resume accepted: %v", err)
	}
	if _, err := clock.TechnicalWait(start.Add(5 * time.Second)); err != nil {
		test.Fatal(err)
	}
	if _, err := clock.ResumeTechnicalWait(start.Add(4*time.Second), 62); err != ErrInvalidClockTime {
		test.Fatalf("recovery before wait accepted: %v", err)
	}
}
