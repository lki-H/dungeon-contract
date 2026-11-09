import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
import { verifyCurrentFingerprint } from '../../../../deploy/dc-current-integrity.mjs';
import { fileURLToPath } from 'node:url';

const folder = path.dirname(fileURLToPath(import.meta.url));
const docs = path.resolve(folder, '../..');
const root = path.resolve(docs, '../..');
const read = name => JSON.parse(fs.readFileSync(path.join(folder, name), 'utf8'));
const digest = value => crypto.createHash('sha256').update(value).digest('hex');
const canonical = value => Array.isArray(value) ? value.map(canonical) : value && typeof value === 'object'
  ? Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])])) : value;
const clone = value => JSON.parse(JSON.stringify(value));
const same = (first, second) => assert.deepEqual(first, second);
const unique = values => assert.equal(new Set(values).size, values.length);
let negativeCount = 0;
const rejects = operation => { assert.throws(operation); negativeCount++; };
const parameters = read('parameters.json');
const boss = parameters.enemies.find(entry => entry.id === 'boss');
const ids = (prefix, count, width) => Array.from({ length: count }, (_, index) => prefix + String(index + 1).padStart(width, '0'));
const exists = relative => assert(fs.existsSync(path.resolve(docs, relative)), 'Missing document: ' + relative);

