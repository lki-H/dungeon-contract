using UnityEditor;

namespace DungeonContract.Editor
{
    public sealed class DcP5Imports : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if(!assetPath.Contains("/Resources/DungeonContract/P5/"))return;
            var importer=(TextureImporter)assetImporter;importer.textureType=TextureImporterType.Default;importer.filterMode=UnityEngine.FilterMode.Point;importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=2048;
        }
        private void OnPreprocessAudio()
        {
            if(!assetPath.Contains("/Resources/DungeonContract/P5/"))return;
            var importer=(AudioImporter)assetImporter;importer.forceToMono=true;var settings=importer.defaultSampleSettings;settings.loadType=assetPath.EndsWith("music.wav")?UnityEngine.AudioClipLoadType.Streaming:UnityEngine.AudioClipLoadType.DecompressOnLoad;settings.compressionFormat=UnityEngine.AudioCompressionFormat.Vorbis;settings.quality=0.7f;importer.defaultSampleSettings=settings;
        }
    }
}
