using UnityEngine;

namespace DungeonContract
{
    public sealed partial class DcP1World
    {
        private readonly System.Collections.Generic.Dictionary<string,DcP1Input> foundationInputs=new System.Collections.Generic.Dictionary<string,DcP1Input>();
        public void QueueFoundationInput(string id,DcP1Input input){if(P2&&technicalWaiting)return;foundationInputs[id]=input;}
        public void FlushFoundationInputs(long at){AdvanceTime(at);foreach(var pair in foundationInputs)Input(pair.Key,pair.Value);foundationInputs.Clear();}
        public void FreezeInputs(string reason="paused")
        {
            if(Weapons)while(queued.Count>0){var action=queued.Dequeue();RejectWeaponAction(action.Player,action.Intent,action.ID,reason);}
            queued.Clear();
            foundationInputs.Clear();
            waitingHeld.Clear();
            foreach(var player in players.Values){var aim=player.Input.Aim;player.Input=default;player.Input.Aim=aim;CancelWeaponBuffer(player);}
        }
        public void ResumeInputs()
        {
            queued.Clear();
            foreach(var player in players.Values){var aim=player.Input.Aim;player.Input=default;player.Input.Aim=aim;player.InputAt=now;CancelWeaponBuffer(player);player.Charging=false;player.InteractionTarget=null;if(V5){player.AttackEpisode=null;player.ChargeAction=null;player.ChargeRequested=false;player.ReleaseRequired=true;}}
        }
        public void StopMovement(string id)
        {
            if(players.TryGetValue(id,out var player)){player.Input=default;CancelWeaponBuffer(player);}
        }
        public long GameTime=>now;
    }
}
