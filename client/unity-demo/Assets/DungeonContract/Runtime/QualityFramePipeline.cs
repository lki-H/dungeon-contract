using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class DcBootstrap
    {
        private sealed class RecordedTick
        {
            public long Elapsed;
            public long Frame;
            public JObject Before;
            public JObject After;
            public JArray Facts;
            public ActionIntent[] Actions;
            public Dictionary<string, GrayboxInput> Inputs;
            public JObject Terminal;
            public JObject Summary;
            public double Created;
        }
        private readonly List<RecordedTick> recordedTicks = new List<RecordedTick>();
        private bool pumpingQuality;
        private double oldestPendingAt;
        private bool qualityWait;

        private void TickQualityWorld()
        {
            if (world.Terminal) return;
            if (pumpingQuality && oldestPendingAt > 0 && Time.realtimeSinceStartupAsDouble - oldestPendingAt > 0.25)
            {
                qualityWait = true; hadWait = true; world.FreezeTime(elapsed.ElapsedMilliseconds); BroadcastWorld(); return;
            }
            if (recordedTicks.Count >= 60) { hadWait = true; _ = AbortTechnical(); return; }
            var currentInputs = inputs.ToDictionary(item => item.Key, item => receivedInputAt.TryGetValue(item.Key, out var received) && Time.realtimeSinceStartupAsDouble - received <= 0.25 ? item.Value : default);
            world.AdvanceTime(elapsed.ElapsedMilliseconds);
            var record = new RecordedTick { Before = world.Capture(), Elapsed = elapsed.ElapsedMilliseconds, Actions = actions.ToArray(), Inputs = currentInputs, Created = Time.realtimeSinceStartupAsDouble };
            actions.Clear();
            ApplyRecordedTick(record, true);
            recordedTicks.Add(record);
            if (record.Facts.Count > 0 || record.Terminal != null) { oldestPendingAt = oldestPendingAt == 0 ? record.Created : oldestPendingAt; if (!pumpingQuality) _ = PumpQualityFrames(); }
            else if (!pumpingQuality && !recordedTicks.Any(item => item.Facts.Count > 0 || item.Terminal != null)) recordedTicks.Clear();
            BroadcastWorld();
        }

        private void ApplyRecordedTick(RecordedTick record, bool sendReceipt)
        {
            world.AdvanceTime(record.Elapsed);
            foreach (var input in record.Inputs) world.Input(input.Key, input.Value);
            foreach (var intent in record.Actions)
            {
                var accepted = connected.TryGetValue(intent.Connection, out var identity) && (string)identity["player_id"] == intent.Player && world.Action(intent.Player, intent.Kind, intent.Intent);
                if (sendReceipt) SendActionReceipt(intent, accepted);
            }
            record.Facts = new JArray(world.Tick(record.Elapsed)); record.Frame = world.WorldTick + 1;
            record.Terminal = world.Terminal ? world.TerminalRequest(record.Frame, 0) : null;
            record.Summary = record.Terminal == null ? null : world.Summary(); record.After = world.Capture();
        }

        private async Task PumpQualityFrames()
        {
            pumpingQuality = true;
            try
            {
                await sender.WaitAsync(stopping.Token);
                try
                {
                    for (;;)
                    {
                        var record = recordedTicks.FirstOrDefault(item => item.Facts.Count > 0 || item.Terminal != null);
                        if (record == null) break;
                        if ((string)record.Terminal?["outcome"] == "aborted") { await AbortTechnical(); recordedTicks.Clear(); break; }
                        oldestPendingAt = record.Created;
                        var frame = QualityFrame(record); var retries = 0; var fenced = false;
                        JObject receipt;
                        for (;;)
                        {
                            frame.Remove("fingerprint"); frame["fingerprint"] = Hash(Encoding.UTF8.GetBytes(Canonical(frame)));
                            try { receipt = await Internal("runs/" + assignment["run_id"] + "/frames", frame); break; }
                            catch (ServiceFailure failure) when (failure.Code == 40982)
                            {
                                var commands = await ReadCommands(); if ((long)frame["control_version"] == controlVersion) throw;
                                world.Restore(record.Before);
                                foreach (var member in (JArray)assignment["members"]) { var id = (string)member["player_id"]; world.Presence(id, connected.Values.Any(identity => (string)identity["player_id"] == id)); }
                                foreach (var command in commands) world.Leave((string)command["payload"]["player_id"]);
                                record.Before = world.Capture(); ApplyRecordedTick(record, false); fenced = true; frame = QualityFrame(record);
                            }
                            catch (Exception error) when ((error is HttpRequestException || error is TaskCanceledException || error is ServiceFailure service && service.Status == 503) && !stopping.IsCancellationRequested && retries++ < 2)
                            {
                                qualityWait = true; hadWait = true; await Task.Delay(250 * retries, stopping.Token);
                            }
                        }
                        appliedSequence = (long)receipt["applied_sequence"]; lastWorldFrame = (long)frame["world_frame_id"];
                        if (fenced)
                        {
                            world.Acknowledge(receipt);
                            foreach (var subsequent in recordedTicks.Where(item => item.Elapsed > record.Elapsed).ToArray()) { subsequent.Before = world.Capture(); ApplyRecordedTick(subsequent, false); }
                        }
                        else world.Acknowledge(receipt);
                        recordedTicks.RemoveAll(item => item.Elapsed <= record.Elapsed);
                        if ((string)receipt["run_status"] != "running") { running = false; draining = true; recordedTicks.Clear(); }
                        BroadcastWorld(true);
                    }
                    if (qualityWait && running)
                    {
                        var waitingFrame = new JObject { ["schema_version"] = 2, ["source"] = "unity_game_server", ["run_id"] = assignment["run_id"], ["instance_id"] = "dc_local_01", ["boot_id"] = boot, ["assignment_id"] = assignment["assignment_id"], ["source_generation"] = assignment["source_generation"], ["sequence"] = appliedSequence + 1, ["frame_id"] = Guid.NewGuid().ToString(), ["world_frame_id"] = Math.Max(lastWorldFrame + 1, world.WorldTick + 1), ["elapsed_ms"] = elapsed.ElapsedMilliseconds, ["control_version"] = controlVersion, ["facts"] = new JArray(PhaseFact("technical_wait")), ["terminal"] = null, ["summary"] = null };
                        waitingFrame["fingerprint"] = Hash(Encoding.UTF8.GetBytes(Canonical(waitingFrame)));
                        var waitingReceipt = await Internal("runs/" + assignment["run_id"] + "/frames", waitingFrame);
                        appliedSequence = (long)waitingReceipt["applied_sequence"]; lastWorldFrame = (long)waitingFrame["world_frame_id"];
                    }
                    recordedTicks.Clear(); qualityWait = false; hadWait = false;
                    BroadcastWorld(true);
                }
                finally { sender.Release(); }
            }
            catch (Exception error) { Fail(error); hadWait = true; await AbortTechnical(); }
            finally { oldestPendingAt = 0; pumpingQuality = false; }
        }

        private JObject QualityFrame(RecordedTick record)
        {
            var frame = new JObject { ["schema_version"] = 2, ["source"] = "unity_game_server", ["run_id"] = assignment["run_id"], ["instance_id"] = "dc_local_01", ["boot_id"] = boot, ["assignment_id"] = assignment["assignment_id"], ["source_generation"] = assignment["source_generation"], ["sequence"] = appliedSequence + 1, ["frame_id"] = Guid.NewGuid().ToString(), ["world_frame_id"] = Math.Max(lastWorldFrame + 1, record.Frame), ["elapsed_ms"] = record.Elapsed, ["control_version"] = controlVersion, ["facts"] = record.Facts.Count == 0 ? new JArray(PhaseFact("active")) : record.Facts, ["terminal"] = record.Terminal?.DeepClone(), ["summary"] = record.Summary?.DeepClone() };
            if (frame["terminal"] is JObject terminal) { terminal["cutoff_frame"] = frame["world_frame_id"]; terminal["cutoff_sequence"] = frame["sequence"]; }
            if ((string)record.Terminal?["reason"] == "timeout") frame["elapsed_ms"] = assignment["config"]["operation"]["hard_deadline_ms"];
            return frame;
        }
    }
}
