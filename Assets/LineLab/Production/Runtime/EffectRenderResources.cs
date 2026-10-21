using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

namespace LineLab.Production
{
    /// <summary>One immutable GPU copy of a baked library, shared by every pooled component.</summary>
    public sealed class EffectRenderResources : IDisposable
    {
        public readonly NativeEffectCatalog Catalog;
        public readonly GraphicsBuffer Effects;
        public readonly GraphicsBuffer Ribbons;
        public readonly GraphicsBuffer Layers;
        public readonly GraphicsBuffer Ramps;

        public EffectRenderResources(NativeEffectCatalog catalog)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            try
            {
                Effects = Upload(catalog.Effects.Reinterpret<float4>(96));
                Ribbons = Upload(catalog.Ribbons.Reinterpret<float4>(48));
                Layers = Upload(catalog.Layers.Reinterpret<float4>(96));
                Ramps = Upload(catalog.Ramps);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        static GraphicsBuffer Upload(NativeArray<float4> values)
        {
            var buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                math.max(1, values.Length),
                16
            );
            if (values.Length > 0)
            {
                buffer.SetData(values);
            }

            return buffer;
        }

        public Material Material(string name)
        {
            var source = Resources.Load<Material>("Production/Generated/" + name);
            if (source == null)
            {
                throw new InvalidOperationException(
                    "Compile this effect library in the Effect Workbench. Missing material: " + name
                );
            }

            var material = UnityEngine.Object.Instantiate(source);
            Bind(material);
            return material;
        }

        public void Bind(Material material)
        {
            material.SetBuffer("_Effects", Effects);
            material.SetBuffer("_Ribbons", Ribbons);
            material.SetBuffer("_Layers", Layers);
            material.SetBuffer("_Ramps", Ramps);
            material.SetInt("_RibbonSlots", Catalog.RibbonSlots);
            material.SetInt("_LayerSlots", Catalog.LayerSlots);
            material.SetFloat("_AtlasGrid", Catalog.AtlasGrid);
            material.SetTexture("_MainTex", Catalog.Atlas);
        }

        public void Bind(VisualEffect effect)
        {
            effect.SetGraphicsBuffer("Effects", Effects);
            effect.SetGraphicsBuffer("Ribbons", Ribbons);
            effect.SetGraphicsBuffer("Layers", Layers);
            effect.SetGraphicsBuffer("Ramps", Ramps);
            effect.SetTexture("Atlas", Catalog.Atlas);
            effect.SetVector2("AtlasGrid", new Vector2(Catalog.AtlasGrid, Catalog.AtlasGrid));
        }

        public static float2 AtlasUv(float2 uv, float tile, int grid)
        {
            return (new float2(math.fmod(tile, grid), math.floor(tile / grid)) + uv) / grid;
        }

        public static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(value);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }

        public void Dispose()
        {
            Effects?.Dispose();
            Ribbons?.Dispose();
            Layers?.Dispose();
            Ramps?.Dispose();
        }
    }
}
