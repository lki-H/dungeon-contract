package main

import "testing"

func TestP2WorldEvidenceUsesSeparateClientFiles(test *testing.T) {
	if p2WorldEvidenceName(0, 1) != "client-p1-v3.json" {
		test.Fatal("Single-player evidence compatibility")
	}
	for _, count := range []int{2, 4} {
		seen := map[string]bool{}
		for index := 0; index < count; index++ {
			path := p2WorldEvidenceName(index, count)
			if seen[path] {
				test.Fatal("Concurrent clients share writable evidence", path)
			}
			seen[path] = true
		}
	}
}
