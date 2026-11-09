using System.Linq;
using Newtonsoft.Json.Linq;

namespace DungeonContract
{
    public sealed partial class DcP1World
    {
        public sealed class RuntimeCheckpoint
        {
            private readonly JObject state;
            private readonly Projectile[] flights;
            private readonly Cover[] blocks;
            private readonly Trap[] hazards;
            internal RuntimeCheckpoint(DcP1World world){state=world.Capture(false,!world.V6);flights=world.projectiles.Select(Copy).ToArray();if(world.V6){blocks=world.covers.Select(Copy).ToArray();hazards=world.traps.Select(Copy).ToArray();}}
            private static Cover Copy(Cover source)=>new Cover{ID=source.ID,Room=source.Room,Position=source.Position,Health=source.Health,Revision=source.Revision};
            private static Trap Copy(Trap source){var copy=new Trap{ID=source.ID,Room=source.Room,Position=source.Position,Phase=source.Phase,Group=source.Group,Epoch=source.Epoch,ActivationGeneration=source.ActivationGeneration,Cycle=source.Cycle,Until=source.Until,Trigger=source.Trigger};copy.Hit.UnionWith(source.Hit);return copy;}
            private static Projectile Copy(Projectile source)
            {
                var copy=new Projectile{ProtocolVersion=source.ProtocolVersion,BornGameMS=source.BornGameMS,ExpiresGameMS=source.ExpiresGameMS,EmissionIndex=source.EmissionIndex,Episode=source.Episode,ContactStalls=source.ContactStalls,SourceLife=source.SourceLife,EmissionFrame=source.EmissionFrame,ID=source.ID,Action=source.Action,Owner=source.Owner,Room=source.Room,Position=source.Position,Direction=source.Direction,Remaining=source.Remaining,Speed=source.Speed,Damage=source.Damage,Enemy=source.Enemy,Penetration=source.Penetration,Bounces=source.Bounces,CollisionRadius=source.CollisionRadius,RuneBolt=source.RuneBolt,ChainTriggered=source.ChainTriggered,Unbounded=source.Unbounded,ChainEnabled=source.ChainEnabled,FrostEnabled=source.FrostEnabled,Root=source.Root,NetworkGroup=source.NetworkGroup,MuzzlePending=source.MuzzlePending,MuzzleOrigin=source.MuzzleOrigin,TrajectoryRevision=source.TrajectoryRevision,TerminationReason=source.TerminationReason};copy.Hit.UnionWith(source.Hit);copy.Inside.UnionWith(source.Inside);foreach(var pair in source.Entries)copy.Entries[pair.Key]=pair.Value;return copy;
            }
            internal void Restore(DcP1World world){world.Restore(state);world.projectiles.AddRange(flights.Select(Copy));if(blocks!=null){world.covers.AddRange(blocks.Select(Copy));world.traps.AddRange(hazards.Select(Copy));foreach(var flight in world.projectiles)world.v6Flights[flight.ID]=new LifeV6{Flight=flight};}}
        }
        public RuntimeCheckpoint CaptureRuntimeCheckpoint()=>new RuntimeCheckpoint(this);
        public void Restore(RuntimeCheckpoint checkpoint)=>checkpoint.Restore(this);
    }
}