function parameterCheck(value) {
  same(value.specification_version, '2.2');
  same(value.status, 'planned_not_loaded');
  same(value.published, false);
  same(value.versions.battle_protocol, 5);
  same(value.versions.control_schema, 2);
  same(value.versions.operation, 'dc_ruins.v5');
  same(value.versions.tasks, 'dc_tasks.v5.draft1');
  same(value.loadouts.length, 2);
  same(value.enemies.length, 5);
  same(value.growth.length, 10);
  same(value.tasks.length, 6);
  same(value.rooms.length, 8);
  for (const field of ['loadouts', 'enemies', 'growth', 'tasks', 'rooms']) unique(value[field].map(entry => entry.id));
  same(value.rooms.filter(entry => !entry.id.endsWith('_branch')).length, 6);
  same(value.input.right_mouse_combat, true);
  same(value.input.charge_supported_weapons, ['rune_hammer']);
  same(value.input.recovery_scope, 'origin_weapon_only');
  same(value.input.switched_weapon_requires_new_left_press, true);
  same(value.player.dodge_cooldown_ms, 3000);
  same(value.player.hurt_invulnerable_ms, 200);
  same(value.player.hit_stun_ms, 0);
  same(value.loadouts[0].melee.charge_supported, false);
  same(value.loadouts[1].melee.charge_supported, true);
  for (const loadout of value.loadouts) {
    assert(loadout.ranged.damage > 0 && loadout.ranged.interval_ms > 0);
    assert(loadout.ranged.projectile_speed_milli_tiles_per_second > 0 && loadout.ranged.projectile_speed_milli_tiles_per_second <= 65535);
    assert(loadout.ranged.projectile_radius_milli_tiles > 0);
    assert(loadout.primary.cooldown_ms > 0 && loadout.secondary.cooldown_ms > 0);
    assert(loadout.melee.windup_ticks > 0 && loadout.melee.active_ticks > 0 && loadout.melee.recovery_ticks > 0);
    same(loadout.ranged.travel_mode, 'unbounded_in_source_room');
    assert(!Object.hasOwn(loadout.ranged, 'range_milli_tiles'));
  }
  assert(value.loadouts[0].ranged.magazine_size > 0 && value.loadouts[0].ranged.reload_ms > 0);
  same(value.loadouts[1].ranged.magazine_size, 0);
  same(value.loadouts[1].ranged.reload_ms, 0);
  assert(value.loadouts[1].melee.charge.minimum_ms > 0);
  assert(value.loadouts[1].melee.charge.full_ms > value.loadouts[1].melee.charge.minimum_ms);
  assert(value.loadouts[1].melee.charge.wave_radius_milli_tiles > value.loadouts[1].melee.charge.direct_contact_radius_milli_tiles);
  same(value.loadouts[1].melee.charge.direct_total_damage_milli_factor, 2000);
  assert(value.player.dodge_invulnerable_ms <= value.player.dodge_duration_ms);
  same(value.projectile_rules.normal_distance_limit_milli_tiles, null);
  same(value.projectile_rules.normal_lifetime_game_ms, null);
  same(value.loadouts[0].melee.windup_ticks + value.loadouts[0].melee.active_ticks + value.loadouts[0].melee.recovery_ticks, 9);
  same(value.loadouts[1].melee.windup_ticks + value.loadouts[1].melee.active_ticks + value.loadouts[1].melee.recovery_ticks, 21);
  same(value.loadouts[1].melee.charge.damage_resolution, 'floor_wave_with_all_modifiers_then_exact_double_direct');
  same(value.loadouts[0].primary.base_damage_milli_factor, 2000);
  same(value.loadouts[0].primary.maximum_projectiles_per_cast, 9);
  same(value.loadouts[0].primary.projectile_speed_milli_tiles_per_second, value.loadouts[0].ranged.projectile_speed_milli_tiles_per_second);
  same(value.loadouts[1].primary.projectile_speed_milli_tiles_per_second, value.loadouts[1].ranged.projectile_speed_milli_tiles_per_second);
  same(value.loadouts[1].primary.damage, 14);
  same(value.loadouts[1].primary.twin_damage, 9);
  same(value.loadouts[1].primary.interval_ms, 600);
  same(value.growth.find(entry => entry.id === 'ranger_tension').melee_cycle_milli_factor, 850);
  assert(!Object.hasOwn(value.growth.find(entry => entry.id === 'ranger_tension'), 'charge_full_delta_ms'));
  const definition = value.enemies.find(entry => entry.id === 'boss');
  same(definition.max_health, 1400);
  same(definition.summon.total_limit, null);
  same(definition.summon.composition, { archer: 2, guard: 2, charger: 1, elite: 1 });
  same(Object.values(definition.summon.composition).reduce((total, amount) => total + amount, 0), 6);
  same(definition.summon.max_alive_and_reserved, 12);
  same(definition.summon.room_cap_including_boss, 13);
  same(definition.summon.max_pending_batches, 1);
  same(definition.summon.first_request_game_ms, 10000);
  same(definition.summon.minimum_actual_spawn_interval_game_ms, 20000);
  same(definition.summon.contract_positive_progress, false);
  same(value.budgets.global_alive_enemies, 24);
  same(value.budgets.projectiles, 1024);
  same(value.budgets.stat_snapshots, 256);
  for (const [index, stage] of definition.phases.entries()) {
    same(stage.projectile_damage, 18);
    same(stage.barrage_duration_ms / stage.barrage_interval_ms * stage.barrage_projectiles_per_pulse, index === 0 ? 96 : 240);
    assert(stage.move_milli_tiles_per_second < value.player.move_milli_tiles_per_second);
    assert(stage.dash_speed_milli_tiles_per_second > value.player.move_milli_tiles_per_second);
  }
  assert(definition.summon.room_cap_including_boss <= value.budgets.global_alive_enemies);
  assert(definition.summon.batch_size <= definition.summon.max_alive_and_reserved);
  same(value.terrain.max_room_width_tiles, 34);
  same(value.terrain.max_room_height_tiles, 20);
  same(value.window.hud_outside_battle_view_required, false);
  same(value.terrain.reference_width, 384);
  same(value.terrain.reference_height, 216);
  same(value.window.ui_reference_width, 512);
  same(value.ui.skill_icons_clickable, false);
  same(value.ui.boss_head_bar, false);
  assert(value.network.dynamic_projectiles_per_group * value.network.dynamic_binary_record_max_bytes < value.network.dynamic_packet_max_bytes);
  assert(Math.ceil(value.budgets.projectiles / value.network.dynamic_projectiles_per_group) + 4 <= value.network.max_dynamic_groups);
  assert(value.network.baseline_max_chunks * value.network.baseline_chunk_payload_max_bytes >= value.network.baseline_max_bytes);
  assert(value.clocks.target_duration_max_ms < value.clocks.active_limit_ms);
  assert(value.clocks.active_limit_ms < value.clocks.wall_limit_ms);
  for (const task of value.tasks) {
    same(task.version, value.versions.tasks);
    for (const objective of task.objectives) if (['kill', 'damage'].includes(objective.kind)) assert(objective.excluded_spawn_origins.includes('boss_summon'));
  }
  for (const source of value.provenance.sources) same(digest(fs.readFileSync(path.resolve(root, source.path))), source.sha256);
}

