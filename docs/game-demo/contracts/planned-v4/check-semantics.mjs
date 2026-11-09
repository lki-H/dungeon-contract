import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';

const folder = path.dirname(fileURLToPath(import.meta.url));
const docs = path.resolve(folder, '../..');
const read = name => JSON.parse(fs.readFileSync(path.join(folder, name), 'utf8'));
const stable = value => Array.isArray(value) ? value.map(stable) : value && typeof value === 'object'
  ? Object.fromEntries(Object.keys(value).sort().map(key => [key, stable(value[key])])) : value;
const digest = value => crypto.createHash('sha256').update(value).digest('hex');
const same = (first, second) => assert.deepEqual(first, second);
const unique = items => assert.equal(new Set(items).size, items.length);
let negatives = 0;
const rejects = operation => { assert.throws(operation); negatives++; };
const playerID = value => assert(/^[1-9][0-9]{0,18}$/.test(value) && BigInt(value) <= 9223372036854775807n);

function parametersCheck(value) {
  same(value.versions.operation, 'dc_ruins.v4');
  same(value.versions.battle_protocol, 4);
  same(value.versions.control_schema, 2);
  same(value.published, false);
  same(value.status, 'planned_not_loaded');
  same(value.loadouts.length, 2);
  same(value.enemies.length, 5);
  same(value.growth.length, 10);
  same(value.tasks.length, 6);
  same(value.rooms.filter(room => !room.id.endsWith('_branch')).length, 6);
  same(value.terrain.max_room_width_tiles, 24);
  same(value.terrain.max_room_height_tiles, 14);
  same(value.terrain.reference_width, 512);
  same(value.terrain.reference_height, 288);
  same(value.terrain.spawn_warning_ms, 800);
  same(value.player.dodge_cooldown_ms, 3000);
  same(value.player.hurt_invulnerable_ms, 200);
  same(value.ai.attack_ground_geometry, false);
  same(value.network.dynamic_delivery, 'unreliable_application_group_order');
  same(value.network.baseline_chunk_payload_max_bytes, 768);
  assert(value.network.max_pending_baselines_per_connection === 1);
  assert(value.network.baseline_max_chunks * value.network.baseline_chunk_payload_max_bytes >= value.network.baseline_max_bytes);
  assert(value.network.baseline_chunk_payload_max_bytes < value.network.dynamic_packet_max_bytes);
  assert(value.clocks.target_duration_max_ms < value.clocks.active_limit_ms);
  assert(value.clocks.active_limit_ms < value.clocks.wall_limit_ms);
  for (const collection of ['loadouts', 'enemies', 'rooms', 'growth', 'tasks']) unique(value[collection].map(item => item.id));
  for (const loadout of value.loadouts) {
    assert(loadout.ranged.interval_ms > 0 && loadout.primary.cooldown_ms > 0 && loadout.secondary.cooldown_ms > 0);
    const options = value.growth.filter(option => option.scope === 'common' || option.scope === loadout.id);
    same(options.length, 7);
    for (const first of options) for (const second of options.filter(option => option.id !== first.id)) {
      assert(options.filter(option => option.id !== first.id && option.id !== second.id).length >= 3);
    }
  }
  same(value.loadouts[1].primary.twin_damage, value.growth.find(option => option.id === 'runesmith_twin').turret_damage);
  assert(value.player.dodge_invulnerable_ms <= value.player.dodge_duration_ms);
  assert(value.loadouts[0].primary.dash_invulnerable_ms <= value.loadouts[0].primary.dash_duration_ms);
}

