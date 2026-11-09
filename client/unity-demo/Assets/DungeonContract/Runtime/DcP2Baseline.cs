using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DungeonContract
{
    public sealed class DcP2Baseline
    {
        private int revision;
        private uint transfer;
        private byte[][] parts;
        private byte[] digest;
        private int total;
        private int received;
        private double expires;
        public int MaximumPacket {get;private set;}
        public int ProtocolVersion {get;set;}=4;
        public static byte[][] Encode(JObject value,uint transferID,long source,long connection,int protocol=4)
        {
            var bytes=Encoding.UTF8.GetBytes(value.ToString(Formatting.None));if(bytes.Length<1||bytes.Length>262144)throw new InvalidDataException("Baseline total budget");
            using var hash=SHA256.Create();var fingerprint=hash.ComputeHash(bytes);var count=(bytes.Length+767)/768;var packets=new byte[count][];
            for(var index=0;index<count;index++)
            {
                using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);writer.Write((byte)protocol);writer.Write((int)value["state_revision"]);writer.Write(transferID);writer.Write(source);writer.Write(connection);writer.Write(bytes.Length);writer.Write((ushort)index);writer.Write((ushort)count);writer.Write(fingerprint);writer.Write(bytes,index*768,Math.Min(768,bytes.Length-index*768));packets[index]=stream.ToArray();
            }
            return packets;
        }
        public JObject Receive(byte[] packet,long source,long connection,double wall,int appliedRevision)
        {
            if(packet.Length<66||packet.Length>900)throw new InvalidDataException("Baseline packet budget");MaximumPacket=Math.Max(MaximumPacket,packet.Length);
            using var stream=new MemoryStream(packet);using var reader=new BinaryReader(stream);if(reader.ReadByte()!=ProtocolVersion)throw new InvalidDataException("Baseline protocol");
            var nextRevision=reader.ReadInt32();var nextTransfer=reader.ReadUInt32();if(reader.ReadInt64()!=source||reader.ReadInt64()!=connection)throw new InvalidDataException("Baseline identity");
            var length=reader.ReadInt32();var index=reader.ReadUInt16();var count=reader.ReadUInt16();var fingerprint=reader.ReadBytes(32);var remaining=(int)(stream.Length-stream.Position);
            if(nextRevision<1||nextTransfer==0||length<1||length>262144||count!=(length+767)/768||index>=count||remaining!=Math.Min(768,length-index*768))throw new InvalidDataException("Baseline shape");
            if(nextRevision<=appliedRevision||nextRevision<revision)return null;
            if(parts==null||nextRevision>revision||wall>=expires){revision=nextRevision;transfer=nextTransfer;total=length;digest=fingerprint;parts=new byte[count][];received=0;expires=wall+2;}
            if(transfer!=nextTransfer||length!=total||count!=parts.Length||!fingerprint.SequenceEqual(digest))throw new InvalidDataException("Baseline chunk conflict");
            var payload=reader.ReadBytes(remaining);if(parts[index]!=null){if(!parts[index].SequenceEqual(payload))throw new InvalidDataException("Baseline duplicate conflict");return null;}
            parts[index]=payload;received++;if(received!=parts.Length)return null;
            var bytes=new byte[total];for(var part=0;part<parts.Length;part++)Buffer.BlockCopy(parts[part],0,bytes,part*768,parts[part].Length);
            using var hash=SHA256.Create();if(!hash.ComputeHash(bytes).SequenceEqual(digest))throw new InvalidDataException("Baseline fingerprint");parts=null;return JObject.Parse(new UTF8Encoding(false,true).GetString(bytes),new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error,CommentHandling=CommentHandling.Ignore});
        }
        public void Clear(){parts=null;received=0;revision=0;transfer=0;}
    }
}