function playerIDs(value) {
  if (!value || typeof value !== 'object') return;
  for (const [key, child] of Object.entries(value)) {
    if ((key === 'player_id' || key.endsWith('_player_id')) && child !== null) {
      assert(typeof child === 'string' && /^[1-9][0-9]{0,18}$/.test(child));
      assert(BigInt(child) <= 9223372036854775807n);
    }
    if (child && typeof child === 'object') playerIDs(child);
  }
}
function emitterCheck(value) {
  const stage = boss.phases[value.phase - 1];
  same(value.ends_game_ms - value.started_game_ms, stage.barrage_duration_ms);
  same(value.total_pulses, stage.barrage_duration_ms / stage.barrage_interval_ms);
  same(value.shots_per_pulse, stage.barrage_projectiles_per_pulse);
  assert(value.next_pulse_index <= value.total_pulses);
  same(value.next_pulse_game_ms, value.started_game_ms + value.next_pulse_index * stage.barrage_interval_ms);
  same(value.reserved_remaining, (value.total_pulses - value.next_pulse_index) * value.shots_per_pulse);
  same(value.stat_snapshot.damage, 18);
}
function batchCheck(value) {
  same(value.spawn_origin, 'boss_summon');
  same(value.contract_progress_eligible, false);
  if (value.state === 'waiting_capacity') {
    same(value.reserved_enemy_slots, 0);
    same(value.points.length, 0);
    return;
  }
  same(value.reserved_enemy_slots, 6);
  same(value.points.length, 6);
  unique(value.points.map(entry => entry.reservation_id));
  same([...value.points.map(entry => entry.slot)].sort(), [0, 1, 2, 3, 4, 5]);
  for (const [kind, count] of Object.entries(boss.summon.composition)) same(value.points.filter(entry => entry.kind === kind).length, count);
  for (const entry of value.points) same(entry.warning_end_game_ms - entry.warning_begin_game_ms, 800);
  if (value.last_actual_spawn_game_ms !== null) assert(value.requested_game_ms >= value.last_actual_spawn_game_ms + 20000);
}
function clockCheck(value) { same(value.game_ms + value.manual_pause_ms, value.wall_ms); }
function messageCheck(value) {
  playerIDs(value);
  if (Object.hasOwn(value, 'move_x')) assert(value.move_x ** 2 + value.move_y ** 2 <= 1000000);
  if (value.message === 'dc.input.v5') {
    if (value.attack_held) assert(value.attack_episode_id);
    if (value.charge_held) assert(value.charge_episode_id && value.weapon_id === 'rune_hammer');
  }
  if (value.message === 'dc.action.v5' && value.data.kind.startsWith('charge_')) same(value.weapon_id, 'rune_hammer');
  if (value.message === 'dc.receipt.v5') {
    if (value.status === 'accepted') assert(Number.isInteger(value.effective_frame));
    if (['buffered', 'rejected'].includes(value.status)) same(value.effective_frame, null);
  }
  if (value.message === 'dc.emitter.v5') emitterCheck(value.data);
  if (value.message === 'dc.summon.v5') batchCheck(value.data);
  if (value.message === 'dc.dynamic.v5') {
    for (const entity of value.entities) same(Object.hasOwn(entity, 'entity_handle'), value.group_kind === 'projectiles');
    if (value.group_kind === 'own') assert(Number.isInteger(value.processed_input_sequence));
    else same(value.processed_input_sequence, null);
  }
  if (value.message !== 'dc.baseline.v5') return;
  clockCheck(value.clock);
  const owned = value.own;
  same(owned.weapon_id, owned.weapons.find(entry => entry.weapon_id === owned.weapon_id).weapon_id);
  unique(owned.weapons.map(entry => entry.weapon_id));
  same([...owned.weapons.map(entry => entry.weapon_id)].sort(), (owned.loadout === 'runesmith' ? ['rune_bolt', 'rune_hammer'] : ['repeating_crossbow', 'ruin_blade']).sort());
  assert(owned.health <= owned.max_health);
  if (owned.charge) {
    same(owned.weapon_id, 'rune_hammer');
    same(owned.attack_state.phase, 'charging');
    assert(owned.charge.started_game_ms <= value.clock.game_ms);
    assert(owned.weapons.find(entry => entry.weapon_id === owned.weapon_id).ready_game_ms <= owned.charge.started_game_ms);
  }
  const members = new Set([owned.player_id, ...value.peers.map(entry => entry.player_id)]);
  for (const peer of value.peers) if (peer.room_id !== value.observed_room.room_id) same(peer.position_milli, null);
  for (const projectile of value.observed_room.projectiles) if (projectile.owner_player_id) assert(members.has(projectile.owner_player_id));
  for (const projectile of value.observed_room.projectiles) {
    assert(projectile.bounces_remaining <= projectile.stat_snapshot.bounce_count);
    assert(projectile.extra_targets_remaining <= projectile.stat_snapshot.extra_targets);
    same(projectile.room_id, value.observed_room.room_id);
  }
  unique(value.stat_catalog.map(entry => entry.handle));
  assert(value.stat_catalog.length <= parameters.budgets.stat_snapshots);
  for (const emitter of value.observed_room.emitters) emitterCheck(emitter);
  if (value.observed_room.summon_batch) batchCheck(value.observed_room.summon_batch);
  const reserved = value.observed_room.emitters.reduce((total, entry) => total + entry.reserved_remaining, 0);
  assert(reserved + value.observed_room.projectiles.length <= parameters.budgets.projectiles);
  const summonReserved = value.observed_room.summon_batch?.reserved_enemy_slots ?? 0;
  assert(value.observed_room.summon_live_count + summonReserved <= boss.summon.max_alive_and_reserved);
  if (value.observed_room.room_id === 'boss_room') {
    assert(value.observed_room.enemies.length + summonReserved <= boss.summon.room_cap_including_boss);
    same(value.observed_room.summon_live_count, value.observed_room.enemies.filter(entry => entry.spawn_origin === 'boss_summon').length);
    same(value.observed_room.remaining_waves, null);
    same(value.observed_room.finite_enemy_budget_remaining, null);
  }
  if (owned.rescue_target === null) {
    same(owned.rescue_target_life_generation, null);
    same(owned.rescue_target_downed_generation, null);
  } else assert(owned.rescue_target_life_generation > 0 && owned.rescue_target_downed_generation > 0);
}
function frameCheck(value) {
  clockCheck(value.clock);
  playerIDs(value);
  unique(value.facts.map(entry => entry.event_id));
  for (const fact of value.facts) {
    assert(fact.event_game_ms <= value.clock.game_ms);
    if (fact.contract_progress_eligible) {
      assert(fact.actor_player_id);
      assert(!['boss_summon', 'practice'].includes(fact.target_spawn_origin));
    }
    if (fact.kind === 'damage_action') assert(fact.root_attack_id && fact.amount > 0);
    if (['protection', 'control_effect'].includes(fact.kind)) {
      assert(fact.proof && (fact.proof.effective_amount > 0 || fact.proof.effective_time_ms > 0));
      assert(fact.proof.window_begin_game_ms <= fact.proof.window_end_game_ms);
    }
    if (['downed', 'death'].includes(fact.kind)) assert(fact.actor_downed_generation > 0);
    if (fact.kind === 'rescued') assert(fact.target_life_generation > 0 && fact.target_downed_generation > 0);
    if (fact.kind === 'reinforcement_spawned') assert(fact.reinforcement_id);
    if (fact.kind === 'growth_choice') {
      assert(fact.growth_choice);
      same(fact.event_game_ms, fact.growth_choice.selected_game_ms);
    }
    unique(fact.contributors);
    same([...fact.contributors].sort(), fact.contribution_proofs.map(entry => entry.player_id).sort());
  }
  unique(value.facts.filter(entry => entry.kind === 'damage_action' && entry.contract_progress_eligible).map(entry => entry.actor_player_id + ':' + entry.root_attack_id));
  if (value.terminal) {
    assert(value.summary);
    same(value.terminal.cutoff_frame, value.world_frame_id);
    same(value.terminal.cutoff_sequence, value.sequence);
    if (value.terminal.result !== 'aborted') same(value.summary.totals_complete, true);
    unique(value.summary.players.map(entry => entry.player_id));
    unique(value.summary.players.map(entry => entry.slot));
    for (const participant of value.summary.players) {
      same(participant.growth, participant.choices.map(entry => entry.option_id));
      unique(participant.choices.map(entry => entry.node_id));
      for (const choice of participant.choices) {
        same(choice.deadline_game_ms - choice.opened_game_ms, parameters.clocks.choice_timeout_game_ms);
        if (choice.automatic) assert(choice.selected_game_ms >= choice.deadline_game_ms);
        else assert(choice.selected_game_ms >= choice.opened_game_ms && choice.selected_game_ms < choice.deadline_game_ms);
      }
    }
  } else same(value.summary, null);
}
function controlCheck(value) {
  if (value.type === 'v2.run.pause') {
    assert(['menu', 'focus_loss'].includes(value.data.reason));
    same(value.data.pause_id, null);
  } else {
    same(value.type, 'v2.run.resume');
    same(value.data.reason, 'explicit_resume');
    assert(value.data.pause_id);
  }
}

