using System;
using System.Collections.Generic;

namespace DungeonContract
{
    public sealed class DcV5BaselineTracker
    {
        private sealed class Entry {public int? ConfirmedRoom;public int Revision;public int FirstRevision;public int Room;}
        private readonly Dictionary<ulong,Entry> entries=new Dictionary<ulong,Entry>();
        public bool Complete(ulong connection,int room)=>!entries.TryGetValue(connection,out var entry)||entry.ConfirmedRoom!=room;
        public void Sent(ulong connection,int room,int revision)
        {
            if(!entries.TryGetValue(connection,out var entry)){if(entries.Count>=4)throw new InvalidOperationException("V5 baseline roster");entries[connection]=entry=new Entry();}
            if(entry.FirstRevision==0||entry.Room!=room){entry.ConfirmedRoom=null;entry.FirstRevision=revision;}entry.Room=room;entry.Revision=revision;
        }
        public void Acknowledge(ulong connection,int revision){if(entries.TryGetValue(connection,out var entry)&&revision>=entry.FirstRevision&&revision<=entry.Revision)entry.ConfirmedRoom=entry.Room;}
        public void Remove(ulong connection)=>entries.Remove(connection);
        public void Clear()=>entries.Clear();
    }
}
