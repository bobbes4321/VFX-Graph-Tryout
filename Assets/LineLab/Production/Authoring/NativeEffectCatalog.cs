using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace LineLab.Production
{
    /// <summary>
    /// Main-thread bake and ownership boundary. Jobs receive immutable value tables only.
    /// Complete every dependent job and release rendering buffers before disposing this owner.
    /// Structural edits take effect only when a new catalog and rendering pools are built.
    /// </summary>
    public sealed class NativeEffectCatalog : IDisposable
    {
        public const int AtlasTileSize = 256;
        public const int AtlasPadding = 2;
        public const int MaximumRibbons = 8;
        public const int MaximumLayers = 12;
        public const int MaximumTextures = 64;
        internal NativeArray<EffectRecipeData> Effects { get; private set; }
        internal NativeArray<RibbonRecipeData> Ribbons { get; private set; }
        internal NativeArray<LayerRecipeData> Layers { get; private set; }
        internal NativeArray<float4> Ramps { get; private set; }

        /// <summary>Borrowed immutable tables. Valid until this catalog is disposed.</summary>
        public NativeArray<EffectRecipeData>.ReadOnly EffectRecipes => Effects.AsReadOnly();
        public NativeArray<RibbonRecipeData>.ReadOnly RibbonRecipes => Ribbons.AsReadOnly();
        public NativeArray<LayerRecipeData>.ReadOnly LayerRecipes => Layers.AsReadOnly();
        public NativeArray<float4>.ReadOnly RampSamples => Ramps.AsReadOnly();
        public int RibbonSlots { get; private set; }
        public int LayerSlots { get; private set; }
        public int[] LayerCapacities { get; private set; }
        public int Segments { get; private set; }
        public int EffectCount { get; private set; }
        public int AllocatedCoreQuadsPerAbility { get; private set; }
        public int AllocatedFullQuadsPerAbility { get; private set; }
        public string[] EffectNames { get; private set; }
        public string[] EffectKeys { get; private set; }
        public string Fingerprint { get; private set; }
        public string LayoutKey { get; private set; }
        public Texture2D Atlas { get; private set; }
        public int AtlasGrid { get; private set; }
        public int TextureCount { get; private set; }
        public int RampRowCount =>
            Ramps.IsCreated ? Ramps.Length / EffectRecipeLayout.RampSamples : 0;

        readonly Dictionary<string, int> effectIds = new Dictionary<string, int>(
            StringComparer.Ordinal
        );
        readonly List<Texture2D> textures = new List<Texture2D>();
        readonly List<float4> ramps = new List<float4>();

        public NativeEffectCatalog(EffectLibrary library)
        {
            ValidateLibrary(library);
            Segments = library.Segments;
            EffectCount = library.Effects.Count;
            EffectNames = new string[EffectCount];
            EffectKeys = new string[EffectCount];
            foreach (EffectDefinition effect in library.Effects)
            {
                RibbonSlots = Math.Max(RibbonSlots, effect.Ribbons.Count);
                LayerSlots = Math.Max(LayerSlots, effect.Layers.Count);
            }

            // A single disabled slot keeps shader buffers valid for particle-only/core-only catalogs.
            RibbonSlots = Math.Max(1, RibbonSlots);
            LayerSlots = Math.Max(1, LayerSlots);
            LayerCapacities = new int[LayerSlots];
            var effects = new EffectRecipeData[EffectCount];
            var ribbons = new RibbonRecipeData[EffectCount * RibbonSlots];
            var layers = new LayerRecipeData[EffectCount * LayerSlots];
            // Row zero and texture zero are useful defaults for every padded slot.
            AddCurve(AnimationCurve.Linear(0, 1, 1, 1));
            textures.Add(null);
            for (int i = 0; i < EffectCount; i++)
            {
                EffectDefinition effect = library.Effects[i];
                EffectNames[i] = effect.name;
                EffectKeys[i] = effect.StableKey;
                effectIds.Add(effect.StableKey, i);
                float retirement = effect.CoreReleaseSeconds;
                for (int slot = 0; slot < effect.Ribbons.Count; slot++)
                {
                    RibbonLayerAsset ribbon = effect.Ribbons[slot];
                    ribbons[i * RibbonSlots + slot] = new RibbonRecipeData
                    {
                        Shape = new float4(
                            ribbon.WidthMultiplier,
                            ribbon.Opacity,
                            ribbon.Whiten,
                            ribbon.PhaseOffset
                        ),
                        Surface = new float4(
                            TextureIndex(ribbon.Profile),
                            AddGradient(ribbon.ColorAlongLine, Color.white),
                            AddCurve(ribbon.WidthAlongLine),
                            0
                        ),
                        Tint = Linear(ribbon.Tint),
                    };
                }

                for (int slot = 0; slot < effect.Layers.Count; slot++)
                {
                    ParticleLayerAsset layer = effect.Layers[slot];
                    LayerCapacities[slot] = Math.Max(LayerCapacities[slot], layer.Count);
                    if (layer.Count > 0)
                    {
                        retirement = Math.Max(retirement, layer.ReleaseSeconds);
                    }

                    layers[i * LayerSlots + slot] = new LayerRecipeData
                    {
                        Layout = new float4(
                            (int)layer.Kind,
                            layer.Count,
                            TextureIndex(layer.Profile),
                            AddGradient(layer.ColorOverLife, layer.Tint)
                        ),
                        Size = new float4(
                            layer.MinimumSize,
                            layer.MaximumSize,
                            layer.AspectRatio,
                            layer.FadeExponent
                        ),
                        Motion = new float4(
                            layer.CyclesPerSecond,
                            layer.OrbitRadius,
                            layer.OrbitSpread,
                            layer.Spin
                        ),
                        Physics = new float4(
                            layer.RadialSpeed,
                            layer.UpwardSpeed,
                            layer.UpwardVariation,
                            layer.Gravity
                        ),
                        Appearance = new float4(
                            layer.Opacity,
                            layer.Whiten,
                            layer.ReleaseSeconds,
                            AddCurve(layer.SizeOverLife)
                        ),
                        Placement = new float4(
                            (int)layer.Endpoint,
                            layer.Height,
                            layer.SourceScale,
                            layer.TargetScale
                        ),
                    };
                }

                LinePathAsset path = effect.Path;
                effects[i] = new EffectRecipeData
                {
                    Tint = Linear(effect.Tint),
                    Timing = new float4(
                        effect.ChargeSeconds,
                        effect.CoreReleaseSeconds,
                        retirement,
                        effect.Width
                    ),
                    Path = new float4(
                        (int)path.Kind,
                        path.UpAmplitude,
                        path.SideAmplitude,
                        path.SpatialFrequency
                    ),
                    Motion = new float4(
                        path.Speed,
                        path.NoiseCells,
                        path.NoiseTicks,
                        path.StartingPhase
                    ),
                    Pulse = new float4(
                        path.BaseIntensity,
                        path.PulseDepth,
                        path.PulseFrequency,
                        path.PulseSpeed
                    ),
                    Layout = new float4(effect.Ribbons.Count, effect.Layers.Count, 0, 0),
                };
            }

            AllocatedCoreQuadsPerAbility = RibbonSlots * Segments;
            AllocatedFullQuadsPerAbility = AllocatedCoreQuadsPerAbility;
            foreach (int capacity in LayerCapacities)
            {
                AllocatedFullQuadsPerAbility += capacity;
            }

            TextureCount = textures.Count;
            AtlasGrid = Mathf.CeilToInt(Mathf.Sqrt(TextureCount));
            LayoutKey = Segments + "r" + RibbonSlots + "l" + string.Join("_", LayerCapacities);
            try
            {
                Atlas = BuildAtlas();
                Fingerprint = ComputeFingerprint(
                    effects,
                    ribbons,
                    layers,
                    ramps.ToArray(),
                    Atlas.GetRawTextureData<byte>().ToArray()
                );
                Effects = new NativeArray<EffectRecipeData>(effects, Allocator.Persistent);
                Ribbons = new NativeArray<RibbonRecipeData>(ribbons, Allocator.Persistent);
                Layers = new NativeArray<LayerRecipeData>(layers, Allocator.Persistent);
                Ramps = new NativeArray<float4>(ramps.ToArray(), Allocator.Persistent);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public EffectId GetEffectId(string stableKey)
        {
            if (stableKey == null || !effectIds.TryGetValue(stableKey, out int index))
            {
                throw new ArgumentException(
                    "Unknown effect key '"
                        + stableKey
                        + "'. Add the recipe to the selected library before baking."
                );
            }

            return new EffectId(index);
        }

        int TextureIndex(Texture2D texture)
        {
            int index = textures.IndexOf(texture);
            if (index >= 0)
            {
                return index;
            }

            if (textures.Count >= MaximumTextures)
            {
                throw new ArgumentException(
                    "Catalog exceeds 64 texture tiles, including the default white tile. Reuse textures or split the library."
                );
            }

            textures.Add(texture);
            return textures.Count - 1;
        }

        int AddGradient(Gradient gradient, Color tint)
        {
            int row = ramps.Count / EffectRecipeLayout.RampSamples;
            float4 multiplier = Linear(tint);
            for (int i = 0; i < EffectRecipeLayout.RampSamples; i++)
            {
                ramps.Add(
                    Linear(gradient.Evaluate(i / (float)(EffectRecipeLayout.RampSamples - 1)))
                        * multiplier
                );
            }

            return row;
        }

        int AddCurve(AnimationCurve curve)
        {
            int row = ramps.Count / EffectRecipeLayout.RampSamples;
            for (int i = 0; i < EffectRecipeLayout.RampSamples; i++)
            {
                ramps.Add(
                    new float4(curve.Evaluate(i / (float)(EffectRecipeLayout.RampSamples - 1)))
                );
            }

            return row;
        }

        static float4 Linear(Color color)
        {
            Color linear = color.linear;
            return new float4(linear.r, linear.g, linear.b, color.a);
        }

        Texture2D BuildAtlas()
        {
            int width = AtlasGrid * AtlasTileSize;
            var pixels = new Color[width * width];
            int contentSize = AtlasTileSize - 2 * AtlasPadding;
            for (int tile = 0; tile < textures.Count; tile++)
            {
                Color[] content = ReadTexture(textures[tile], contentSize);
                int originX = tile % AtlasGrid * AtlasTileSize;
                int originY = tile / AtlasGrid * AtlasTileSize;
                for (int y = 0; y < AtlasTileSize; y++)
                {
                    for (int x = 0; x < AtlasTileSize; x++)
                    {
                        int sx = Mathf.Clamp(x - AtlasPadding, 0, contentSize - 1);
                        int sy = Mathf.Clamp(y - AtlasPadding, 0, contentSize - 1);
                        pixels[(originY + y) * width + originX + x] = content[
                            sy * contentSize + sx
                        ];
                    }
                }
            }

            var atlas = new Texture2D(width, width, TextureFormat.RGBAHalf, false, true)
            {
                name = "Baked Effect Profiles",
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            atlas.SetPixels(pixels);
            atlas.Apply(false, false);
            return atlas;
        }

        static Color[] ReadTexture(Texture2D source, int size)
        {
            if (source == null)
            {
                var white = new Color[size * size];
                for (int i = 0; i < white.Length; i++)
                {
                    white[i] = Color.white;
                }

                return white;
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(
                size,
                size,
                0,
                RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear
            );
            Texture2D readable = null;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                readable = new Texture2D(size, size, TextureFormat.RGBAHalf, false, true);
                readable.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                readable.Apply();
                Color[] pixels = readable.GetPixels();
                foreach (Color pixel in pixels)
                {
                    if (!math.all(math.isfinite(new float4(pixel.r, pixel.g, pixel.b, pixel.a))))
                    {
                        throw new ArgumentException(
                            source.name
                                + ": profile contains nonfinite values or exceeds the RGBAHalf atlas range. Use finite profile values within +/-65504."
                        );
                    }
                }

                return pixels;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                DestroyOwned(readable);
            }
        }

        string ComputeFingerprint(
            EffectRecipeData[] effects,
            RibbonRecipeData[] ribbons,
            LayerRecipeData[] layers,
            float4[] samples,
            byte[] atlasBytes
        )
        {
            using (var bytes = new MemoryStream())
            using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
            {
                writer.Write("LineLab.EffectCatalog.v1");
                writer.Write(Segments);
                writer.Write(RibbonSlots);
                writer.Write(LayerSlots);
                writer.Write(AtlasGrid);
                writer.Write((int)Atlas.format);
                writer.Write(TextureCount);
                foreach (string key in EffectKeys)
                {
                    writer.Write(key);
                }

                foreach (EffectRecipeData item in effects)
                {
                    Write(writer, item.Tint);
                    Write(writer, item.Timing);
                    Write(writer, item.Path);
                    Write(writer, item.Motion);
                    Write(writer, item.Pulse);
                    Write(writer, item.Layout);
                }

                foreach (RibbonRecipeData item in ribbons)
                {
                    Write(writer, item.Shape);
                    Write(writer, item.Surface);
                    Write(writer, item.Tint);
                }

                foreach (LayerRecipeData item in layers)
                {
                    Write(writer, item.Layout);
                    Write(writer, item.Size);
                    Write(writer, item.Motion);
                    Write(writer, item.Physics);
                    Write(writer, item.Appearance);
                    Write(writer, item.Placement);
                }

                foreach (float4 sample in samples)
                {
                    Write(writer, sample);
                }

                // Hash the exact half-float GPU texture representation. Color32 conversion
                // would clip HDR and discard precision, hiding real content changes.
                writer.Write(atlasBytes.Length);
                writer.Write(atlasBytes);
                writer.Flush();
                using (SHA256 sha = SHA256.Create())
                    return BitConverter
                        .ToString(sha.ComputeHash(bytes.ToArray()))
                        .Replace("-", "")
                        .ToLowerInvariant();
            }
        }

        static void Write(BinaryWriter writer, float4 value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
            writer.Write(value.w);
        }

        public static void ValidateLibrary(EffectLibrary library)
        {
            if (library == null)
            {
                throw new ArgumentException("Select an EffectLibrary asset.");
            }

            if (library.Segments < 1 || library.Segments > 128)
            {
                Fail(library, "Segments must be between 1 and 128.");
            }

            if (library.Effects == null || library.Effects.Count == 0)
            {
                Fail(library, "Add at least one EffectDefinition to Effects.");
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (EffectDefinition effect in library.Effects)
            {
                if (effect == null)
                {
                    Fail(library, "Effects contains a missing recipe reference.");
                }

                if (
                    string.IsNullOrWhiteSpace(effect.StableKey)
                    || effect.StableKey != effect.StableKey.Trim()
                )
                {
                    Fail(effect, "StableKey must be nonempty and have no surrounding whitespace.");
                }

                if (!keys.Add(effect.StableKey))
                {
                    Fail(
                        effect,
                        "Duplicate StableKey '"
                            + effect.StableKey
                            + "'. Give every recipe a unique gameplay key."
                    );
                }

                if (effect.Path == null)
                {
                    Fail(effect, "Assign a Path module.");
                }

                if (effect.Ribbons == null || effect.Ribbons.Count > MaximumRibbons)
                {
                    Fail(effect, "Ribbons supports at most 8 slots.");
                }

                if (effect.Layers == null || effect.Layers.Count > MaximumLayers)
                {
                    Fail(effect, "Layers supports at most 12 slots.");
                }

                Positive(
                    effect,
                    "Width/ChargeSeconds/CoreReleaseSeconds",
                    effect.Width,
                    effect.ChargeSeconds,
                    effect.CoreReleaseSeconds
                );
                CheckColor(effect, effect.Tint);
                LinePathAsset path = effect.Path;
                if (!Enum.IsDefined(typeof(LinePathKind), path.Kind))
                {
                    Fail(path, "Choose a supported path Kind.");
                }

                Nonnegative(
                    path,
                    "Amplitude/frequency/pulse",
                    path.UpAmplitude,
                    path.SideAmplitude,
                    path.SpatialFrequency,
                    path.NoiseTicks,
                    path.BaseIntensity,
                    path.PulseDepth,
                    path.PulseFrequency
                );
                Positive(path, "NoiseCells", path.NoiseCells);
                Finite(path, "Speed/phase", path.Speed, path.StartingPhase, path.PulseSpeed);
                foreach (RibbonLayerAsset ribbon in effect.Ribbons)
                {
                    if (ribbon == null)
                    {
                        Fail(
                            effect,
                            "Ribbons contains a missing module. Remove the slot or assign an asset."
                        );
                    }

                    Nonnegative(ribbon, "WidthMultiplier", ribbon.WidthMultiplier);
                    Unit(ribbon, "Opacity/Whiten", ribbon.Opacity, ribbon.Whiten);
                    Finite(ribbon, "PhaseOffset", ribbon.PhaseOffset);
                    CheckColor(ribbon, ribbon.Tint);
                    CheckGradient(ribbon, ribbon.ColorAlongLine);
                    CheckCurve(ribbon, ribbon.WidthAlongLine);
                }

                foreach (ParticleLayerAsset layer in effect.Layers)
                {
                    if (layer == null)
                    {
                        Fail(
                            effect,
                            "Layers contains a missing module. Remove the slot or assign an asset."
                        );
                    }

                    if (
                        !Enum.IsDefined(typeof(ParticleMotionKind), layer.Kind)
                        || !Enum.IsDefined(typeof(EffectEndpoint), layer.Endpoint)
                    )
                    {
                        Fail(layer, "Choose a supported Kind and Endpoint.");
                    }

                    if (layer.Count < 0 || layer.Count > 4096)
                    {
                        Fail(layer, "Count must be between 0 and 4096.");
                    }

                    Nonnegative(
                        layer,
                        "Size/orbit/physics/scales",
                        layer.MinimumSize,
                        layer.MaximumSize,
                        layer.FadeExponent,
                        layer.OrbitRadius,
                        layer.OrbitSpread,
                        layer.RadialSpeed,
                        layer.UpwardVariation,
                        layer.SourceScale,
                        layer.TargetScale
                    );
                    if (layer.MaximumSize < layer.MinimumSize)
                    {
                        Fail(layer, "MaximumSize must be at least MinimumSize.");
                    }

                    Positive(
                        layer,
                        "AspectRatio/ReleaseSeconds",
                        layer.AspectRatio,
                        layer.ReleaseSeconds
                    );
                    Unit(layer, "Opacity/Whiten", layer.Opacity, layer.Whiten);
                    Finite(
                        layer,
                        "Motion/physics/placement",
                        layer.CyclesPerSecond,
                        layer.Spin,
                        layer.UpwardSpeed,
                        layer.Gravity,
                        layer.Height
                    );
                    CheckColor(layer, layer.Tint);
                    CheckGradient(layer, layer.ColorOverLife);
                    CheckCurve(layer, layer.SizeOverLife);
                }
            }
        }

        static void CheckGradient(UnityEngine.Object asset, Gradient gradient)
        {
            if (gradient == null)
            {
                Fail(asset, "Assign a color Gradient.");
            }

            foreach (GradientColorKey key in gradient.colorKeys)
            {
                CheckColor(asset, key.color);
                Unit(asset, "Gradient key time", key.time);
            }

            foreach (GradientAlphaKey key in gradient.alphaKeys)
            {
                Unit(asset, "Gradient alpha/time", key.alpha, key.time);
            }

            for (int i = 0; i < EffectRecipeLayout.RampSamples; i++)
            {
                CheckColor(
                    asset,
                    gradient.Evaluate(i / (float)(EffectRecipeLayout.RampSamples - 1))
                );
            }
        }

        static void CheckCurve(UnityEngine.Object asset, AnimationCurve curve)
        {
            if (curve == null || curve.length == 0)
            {
                Fail(asset, "Assign a nonempty size/width curve.");
            }

            foreach (Keyframe key in curve.keys)
            {
                Finite(asset, "Curve key time/value", key.time, key.value);
            }

            for (int i = 0; i < EffectRecipeLayout.RampSamples; i++)
            {
                Nonnegative(
                    asset,
                    "Sampled curve value",
                    curve.Evaluate(i / (float)(EffectRecipeLayout.RampSamples - 1))
                );
            }
        }

        static void CheckColor(UnityEngine.Object asset, Color color)
        {
            Nonnegative(asset, "HDR color", color.r, color.g, color.b);
            Unit(asset, "Color alpha", color.a);
            Color linear = color.linear;
            Finite(asset, "HDR color after linear conversion", linear.r, linear.g, linear.b);
        }

        static void Finite(UnityEngine.Object asset, string field, params float[] values)
        {
            foreach (float value in values)
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    Fail(asset, field + " contains a nonfinite value.");
                }
            }
        }

        static void Nonnegative(UnityEngine.Object asset, string field, params float[] values)
        {
            Finite(asset, field, values);
            foreach (float value in values)
            {
                if (value < 0)
                {
                    Fail(asset, field + " must be nonnegative.");
                }
            }
        }

        static void Positive(UnityEngine.Object asset, string field, params float[] values)
        {
            Finite(asset, field, values);
            foreach (float value in values)
            {
                if (value <= 0)
                {
                    Fail(asset, field + " must be greater than zero.");
                }
            }
        }

        static void Unit(UnityEngine.Object asset, string field, params float[] values)
        {
            Finite(asset, field, values);
            foreach (float value in values)
            {
                if (value < 0 || value > 1)
                {
                    Fail(asset, field + " must be between zero and one.");
                }
            }
        }

        static void Fail(UnityEngine.Object asset, string message)
        {
            throw new ArgumentException(
                (asset == null ? "Effect catalog" : asset.name) + ": " + message
            );
        }

        public void Dispose()
        {
            if (Effects.IsCreated)
            {
                Effects.Dispose();
            }

            if (Ribbons.IsCreated)
            {
                Ribbons.Dispose();
            }

            if (Layers.IsCreated)
            {
                Layers.Dispose();
            }

            if (Ramps.IsCreated)
            {
                Ramps.Dispose();
            }

            Effects = default;
            Ribbons = default;
            Layers = default;
            Ramps = default;
            DestroyOwned(Atlas);
            Atlas = null;
        }

        static void DestroyOwned(UnityEngine.Object value)
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
    }
}
