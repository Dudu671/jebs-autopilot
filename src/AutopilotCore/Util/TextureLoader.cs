using System.IO;
using UnityEngine;

namespace AutopilotCore
{
    public static class TextureLoader
    {
        public static string TextureDirectory()
        {
            // PluginData is skipped by KSP's GameDatabase, so these PNGs are not loaded twice.
            return Path.Combine(KSPUtil.ApplicationRootPath, "GameData/MyAutopilot/PluginData/Textures");
        }

        public static Texture2D Load(string fileName)
        {
            string path = Path.Combine(TextureDirectory(), fileName);
            if (!File.Exists(path))
            {
                Debug.LogWarning("[AutopilotCore] Texture not found: " + path);
                return null;
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            texture.LoadImage(File.ReadAllBytes(path));
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 4;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        public static void Destroy(ref Texture2D texture)
        {
            if (texture != null)
            {
                Object.Destroy(texture);
                texture = null;
            }
        }
    }
}
