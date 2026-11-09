package run

import (
	"testing"
	"time"
)

func TestCooperationVersionsLateEmissionAndCanceledReinforcement(test *testing.T) {
	for _, version := range []string{"dc_ruins.v4.p3.1", "dc_ruins.v4.p4.1", "dc_ruins.v5.p4.1", "dc_ruins.v5.p5.1"} {
		test.Run(version, func(test *testing.T) {
			if !CooperationVersion(version) {
				test.Fatal("cooperation result version omitted")
			}
			state := p3State(2)
			state.Rules.Version = version
			state.Participants[0].LifeStatus = "dead"
			damage := p3Event("damage", 1, 3000)
			damage.TargetID = "enemy_1"
			if err := p3Apply(state, damage); err != nil {
				test.Fatal("delayed emission", err)
			}
			state.Spawns["reserved"] = Spawn{Status: "pending_spawn"}
			EndAuthority(state, "technical_failure", time.Now().UTC())
			if state.Spawns["reserved"].Status != "canceled" {
				test.Fatal("terminal left pending reinforcement")
			}
		})
	}
}
