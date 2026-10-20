using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

namespace LineLab.Editor
{
    public static class BackendValidation
    {
        public static string Capture(string technique)
        {
            var lab = UnityEngine.Object.FindFirstObjectByType<LineLabController>();
            if (lab == null)
            {
                throw new Exception("Enter Play mode first");
            }

            var t = (Technique)Enum.Parse(typeof(Technique), technique);
            var previous = lab.Backend?.Root;
            var previousActors = GameObject.Find("Placeholder units / presentation only");
            lab.Configure(t, 8, 32, Workload.Moving, true);
            if (previous != null)
            {
                UnityEngine.Object.DestroyImmediate(previous);
            }

            if (previousActors != null)
            {
                UnityEngine.Object.DestroyImmediate(previousActors);
            }

            typeof(LineLabController)
                .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(lab, null);
            typeof(LineLabController)
                .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(lab, null);
            foreach (var effect in lab.Backend.Root.GetComponentsInChildren<VisualEffect>())
            {
                effect.Simulate(1f / 60, 4);
            }

            var camera = Camera.main;
            var old = camera.targetTexture;
            var rt = new RenderTexture(
                1920,
                1080,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB
            );
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            Directory.CreateDirectory("Screenshots/Validation");
            File.WriteAllBytes("Screenshots/Validation/" + technique + ".png", tex.EncodeToPNG());
            camera.targetTexture = old;
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            int actual =
                lab.Backend.Root.GetComponentsInChildren<ParticleSystem>().Sum(p => p.particleCount)
                + lab.Backend.Root.GetComponentsInChildren<VisualEffect>()
                    .Sum(v => (int)v.aliveParticleCount);
            return technique
                + ": "
                + actual
                + " particles / "
                + lab.Backend.ObjectCount
                + " objects / GPU instancing "
                + lab.Backend.InstancingActive;
        }
    }
}
