using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Profiling;

namespace DungeonContract
{
    public sealed class DcS1Metrics : MonoBehaviour
    {
        private readonly List<double> frames=new List<double>();
        private readonly Stopwatch wall=new Stopwatch();
        private readonly FrameTiming[] timings=new FrameTiming[1];
        private ProfilerRecorder allocations;
        private long allocated;
        private long initialAllocated;
        private double cpu;
        private double gpu;
        private int cpuSamples;
        private int gpuSamples;
        private int initialGC;
        private string output;
        private bool written;
        private int warmupMilliseconds;
        private int observedRoom=-1;
        private int maximumEnemies;
        private int maximumProjectiles;
        private readonly Dictionary<int,List<double>> roomFrames=new Dictionary<int,List<double>>();
        public void Observe(int room,int enemies,int projectiles){observedRoom=room;maximumEnemies=Math.Max(maximumEnemies,enemies);maximumProjectiles=Math.Max(maximumProjectiles,projectiles);}
        private void Start()
        {
            output=DcBootstrap.Argument("-dc-s1-metrics");
            if(string.IsNullOrEmpty(output)){enabled=false;return;}
            output=Path.GetFullPath(output);
            if(!output.StartsWith(Path.GetFullPath(DcBootstrap.DemoEvidenceRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Metrics outside Demo evidence");
            warmupMilliseconds=int.Parse(DcBootstrap.Argument("-dc-metrics-warmup-ms","30000"));if(warmupMilliseconds<0||warmupMilliseconds>60000)throw new InvalidOperationException("Metrics warmup range");initialGC=GC.CollectionCount(0);initialAllocated=GC.GetAllocatedBytesForCurrentThread();wall.Start();allocations=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1);
        }
        private void Update()
        {
            if(int.TryParse(DcBootstrap.Argument("-dc-s1-exit-after","0"),out var exitAfter)&&exitAfter>0&&wall.Elapsed.TotalSeconds>=exitAfter){Application.Quit(0);return;}
            if(wall.ElapsedMilliseconds<warmupMilliseconds)return;
            if(frames.Count<60000)frames.Add(Time.unscaledDeltaTime*1000d);
            if(observedRoom>=0){if(!roomFrames.TryGetValue(observedRoom,out var samples)){samples=new List<double>();roomFrames[observedRoom]=samples;}if(samples.Count<60000)samples.Add(Time.unscaledDeltaTime*1000d);}
            if(allocations.Valid)allocated+=Math.Max(0,allocations.LastValue);
            FrameTimingManager.CaptureFrameTimings();
            if(FrameTimingManager.GetLatestTimings(1,timings)>0){if(timings[0].cpuFrameTime>0){cpu+=timings[0].cpuFrameTime;cpuSamples++;}if(timings[0].gpuFrameTime>0){gpu+=timings[0].gpuFrameTime;gpuSamples++;}}
        }
        private double Percentile(double percentile){if(frames.Count==0)return 0;var values=frames.OrderBy(value=>value).ToArray();return values[Math.Min(values.Length-1,(int)Math.Ceiling(values.Length*percentile)-1)];}
        private static double RoomPercentile(List<double> samples,double percentile){var values=samples.OrderBy(value=>value).ToArray();return values[Math.Min(values.Length-1,(int)Math.Ceiling(values.Length*percentile)-1)];}
        private void Write()
        {
            if(written||string.IsNullOrEmpty(output))return;written=true;Directory.CreateDirectory(Path.GetDirectoryName(output));
            var stage=DcBootstrap.Argument("-dc-stage","p0");var value=new JObject {["profile"]=stage=="p4"?"dc_ruins.v4.p4.1":stage=="p3"?"dc_ruins.v4.p3.1":stage=="p2-mp"?"dc_ruins.v4.p2.multiplayer":stage=="p2"?"dc_ruins.v4.p2.1":stage=="s2"?"dc_ruins.v4.s2":"dc_ruins.v4.s1",["scope"]="actual_player_render_loop",["duration_wall_ms"]=wall.ElapsedMilliseconds,["warmup_ms"]=warmupMilliseconds,["frames"]=frames.Count,["median_ms"]=Percentile(0.5),["p95_ms"]=Percentile(0.95),["p99_ms"]=Percentile(0.99),["gc0_collections"]=GC.CollectionCount(0)-initialGC,["allocation_recorder_available"]=allocations.Valid,["allocated_bytes"]=allocated,["mono_used_bytes"]=Profiler.GetMonoUsedSizeLong(),["cpu_frame_mean_ms"]=cpuSamples>0?cpu/cpuSamples:0,["cpu_timing_available"]=cpuSamples>0,["gpu_frame_mean_ms"]=gpuSamples>0?gpu/gpuSamples:0,["gpu_timing_available"]=gpuSamples>0,["gpu"]=SystemInfo.graphicsDeviceName,["graphics_api"]=SystemInfo.graphicsDeviceType.ToString(),["cpu"]=SystemInfo.processorType,["memory_mb"]=SystemInfo.systemMemorySize,["resolution"]=Screen.width+"x"+Screen.height,["human_acceptance"]=false};
            if(DcBootstrap.Argument("-dc-runtime","v4")=="v5")value["profile"]="dc_ruins.v5.p4.1";
            if(DcP5Settings.Active){value["profile"]="dc_ruins.v5.p5.1";value["presentation_version"]=DcP5Settings.Version;}
            if(DcV6Profile.Active){value["profile"]="dc_ruins.v6.p5.2";value["allocation_recorder_scope"]="steady_capture_after_warmup";if(!allocations.Valid)value["allocated_bytes"]=JValue.CreateNull();if(cpuSamples==0)value["cpu_frame_mean_ms"]=JValue.CreateNull();if(gpuSamples==0)value["gpu_frame_mean_ms"]=JValue.CreateNull();}
            if(DcV7Profile.Active)value["profile"]="dc_ruins.v7.p5.2";
            if(DcV8Profile.Active)value["profile"]="dc_ruins.v8.p5.1";
            if(DcV7Profile.Active){value["job_worker_maximum"]=Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerMaximumCount;value["job_workers"]=Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount;}
            value["observed_max_enemies"]=maximumEnemies;value["observed_max_projectiles"]=maximumProjectiles;value["rooms"]=new JArray(roomFrames.OrderBy(item=>item.Key).Select(item=>new JObject{["room_id"]=item.Key,["frames"]=item.Value.Count,["median_ms"]=RoomPercentile(item.Value,0.5),["p95_ms"]=RoomPercentile(item.Value,0.95),["p99_ms"]=RoomPercentile(item.Value,0.99)}));
            var mainAllocated=GC.GetAllocatedBytesForCurrentThread()-initialAllocated;value["main_thread_allocation_counter_available"]=mainAllocated>0;value["main_thread_allocated_bytes"]=mainAllocated>0?new JValue(mainAllocated):JValue.CreateNull();value["allocation_scope"]="whole_capture_including_warmup";File.WriteAllText(output,value.ToString());
        }
        private void OnApplicationQuit(){Write();}
        public void Flush(){Write();}
        private void OnDestroy(){Write();if(allocations.Valid)allocations.Dispose();}
    }

    public sealed class DcS1Probe : MonoBehaviour
    {
        public DcP0V3 Runtime;
        private double start=-1;
        private int step;
        private long frozenFrame;
        private string frozen;
        private double pauseStarted;
        private readonly JObject result=new JObject {["real_client"]=true,["human"]=false};
        private async void Update()
        {
            if(Runtime==null||!Runtime.ProbeReady)return;
            if(start<0)start=Time.realtimeSinceStartupAsDouble;
            try
            {
                if(step==0&&Time.realtimeSinceStartupAsDouble-start>2){step=1;await Runtime.ProbePause(true);}
                if(step==1&&Runtime.ProbeFrozenReady){step=2;pauseStarted=Time.realtimeSinceStartupAsDouble;frozenFrame=(long)Runtime.ProbeSnapshot["world_frame_id"];frozen=PlayerState();}
                if(step==2&&Time.realtimeSinceStartupAsDouble-pauseStarted>2)
                {
                    result["paused"]=Runtime.ProbePaused;result["world_frame_frozen"]=(long)Runtime.ProbeSnapshot["world_frame_id"]==frozenFrame;result["player_state_frozen"]=PlayerState()==frozen;result["timescale_frozen"]=Time.timeScale==0;step=3;await Runtime.ProbePause(false);
                }
                if(step==3&&!Runtime.ProbePaused){result["resumed"]=true;step=4;pauseStarted=Time.realtimeSinceStartupAsDouble;}
                if(step==4&&Time.realtimeSinceStartupAsDouble-pauseStarted>2){result["world_resumed"]=(long)Runtime.ProbeSnapshot["world_frame_id"]>frozenFrame;Write();Application.Quit(0);}
                if(Time.realtimeSinceStartupAsDouble-start>20){result["timeout"]=true;Write();Application.Quit(3);}
            }catch(Exception){result["failed"]=true;Write();Application.Quit(3);}
        }
        private void Write()
        {
            var output=DcBootstrap.Argument("-dc-s1-probe-output");if(string.IsNullOrEmpty(output))return;
            var path=Path.GetFullPath(output);if(!path.StartsWith(Path.GetFullPath(DcBootstrap.DemoEvidenceRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Probe output path");
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,result.ToString());
        }
        private string PlayerState(){var players=(JArray)Runtime.ProbeSnapshot["players"].DeepClone();foreach(var player in players){player["x"]=Mathf.RoundToInt((float)player["x"]*1000);player["y"]=Mathf.RoundToInt((float)player["y"]*1000);}return players.ToString();}
    }
}