parameterCheck(parameters);
const examples = fs.readdirSync(folder).filter(name => name.endsWith('.example.json') && name !== 'terminal-frame.example.json');
for (const name of examples) {
  const value = read(name);
  if (name === 'pause.example.json' || name === 'resume.example.json') controlCheck(value);
  else messageCheck(value);
}
const terminal = read('terminal-frame.example.json');
frameCheck(terminal);
const unsigned = clone(terminal);
delete unsigned.fingerprint;
same(digest(JSON.stringify(canonical(unsigned))), terminal.fingerprint);
const release = read('charge-release.example.json');
same(release.data.episode_id, read('baseline.example.json').own.charge.episode_id);
same(release.weapon_revision, read('baseline.example.json').own.weapon_revision);
for (const mutation of [
  value => { value.published = true; },
  value => { value.versions.battle_protocol = 4; },
  value => { value.input.recovery_scope = 'global'; },
  value => { value.input.right_mouse_combat = false; },
  value => { value.enemies[4].summon.max_alive_and_reserved = 18; },
  value => { value.enemies[4].summon.max_pending_batches = 2; },
  value => { value.enemies[4].phases[1].projectile_damage = 14; },
  value => { value.network.dynamic_projectiles_per_group = 30; },
  value => { value.tasks[1].objectives[0].excluded_spawn_origins = []; }
]) rejects(() => { const value = clone(parameters); mutation(value); parameterCheck(value); });
rejects(() => { const value = read('input.example.json'); value.charge_episode_id = null; messageCheck(value); });
rejects(() => { const value = read('charge-release.example.json'); value.weapon_id = 'ruin_blade'; messageCheck(value); });
rejects(() => { const value = read('input.example.json'); value.move_x = 1000; value.move_y = 1000; messageCheck(value); });
rejects(() => { const value = read('emitter.example.json'); value.data.reserved_remaining--; messageCheck(value); });
rejects(() => { const value = read('summon.example.json'); value.data.points[1].reservation_id = value.data.points[0].reservation_id; messageCheck(value); });
rejects(() => { const value = read('summon.example.json'); value.data.points[0].warning_end_game_ms--; messageCheck(value); });
rejects(() => { const value = read('baseline.example.json'); value.own.player_id = '9223372036854775808'; messageCheck(value); });
rejects(() => { const value = read('baseline.example.json'); value.clock.wall_ms++; messageCheck(value); });
rejects(() => { const value = read('baseline.example.json'); value.own.weapons[1].ready_game_ms = 25800; messageCheck(value); });
rejects(() => { const value = clone(terminal); value.facts[0].target_spawn_origin = 'boss_summon'; frameCheck(value); });
rejects(() => { const value = clone(terminal); value.facts.push(clone(value.facts[0])); frameCheck(value); });
rejects(() => { const value = clone(terminal); value.summary.totals_complete = false; frameCheck(value); });
rejects(() => { const value = read('resume.example.json'); value.data.reason = 'focus_loss'; controlCheck(value); });
rejects(() => { const value = read('dynamic.example.json'); value.group_kind = 'projectiles'; messageCheck(value); });
rejects(() => { const value = clone(terminal); value.summary.players[0].choices = []; frameCheck(value); });
rejects(() => { const value = clone(terminal); value.facts[0].kind = 'downed'; frameCheck(value); });
rejects(() => { const value = clone(parameters); value.loadouts[0].primary.cooldown_ms = 0; parameterCheck(value); });
rejects(() => { const value = clone(parameters); value.loadouts[1].melee.charge.full_ms = 200; parameterCheck(value); });
rejects(() => { const value = read('baseline.example.json'); value.observed_room.projectiles[0].bounces_remaining = 2; messageCheck(value); });

