using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DungeonContract
{
    public sealed partial class DcBootstrap
    {
        private readonly Dictionary<ushort, byte[]> snapshotParts = new Dictionary<ushort, byte[]>();
        private long receivingFrame = -1;
        private ushort receivingCount;
        private double receivingStarted;

        private void BroadcastQualitySnapshot(string encoded, long frame)
        {
            var data = Encoding.UTF8.GetBytes(encoded);
            byte[] compressed;
            using (var buffer = new MemoryStream())
            {
                using (var compressor = new DeflateStream(buffer, System.IO.Compression.CompressionLevel.Fastest, true)) compressor.Write(data, 0, data.Length);
                compressed = buffer.ToArray();
            }
            if (compressed.Length > 14400) throw new InvalidDataException("Compressed world budget");
            var count = (ushort)((compressed.Length + 899) / 900);
            foreach (var connection in connected.Keys)
            {
                for (ushort index = 0; index < count; index++)
                {
                    var size = (ushort)Math.Min(900, compressed.Length - index * 900);
                    using var writer = new FastBufferWriter(1024, Allocator.Temp);
                    writer.WriteValueSafe(frame); writer.WriteValueSafe(index); writer.WriteValueSafe(count); writer.WriteValueSafe(size);
                    for (var offset = 0; offset < size; offset++) writer.WriteValueSafe(compressed[index * 900 + offset]);
                    network.CustomMessagingManager.SendNamedMessage("dc.world.part", connection, writer, NetworkDelivery.Unreliable);
                }
            }
        }

        private void OnQualityPart(ulong connection, FastBufferReader reader)
        {
            if (role != "client" || connection != NetworkManager.ServerClientId || reader.Length < 14 || reader.Length > 1024) return;
            try
            {
                reader.ReadValueSafe(out long frame); reader.ReadValueSafe(out ushort index); reader.ReadValueSafe(out ushort count); reader.ReadValueSafe(out ushort size);
                if (frame < 0 || count == 0 || count > 16 || index >= count || size == 0 || size > 900 || reader.Length - reader.Position != size || frame < receivingFrame || lastSnapshot != null && frame <= (long?)lastSnapshot["world_frame_id"]) return;
                if (frame > receivingFrame || Time.realtimeSinceStartupAsDouble - receivingStarted > 0.15) { snapshotParts.Clear(); receivingFrame = frame; receivingCount = count; receivingStarted = Time.realtimeSinceStartupAsDouble; }
                if (count != receivingCount || snapshotParts.ContainsKey(index)) return;
                var data = new byte[size]; for (var offset = 0; offset < size; offset++) reader.ReadValueSafe(out data[offset]); snapshotParts[index] = data;
                if (snapshotParts.Count != count) return;
                using var compressed = new MemoryStream(); for (ushort part = 0; part < count; part++) compressed.Write(snapshotParts[part], 0, snapshotParts[part].Length);
                compressed.Position = 0; using var decompressor = new DeflateStream(compressed, CompressionMode.Decompress); using var plain = new MemoryStream();
                var chunk = new byte[1024]; int read;
                while ((read = decompressor.Read(chunk, 0, chunk.Length)) > 0) { if (plain.Length + read > 60000) throw new InvalidDataException("World decompression budget"); plain.Write(chunk, 0, read); }
                var incoming = JObject.Parse(Encoding.UTF8.GetString(plain.ToArray()));
                if ((long)incoming["world_frame_id"] != frame || (string)incoming["run_id"] != runID) throw new InvalidDataException("World binding");
                lastSnapshot = incoming; view.Receive(incoming); snapshotParts.Clear();
                if ((string)incoming["run_status"] == "ending") _ = RecoverResult();
            }
            catch (Exception error) { snapshotParts.Clear(); Fail(error); }
        }
    }
}