function baselineCheck(value) {
  same(value.clock.game_elapsed_ms + value.clock.manual_pause_ms, value.clock.wall_elapsed_ms);
  playerID(value.own.player_id);
  unique(value.own.weapon_states.map(item => item.weapon_id));
  same(value.own.weapon_states.length, 2);
  const kit = parameters.loadouts.find(item => item.id === value.own.loadout_id);
  same(value.own.weapon_states.map(item => item.weapon_id), [kit.ranged.weapon, kit.melee.weapon]);
  assert(value.own.weapon_states.some(item => item.weapon_id === value.own.active_weapon_id));
  assert(value.own.health <= value.own.max_health);
  for (const weapon of value.own.weapon_states) assert(weapon.magazine <= weapon.magazine_max);
  same(value.team.reinforcements_available + value.team.reinforcements_reserved + value.team.reinforcements_consumed, value.team.reinforcements_total);
  if (value.team.mode === 'normal') same(value.team.trial_eligible, false);
  for (const peer of value.peers) {
    playerID(peer.player_id);
    if (!peer.observed) same([peer.x_milli, peer.y_milli, peer.aim_u16], [null, null, null]);
  }
  for (const reservation of value.spawn_reservations) {
    same(reservation.warning_end_game_ms - reservation.warning_begin_game_ms, parameters.terrain.spawn_warning_ms);
    assert(reservation.warning_begin_game_ms <= value.clock.game_elapsed_ms);
  }
  const reserved = value.spawn_reservations.filter(item => item.state === 'warning' && item.origin !== 'stuck_recovery').length;
  assert(value.enemies.length + reserved <= parameters.budgets.global_alive_enemies);
}

function frameCheck(value) {
  same(value.elapsed_ms + value.manual_pause_ms, value.wall_elapsed_ms);
  unique(value.facts.map(fact => fact.event_id));
  same(value.facts.map(fact => fact.event_id), value.facts.map(fact => fact.event_id).sort());
  for (const fact of value.facts) {
    assert(fact.event_game_ms <= value.elapsed_ms);
    if (fact.actor_player_id !== null) playerID(fact.actor_player_id);
    unique(fact.contributors);
    fact.contributors.forEach(playerID);
    if (fact.proof !== null) assert(fact.proof.window_begin_game_ms <= fact.proof.window_end_game_ms && fact.proof.window_end_game_ms <= fact.event_game_ms);
    unique(fact.contribution_proofs.map(item => item.player_id));
    for (const member of fact.contribution_proofs) {
      playerID(member.player_id);
      assert(fact.contributors.includes(member.player_id));
      assert(member.proof.window_begin_game_ms <= member.proof.window_end_game_ms && member.proof.window_end_game_ms <= fact.event_game_ms);
      assert(member.proof.effective_amount > 0 || member.proof.effective_time_ms > 0);
    }
    if (fact.kind === 'interact') for (const member of fact.contributors.filter(member => member !== fact.actor_player_id)) {
      assert(fact.contribution_proofs.some(proof => proof.player_id === member));
    }
  }
  if (value.terminal !== null) {
    same(value.terminal.cutoff_frame, value.world_frame_id);
    same(value.terminal.cutoff_sequence, value.sequence);
    unique(value.summary.participants.map(item => item.player_id));
    unique(value.summary.participants.map(item => item.slot));
    value.summary.participants.forEach(item => playerID(item.player_id));
  }
  const copied = structuredClone(value);
  delete copied.fingerprint;
  same(value.fingerprint, digest(Buffer.from(JSON.stringify(stable(copied)), 'utf8')));
}

function chunksCheck(chunks) {
  const first = chunks[0];
  same(chunks.length, first.chunk_count);
  same(first.chunk_count, Math.ceil(first.total_bytes / parameters.network.baseline_chunk_payload_max_bytes));
  const sorted = [...chunks].sort((firstChunk, secondChunk) => firstChunk.chunk_index - secondChunk.chunk_index);
  unique(sorted.map(item => item.chunk_index));
  const payloads = sorted.map((chunk, index) => {
    same(chunk.chunk_index, index);
    same(chunk.chunk_count, first.chunk_count);
    same(chunk.total_bytes, first.total_bytes);
    same(chunk.payload_sha256, first.payload_sha256);
    same(chunk.transfer_id, first.transfer_id);
    same(chunk.baseline_id, first.baseline_id);
    same(chunk.run_id, baseline.run_id);
    same(chunk.source_generation, baseline.source_generation);
    same(chunk.connection_generation, baseline.connection_generation);
    same(chunk.interest_revision, first.interest_revision);
    const bytes = Buffer.from(chunk.chunk_data_base64, 'base64');
    same(bytes.toString('base64'), chunk.chunk_data_base64);
    assert(bytes.length > 0 && bytes.length <= parameters.network.baseline_chunk_payload_max_bytes);
    if (index < first.chunk_count - 1) same(bytes.length, parameters.network.baseline_chunk_payload_max_bytes);
    return bytes;
  });
  const bytes = Buffer.concat(payloads);
  same(bytes.length, first.total_bytes);
  assert(bytes.length <= parameters.network.baseline_max_bytes);
  same(digest(bytes), first.payload_sha256);
  same(JSON.parse(bytes.toString('utf8')), baseline);
}

