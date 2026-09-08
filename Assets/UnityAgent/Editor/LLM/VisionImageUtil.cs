using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UnityAgent.Editor.LLM
{
    public static class VisionImageUtil
    {
        public static string ToBase64(string path, int maxBytes = 4_000_000)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > maxBytes)
            {
                // Downscale PNG/JPG via Texture2D when possible.
                try
                {
                    var tex = new Texture2D(2, 2);
                    if (tex.LoadImage(bytes))
                    {
                        var w = Mathf.Min(tex.width, 1280);
                        var h = Mathf.RoundToInt(tex.height * (w / (float)tex.width));
                        var rt = RenderTexture.GetTemporary(w, h);
                        Graphics.Blit(tex, rt);
                        var prev = RenderTexture.active;
                        RenderTexture.active = rt;
                        var scaled = new Texture2D(w, h, TextureFormat.RGB24, false);
                        scaled.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                        scaled.Apply();
                        RenderTexture.active = prev;
                        RenderTexture.ReleaseTemporary(rt);
                        bytes = scaled.EncodeToPNG();
                        UnityEngine.Object.DestroyImmediate(tex);
                        UnityEngine.Object.DestroyImmediate(scaled);
                    }
                }
                catch
                {
                    // fall through with original if too big — provider may reject
                }
            }
            return Convert.ToBase64String(bytes);
        }

        public static string Mime(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                _ => "image/png"
            };
        }
    }
}
