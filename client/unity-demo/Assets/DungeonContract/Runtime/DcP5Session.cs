using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class DcP0V3
    {
        private DcP5Lobby p5Lobby;
        private string p5LobbyRun;
        private string p5LobbyToken;
        private string p5LobbyPlayer;
        private bool p5Returning;
        private string p5ServiceBoot;
        internal bool P5Loaded=>loadedConfirmed&&s1ClientBaselineReady;
        internal long P5Generation=>sourceGeneration;
        internal string P5Assignment=>(string)assignment?["assignment_id"];
        internal string P5Loadout=>(string)p1Snapshot?["players"].First(item=>(string)item["player_id"]==playerID)["loadout_id"];
        internal void AdoptP5ServiceBoot(string value){p5ServiceBoot=value;}
        internal void AdoptP5Session(DcP5Lobby lobby,string run,string token,string player){p5Lobby=lobby;p5LobbyRun=run;p5LobbyToken=token;p5LobbyPlayer=player;}
        internal void PrepareP5Return()
        {
            p5Returning=true;loadedConfirmed=false;p1Controls?.SetEnabled(false);
            // Cancellation precedes transport shutdown and control takeover, so
            // pending Join/handshake/reconnect tasks cannot request a new admission.
            if(!stopping.IsCancellationRequested)stopping.Cancel();
            network?.Shutdown();s1Control?.Dispose();s1Control=null;
        }
        private async void ReturnToP5Lobby()
        {
            if(p5Returning||p5Lobby==null)return;p5Returning=true;
            try{p1Controls?.SetMenu(true);await p5Lobby.ReturnFromRun(p1ResultConfirmed);}finally{p5Returning=false;}
        }
        private void P5JoinReady()
        {
            if(p1View==null)return;
            p1View.P5Roster((Newtonsoft.Json.Linq.JArray)assignment["members"]);
            if(p5Lobby==null)return;
            p5Lobby.RememberRoster((Newtonsoft.Json.Linq.JArray)assignment["members"]);p1View.Exit=ReturnToP5Lobby;
        }
        private async Task RotateP5Service()
        {
            var previous=(string)assignment?["assignment_id"];
            var deadline=Time.realtimeSinceStartupAsDouble+60;
            while(!stopping.IsCancellationRequested&&Time.realtimeSinceStartupAsDouble<deadline)
            {
                await Internal("instances/register",new Newtonsoft.Json.Linq.JObject{["instance_id"]="dc_local_01",["boot_id"]=boot,["protocol_version"]=Protocol,["versions"]=versions,["config_versions"]=new Newtonsoft.Json.Linq.JArray(new Newtonsoft.Json.Linq.JObject{["definition_id"]=configuration["definition_id"],["hash"]=configHash})});
                var next=await Internal("instances/dc_local_01/claim",new Newtonsoft.Json.Linq.JObject{["boot_id"]=boot,["versions"]=versions});
                if((string)(next["assignment"] as Newtonsoft.Json.Linq.JObject)?["assignment_id"]!=previous)
                {
                    var rotation=new GameObject("P5 authority rotation").AddComponent<DcP5ServiceRotation>();rotation.Begin(gameObject,boot);return;
                }
                await Task.Delay(500,stopping.Token);
            }
            throw new InvalidOperationException("P5 previous Run release exceeded its bounded deadline");
        }
    }

    public sealed class DcP5ServiceRotation : MonoBehaviour
    {
        public void Begin(GameObject previous,string boot){StartCoroutine(Rotate(previous,boot));}
        private System.Collections.IEnumerator Rotate(GameObject previous,string boot)
        {
            Destroy(previous);yield return null;yield return null;
            var next=new GameObject("P5 authority session").AddComponent<DcP0V3>();next.AdoptP5ServiceBoot(boot);Destroy(gameObject);
        }
    }
}
