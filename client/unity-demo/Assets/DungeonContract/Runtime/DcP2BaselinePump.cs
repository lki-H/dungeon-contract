using System;
using System.Collections.Generic;

namespace DungeonContract
{
    public sealed class DcP2BaselinePump
    {
        private sealed class Stream {public byte[][] Active;public int Next;public byte[][] Pending;}
        private readonly Dictionary<ulong,Stream> streams=new Dictionary<ulong,Stream>();
        public int MaximumPendingBytes {get;private set;}
        public void Enqueue(ulong connection,byte[][] packets)
        {
            if(packets.Length<1||packets.Length>342)throw new InvalidOperationException("Baseline pump transfer budget");var bytes=0;foreach(var packet in packets){if(packet.Length>900)throw new InvalidOperationException("Baseline pump packet budget");bytes+=packet.Length;}
            if(!streams.TryGetValue(connection,out var stream)){if(streams.Count>=4)throw new InvalidOperationException("Baseline pump roster budget");stream=new Stream();streams[connection]=stream;}
            if(stream.Active==null){stream.Active=packets;stream.Next=0;}else stream.Pending=packets;
            var pending=0;foreach(var item in streams.Values){if(item.Active!=null)for(var index=item.Next;index<item.Active.Length;index++)pending+=item.Active[index].Length;if(item.Pending!=null)foreach(var packet in item.Pending)pending+=packet.Length;}MaximumPendingBytes=Math.Max(MaximumPendingBytes,pending);
        }
        public int Drain(ulong connection,int maximumPackets,Action<byte[]> send)
        {
            if(maximumPackets<1||maximumPackets>8)throw new InvalidOperationException("Baseline pump frame budget");if(!streams.TryGetValue(connection,out var stream)||stream.Active==null)return 0;
            var sent=0;while(sent<maximumPackets&&stream.Active!=null){send(stream.Active[stream.Next++]);sent++;if(stream.Next==stream.Active.Length){stream.Active=stream.Pending;stream.Pending=null;stream.Next=0;}}
            return sent;
        }
        public void Remove(ulong connection){streams.Remove(connection);}
    }
}
