using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DungeonContract
{
    public sealed class DcP5AuthorityProbe : MonoBehaviour
    {
        private DcP1World world;
        private JObject initial;
        private DcP1World.RuntimeCheckpoint initialRuntime;
        private JObject parameters;
        private JObject geometry;
        private JObject configuration;
        private readonly List<double> samples=new List<double>();
        private readonly List<double> checkpointSamples=new List<double>();
        private readonly List<double> tickSamples=new List<double>();
        private readonly List<double> snapshotSamples=new List<double>();
        private readonly List<double> lifecycleSamples=new List<double>();
        private readonly Dictionary<string,List<double>> phaseSamples=new Dictionary<string,List<double>>();
        private readonly JArray results=new JArray();
        private readonly JArray slowTicks=new JArray();
        private double started;
        private double next;
        private int scenario;
        private bool Smoke=>DcBootstrap.Argument(DcV8Profile.Active?"-dc-v8-perf-smoke":DcV7Profile.Active?"-dc-v7-perf-smoke":"-dc-v6-perf-smoke","false")=="true";
        private int Warmup=>DcV6Profile.Active?(Smoke?5:30):15;
        private int Steady=>DcV6Profile.Active?(Smoke?10:120):60;
        private int Repetitions=>DcV6Profile.Active&&!Smoke?int.Parse(DcBootstrap.Argument(DcV8Profile.Active?"-dc-v8-perf-repetitions":DcV7Profile.Active?"-dc-v7-perf-repetitions":"-dc-v6-perf-repetitions","3")):1;
        private bool Ranger=>scenario%2==0;
        private int tick;
        private int gc;
        private double maximumReset;
        private double maximumRegistration;
        private int maximumLoad;
        private int maximumContactParks;
        private string output;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize(){if(DcBootstrap.Argument("-dc-p5-authority-probe","false")=="true")new GameObject("Isolated P5 authority CPU fixture").AddComponent<DcP5AuthorityProbe>();}
        private void Start()
        {
            QualitySettings.vSyncCount=0;Application.targetFrameRate=60;Application.runInBackground=true;
            var content=Path.Combine(Application.dataPath,"..","Content");parameters=JObject.Parse(File.ReadAllText(Path.Combine(content,DcV8Profile.Active?"parameters-v8-p5.json":DcV7Profile.Active?"parameters-v7-p5.json":DcV6Profile.Active?"parameters-v6-p5.json":"parameters-v5-p4.json")));geometry=JObject.Parse(File.ReadAllText(Path.Combine(content,DcV8Profile.Active?"world-v8-p5.json":DcV7Profile.Active?"world-v7-p5.json":DcV6Profile.Active?"world-v6-p5.json":"world-v5-p4.json")));configuration=JObject.Parse(File.ReadAllText(Path.Combine(content,DcV8Profile.Active?"demo-v8-p5.json":DcV7Profile.Active?"demo-v7-p5.json":DcV6Profile.Active?"demo-v6-p5.json":"demo-v5-p5.json")));
            output=Path.GetFullPath(DcBootstrap.Argument("-dc-p5-authority-output"));if(!output.StartsWith(Path.GetFullPath(DcBootstrap.DemoEvidenceRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Authority probe path");Begin();
        }
        private static double Milliseconds(long start)=>(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000d/System.Diagnostics.Stopwatch.Frequency;
        private static JObject Statistics(List<double> values){values.Sort();return new JObject{["samples"]=values.Count,["median_ms"]=values.Count>0?values[values.Count/2]:0,["p95_ms"]=values.Count>0?values[(int)(values.Count*0.95)]:0,["p99_ms"]=values.Count>0?values[(int)(values.Count*0.99)]:0};}
        private void Begin(){if(Repetitions!=1&&Repetitions!=3)throw new InvalidOperationException("Authority repetitions");world=DcP5PressureFixture.Create(parameters,geometry,configuration,Ranger?"ranger":"runesmith",out initial);if(world.V6){world.Restore(initial);initialRuntime=world.CaptureRuntimeCheckpoint();}samples.Clear();checkpointSamples.Clear();tickSamples.Clear();snapshotSamples.Clear();lifecycleSamples.Clear();phaseSamples.Clear();slowTicks.Clear();tick=0;maximumLoad=0;maximumContactParks=0;maximumReset=0;maximumRegistration=0;gc=GC.CollectionCount(0);started=Time.realtimeSinceStartupAsDouble;next=started;world.TickPhaseObserver=(name,ms)=>{if(Time.realtimeSinceStartupAsDouble-started<Warmup)return;if(!phaseSamples.TryGetValue(name,out var list))phaseSamples[name]=list=new List<double>();list.Add(ms);};}
        private void Update()
        {
            if(Time.realtimeSinceStartupAsDouble-started>=Warmup+Steady)
            {
                if(samples.Count==0){enabled=false;File.WriteAllText(output,new JObject{["profile"]=DcV8Profile.Active?"dc_ruins.v8.p5.1":DcV7Profile.Active?"dc_ruins.v7.p5.2":DcV6Profile.Active?"dc_ruins.v6.p5.2":"dc_ruins.v5.p5.1",["status"]="FAIL_NO_STEADY_SAMPLES",["scenario"]=scenario,["human"]=false,["real_udp"]=false}.ToString());Application.Quit(3);return;}
                samples.Sort();results.Add(new JObject{["loadout"]=Ranger?"ranger":"runesmith",["players"]=4,["enemies"]=24,["projectiles"]=1024,["turrets"]=Ranger?0:8,["samples"]=samples.Count,["warmup_seconds"]=Warmup,["steady_seconds"]=Steady,["repetition"]=scenario/2+1,["median_ms"]=samples[samples.Count/2],["p95_ms"]=samples[(int)(samples.Count*0.95)],["p99_ms"]=samples[(int)(samples.Count*0.99)],["maximum_ms"]=samples.Last(),["maximum_fixture_reset_ms"]=maximumReset,["maximum_load"]=maximumLoad,["maximum_contact_budget_parks"]=maximumContactParks,["gc0_collections"]=GC.CollectionCount(0)-gc,["tick_p99_budget_met"]=samples[(int)(samples.Count*0.99)]<=33.333&&maximumContactParks==0});
                results.Last["fixture_version"]=DcV8Profile.Active?DcV8PressureFixture.Version:DcV7Profile.Active?"v7_legal_body_flight_positions_2":DcV6Profile.Active?"v6_current_projectile_bodies_2":"v5_original";results.Last["initial_projectile_radius"]=(float)initial["projectiles"][0]["CollisionRadius"];results.Last["initial_projectile_speed"]=(float)initial["projectiles"][0]["Speed"];results.Last["initial_projectile_bounces"]=(int?)initial["projectiles"][0]["Bounces"]??0;
                results.Last["stage_ms"]=new JObject{["checkpoint"]=Statistics(checkpointSamples),["simulation_tick"]=Statistics(tickSamples),["runtime_snapshot"]=Statistics(snapshotSamples),["lifecycle_drain"]=Statistics(lifecycleSamples)};
                results.Last["maximum_fixture_lifecycle_registration_ms"]=maximumRegistration;results.Last["fixture_restore_primes_existing_flights_before_steady_tick"]=DcV6Profile.Active;
                var phases=new JObject();foreach(var pair in phaseSamples)phases[pair.Key]=Statistics(pair.Value);results.Last["simulation_phases"]=phases;
                results.Last["gc_incremental_enabled"]=UnityEngine.Scripting.GarbageCollector.isIncremental;results.Last["gc_mode"]=UnityEngine.Scripting.GarbageCollector.GCMode.ToString();results.Last["gc_slice_ns"]=UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds;results.Last["slow_ticks"]=slowTicks.DeepClone();
                if(++scenario<2*Repetitions){Begin();return;}
                File.WriteAllText(output,new JObject{["profile"]=DcV8Profile.Active?"dc_ruins.v8.p5.1":DcV7Profile.Active?"dc_ruins.v7.p5.2":DcV6Profile.Active?"dc_ruins.v6.p5.2":"dc_ruins.v5.p5.1",["smoke_only"]=Smoke,["scope"]="actual_standalone_headless_modified_world_tick_snapshot_checkpoint_fixture",["modified_world_state"]=true,["human"]=false,["real_udp"]=false,["unity"]=Application.unityVersion,["cpu"]=SystemInfo.processorType,["memory_mb"]=SystemInfo.systemMemorySize,["job_worker_maximum"]=Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerMaximumCount,["job_workers"]=Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount,["authority_hz"]=30,["player_loop_cap"]=60,["v_sync"]=QualitySettings.vSyncCount,["checkpoint_kind"]=DcV6Profile.Active?"typed_projectiles_terrain_and_json_control_state":"typed_projectiles_and_json_control_state",["reset_included_in_tick_budget"]=false,["reset_cost_recorded_separately"]=true,["results"]=results}.ToString());Application.Quit(0);return;
            }
            if(Time.realtimeSinceStartupAsDouble<next)return;
            if(tick%30==0){var reset=System.Diagnostics.Stopwatch.GetTimestamp();if(world.V6)world.Restore(initialRuntime);else world.Restore(initial);if(world.V6){var registration=System.Diagnostics.Stopwatch.GetTimestamp();world.DrainV6LifecycleRecords();maximumRegistration=Math.Max(maximumRegistration,Milliseconds(registration));}maximumReset=Math.Max(maximumReset,Milliseconds(reset));}
            var window=tick%30+1;var steady=Time.realtimeSinceStartupAsDouble-started>=Warmup;var beforeGC=GC.CollectionCount(0);var clock=System.Diagnostics.Stopwatch.GetTimestamp();var part=clock;
            if(window%8==0){world.CaptureRuntimeCheckpoint();if(steady)checkpointSamples.Add(Milliseconds(part));}
            maximumLoad=Math.Max(maximumLoad,world.ProjectileLoad);part=System.Diagnostics.Stopwatch.GetTimestamp();world.Tick((window*1000+29)/30);if(steady)tickSamples.Add(Milliseconds(part));
            if(steady)foreach(var diagnostic in new[]{("lifecycle_encoding_nested",world.V6LifecycleEncodingMS),("contact_queries_nested",world.V6ContactQueryMS)}){if(!phaseSamples.TryGetValue(diagnostic.Item1,out var list))phaseSamples[diagnostic.Item1]=list=new List<double>();list.Add(diagnostic.Item2);}
            if(world.Terminal){File.WriteAllText(output,new JObject{["profile"]=DcV8Profile.Active?"dc_ruins.v8.p5.1":DcV7Profile.Active?"dc_ruins.v7.p5.2":"inherited",["status"]="FAIL_FIXTURE_AUTHORITY_TERMINATED",["scenario"]=scenario,["human"]=false,["real_udp"]=false}.ToString());Application.Quit(3);enabled=false;return;}
            maximumContactParks=Math.Max(maximumContactParks,world.V7ContactBudgetParks);part=System.Diagnostics.Stopwatch.GetTimestamp();var snapshot=world.RuntimeSnapshot();if(steady)snapshotSamples.Add(Milliseconds(part));
            part=System.Diagnostics.Stopwatch.GetTimestamp();if(world.V6)world.DrainV6LifecycleRecords();if(steady)lifecycleSamples.Add(Milliseconds(part));
            var elapsed=Milliseconds(clock);if(steady){samples.Add(elapsed);var delta=GC.CollectionCount(0)-beforeGC;if(elapsed>25||delta>0){slowTicks.Add(new JObject{["window_tick"]=window,["elapsed_ms"]=elapsed,["gc_collections"]=delta,["lifecycle_encoding_ms"]=world.V6LifecycleEncodingMS,["contact_query_ms"]=world.V6ContactQueryMS,["projectiles"]=snapshot["projectile_load"],["managed_used_bytes"]=UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong()});while(slowTicks.Count>32)slowTicks.First.Remove();}}maximumLoad=Math.Max(maximumLoad,(int)snapshot["projectile_load"]);tick++;next+=1d/30;if(Time.realtimeSinceStartupAsDouble-next>0.15)next=Time.realtimeSinceStartupAsDouble;
        }
    }
}
