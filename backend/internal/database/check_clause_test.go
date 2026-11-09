package database

import "testing"

func TestNormalizeCheckClausePreservesPredicateAndInnerParentheses(test *testing.T) {
	for _, scenario := range []struct {
		clause, expected string
	}{
		{"(`protocol_version` = 3)", "protocol_version=3"},
		{"(`initial_player_count` between 1 and 4)", "initial_player_countbetween1and4"},
		{"(`status` in (_utf8mb4\\'open\\',_utf8mb4\\'closed\\',_utf8mb4\\'aborted\\'))", "statusin('open','closed','aborted')"},
		{"(`protocol_version` >= 0)", "protocol_version>=0"},
	} {
		if actual := normalizeCheckClause(scenario.clause); actual != scenario.expected {
			test.Fatalf("check normalization changed predicate: %q != %q", actual, scenario.expected)
		}
	}
}