const parameters = read('parameters.json');
const baseline = read('baseline.example.json');
const terminal = read('terminal-frame.example.json');
const chunks = read('baseline-chunks.example.json');
parametersCheck(parameters);
baselineCheck(baseline);
frameCheck(terminal);
chunksCheck(chunks);
const snapshot = read('snapshot.example.json');
same(snapshot.own, baseline.own);
same(snapshot.clock, baseline.clock);
const switchAction = read('switch.example.json');
const receipt = read('action-receipt.example.json');
same(receipt.weapon_revision, switchAction.weapon_revision + 1);
same(receipt.weapon_id, switchAction.requested_weapon_id);
same(receipt.action_sequence, switchAction.action_sequence);
const ack = read('control-ack.example.json');
same(ack.operation_id, read('pause.example.json').operation_id);
same(ack.pause_id, read('resume.example.json').data.pause_id);
const state = read('state-update.example.json');
same(state.state_revision, baseline.state_revision + 1);
same(state.interest_revision, baseline.interest_revision);
for (const mutate of [
  value => value.own.health = value.own.max_health + 1,
  value => value.own.weapon_states[0].magazine = value.own.weapon_states[0].magazine_max + 1,
  value => value.own.active_weapon_id = 'rune_bolt',
  value => value.clock.wall_elapsed_ms++,
  value => value.team.reinforcements_available++,
  value => value.spawn_reservations[0].warning_end_game_ms--,
  value => value.own.player_id = '9999999999999999999'
]) { const changed = structuredClone(baseline); mutate(changed); rejects(() => baselineCheck(changed)); }
for (const mutate of [
  value => value.terrain.reference_width = 384,
  value => value.loadouts[1].primary.twin_damage = 5,
  value => value.loadouts[0].primary.dash_invulnerable_ms = 999,
  value => value.loadouts[0].ranged.interval_ms = 0
]) { const changed = structuredClone(parameters); mutate(changed); rejects(() => parametersCheck(changed)); }
for (const mutate of [
  value => value.reverse(),
  value => value[0].chunk_data_base64 = 'YWJj',
  value => value[0].payload_sha256 = '0'.repeat(64)
]) {
  const changed = structuredClone(chunks); mutate(changed);
  if (changed[0].chunk_index !== 0 && changed.length === chunks.length) chunksCheck(changed);
  else rejects(() => chunksCheck(changed));
}
const missing = structuredClone(chunks); missing.pop(); rejects(() => chunksCheck(missing));
for (const mutate of [
  value => value.facts.reverse(),
  value => value.fingerprint = '0'.repeat(64),
  value => value.wall_elapsed_ms++,
  value => value.terminal.cutoff_sequence++
]) { const changed = structuredClone(terminal); mutate(changed); rejects(() => frameCheck(changed)); }
const trace = JSON.parse(fs.readFileSync(path.join(docs, 'playtest-feedback-traceability.json'), 'utf8'));
same([trace.feedback.length, trace.answers.length, trace.tests.length], [16, 30, 48]);
assert(trace.tests.every(item => item.status === 'NOT_RUN'));
const audit = JSON.parse(fs.readFileSync(path.join(docs, 'technical-audit-2-1.json'), 'utf8'));
same(audit.findings.length, 16);
same(audit.tests.length, 16);
unique(audit.findings.map(item => item.id));
unique(audit.tests.map(item => item.id));
assert(audit.tests.every(item => item.status === 'NOT_RUN'));
for (const finding of audit.findings) for (const name of finding.documents) assert(fs.existsSync(path.join(docs, name)));
console.log(JSON.stringify({
  status: 'PASS_DOCUMENTS_ONLY',
  parameters_and_recovery: true,
  terminal_fingerprint_and_cutoff: true,
  baseline_chunk_hash_reassembly: true,
  semantic_negative_checks: negatives,
  feedback: 16,
  confirmed_answers: 30,
  existing_behaviour_tests: 48,
  new_technical_tests: 16,
  runtime_tests: 'NOT_RUN'
}));