let waveChecks = 0;
for (const scaling of parameters.scaling) {
  for (const room of parameters.rooms) for (const baseCount of room.waves) {
    const count = Math.ceil(baseCount * scaling.count_milli / 1000);
    const ranged = Math.ceil(count / 3);
    const melee = count - ranged;
    const fractions = room.id.endsWith('_branch') ? [parameters.encounters.branch_charger_fraction]
      : room.id === 'sanctum' ? [parameters.encounters.sanctum_charger_fraction] : parameters.encounters.seeded_charger_fractions;
    for (const fraction of fractions) {
      const chargers = Math.ceil(melee * fraction.numerator / fraction.denominator);
      assert(chargers >= 0 && chargers <= melee);
      assert(ranged >= Math.ceil(melee / 2));
      same(melee - chargers + chargers + ranged, count);
      waveChecks++;
    }
  }
}
function scaledPhases(phases, factor) {
  const originalTotal = phases.reduce((total, value) => total + value, 0);
  const total = Math.ceil(originalTotal * factor / 1000);
  const values = phases.map(value => Math.floor(value * total / originalTotal));
  const order = phases.map((value, index) => ({ index, remainder: value * total % originalTotal }))
    .sort((first, second) => second.remainder - first.remainder || first.index - second.index);
  let missing = total - values.reduce((sum, value) => sum + value, 0);
  for (const entry of order) if (missing-- > 0) values[entry.index]++;
  return values;
}
same(scaledPhases([2, 3, 4], 850), [2, 3, 3]);
same(scaledPhases([6, 3, 12], 900), [5, 3, 11]);
same(Math.floor(12 * 2 * 650 / 1000), 15);
same(Math.floor(9 * 1500 / 1000), 13);
const partialWave = Math.floor(32 + 32 * (510 - 250) / 650);
same([partialWave, partialWave * 2], [44, 88]);
const trace = JSON.parse(fs.readFileSync(path.join(docs, 'combat-ui-traceability.json'), 'utf8'));
const acceptance = read('acceptance.json');
same(trace.questions.map(entry => entry.id), ids('Q22-', 37, 2));
same(trace.feedback.map(entry => entry.id), ids('F22-', 13, 2));
same(acceptance.cases.map(entry => entry.id), ids('D22-', 60, 3));
const caseIDs = new Set(acceptance.cases.map(entry => entry.id));
const answerIDs = new Set(trace.questions.map(entry => entry.id));
const coveredAnswers = new Set(trace.feedback.flatMap(entry => entry.answers));
for (const answer of answerIDs) assert(coveredAnswers.has(answer), 'Uncovered answer: ' + answer);
function pointer(value, reference) {
  assert(reference.startsWith('/'));
  return reference.slice(1).split('/').reduce((current, component) => {
    const key = component.replace(/~1/g, '/').replace(/~0/g, '~');
    assert(current && Object.hasOwn(current, key), 'Missing parameter: ' + reference);
    return current[key];
  }, value);
}
const decisionText = fs.readFileSync(path.join(docs, 'decisions.md'), 'utf8');
for (const entry of trace.questions) {
  assert(decisionText.includes('| ' + entry.id + ' |') && decisionText.includes(entry.answer));
  for (const document of entry.documents) exists(document);
  for (const reference of entry.parameter_pointers) pointer(parameters, reference);
  for (const identifier of entry.tests) assert(caseIDs.has(identifier));
}
for (const entry of trace.feedback) {
  for (const answer of entry.answers) assert(answerIDs.has(answer));
  for (const identifier of entry.tests) assert(caseIDs.has(identifier));
  for (const document of entry.documents) exists(document);
}
for (const entry of acceptance.cases) {
  same(entry.status, 'NOT_RUN');
  same(entry.evidence, []);
  exists(entry.document);
}
same(trace.new_cases, acceptance.cases.map(entry => entry.id));
const coveredCases = new Set([...trace.feedback.flatMap(entry => entry.tests), ...trace.cross_cutting_tests]);
for (const identifier of caseIDs) assert(coveredCases.has(identifier), 'Unmapped new case: ' + identifier);
same(acceptance.human.status, 'NOT_RUN');
same(acceptance.performance.status, 'NOT_RUN');
for (const source of trace.preserved_sources) same(digest(fs.readFileSync(path.resolve(docs, source.path))), source.sha256);
const archive = JSON.parse(fs.readFileSync(path.join(docs, 'archive/2.1/manifest.json'), 'utf8'));
same(archive.files.length, 19);
for (const entry of archive.files) {
  const bytes = fs.readFileSync(path.resolve(root, entry.archive_path));
  same(bytes.length, entry.archive_bytes);
  same(digest(bytes), entry.archive_sha256);
}
const protectedFiles = read('protected-files.json');
same(protectedFiles.protected.length + protectedFiles.formal_update_preimages.length, 501);
const implementationRecord = JSON.parse(fs.readFileSync(path.join(docs, 'p2-2-change-record.json'), 'utf8'));
same(implementationRecord.profile, 'dc_ruins.v5.p4.1');
const authorizedChanges = new Map(implementationRecord.changed.map(entry => [entry.path, entry]));
const p5RecordPath=path.join(docs,'p5-change-record.json');
const p5Record=fs.existsSync(p5RecordPath)?JSON.parse(fs.readFileSync(p5RecordPath,'utf8')):null;
const p5Changes=new Map((p5Record?.changed??[]).map(entry=>[entry.path,entry]));
for (const entry of protectedFiles.protected) {
  const actual = digest(fs.readFileSync(path.resolve(root, entry.path)));
  if (actual === entry.sha256) continue;
  const p5Direct=p5Changes.get(entry.path);
  if(p5Direct&&p5Direct.before_sha256===entry.sha256){assert(/^(backend\/.*\.go|client\/unity-demo\/Assets\/.*\.(cs|meta)|deploy\/.*\.(ps1|mjs|py)|docs\/game-demo\/(radmin-human-multiplayer-guide|one-click-playtest-launcher)\.md)$/.test(entry.path),'Immutable P5 history: '+entry.path);verifyCurrentFingerprint(root,entry.path,p5Direct.after_sha256);continue;}
  assert(/^(backend\/.*\.go|client\/unity-demo\/Assets\/DungeonContract\/.*\.(cs|meta)|deploy\/.*\.(ps1|mjs)|docs\/game-demo\/radmin-human-multiplayer-guide\.md)$/.test(entry.path), 'Immutable history: ' + entry.path);
  const change = authorizedChanges.get(entry.path);
  assert(change, 'Missing implementation authorization: ' + entry.path);
  same(change.before_sha256, entry.sha256);
  if(change.after_sha256!==actual){const successor=p5Changes.get(entry.path);assert(successor,'Missing P5 successor: '+entry.path);same(successor.before_sha256,change.after_sha256);verifyCurrentFingerprint(root,entry.path,successor.after_sha256);}else same(change.after_sha256, actual);
}
for (const entry of archive.files) same(entry.original_sha256, protectedFiles.formal_update_preimages.find(original => original.path === entry.original_path).sha256);
console.log(JSON.stringify({ scope: 'documentation_only', status: 'PASS_DOCUMENTS_ONLY', parameters: true, message_examples: examples.length, semantic_negative_cases: negativeCount, wave_compositions: waveChecks, damage_and_tick_examples: true, terminal_fingerprint: true, answers: 37, feedback: 13, new_cases: 60, all_new_cases: 'NOT_RUN', archive_documents: archive.files.length, protected_files: protectedFiles.protected.length, historical_sources_unchanged: true, runtime_or_gameplay_tested: false }, null, 2));
