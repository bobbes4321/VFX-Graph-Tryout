using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LineLab.Production.Editor
{
    /// <summary>Optional sample initialization. Existing source assets are never overwritten.</summary>
    public static class EffectAuthoringPresets
    {
        const string Recipes = "Assets/LineLab/Recipes";
        const string ResourcesRoot = "Assets/LineLab/Resources/Production";

        [MenuItem("Line Lab/Authoring/Create Missing Sample Recipes")]
        public static void EnsureSampleAssets()
        {
            EnsureFolder(Recipes);
            EnsureFolder(Recipes + "/Paths");
            EnsureFolder(Recipes + "/Ribbons");
            EnsureFolder(Recipes + "/Particles");
            EnsureFolder(ResourcesRoot);
            var core = Create<RibbonLayerAsset>(
                Recipes + "/Ribbons/HotCore.asset",
                value =>
                {
                    value.Profile = Profile("CoreProfile");
                    value.WidthMultiplier = 1;
                    value.Whiten = .45f;
                }
            );
            var halo = Create<RibbonLayerAsset>(
                Recipes + "/Ribbons/SoftHalo.asset",
                value =>
                {
                    value.Profile = Profile("CoreProfile");
                    value.WidthMultiplier = 3.6f;
                    value.Opacity = .075f;
                    value.Whiten = 0;
                    value.PhaseOffset = 2.094395f;
                }
            );
            var filament = Create<RibbonLayerAsset>(
                Recipes + "/Ribbons/OrbitFilament.asset",
                value =>
                {
                    value.Profile = Profile("CoreProfile");
                    value.WidthMultiplier = .55f;
                    value.Opacity = .65f;
                    value.Whiten = 0;
                    value.PhaseOffset = 4.18879f;
                }
            );
            var corona = Create<ParticleLayerAsset>(
                Recipes + "/Particles/EndpointCorona.asset",
                value =>
                {
                    value.Kind = ParticleMotionKind.Corona;
                    value.Count = 8;
                    value.Profile = Profile("Profile2");
                    value.MinimumSize = .85f;
                    value.MaximumSize = 4.1f;
                    value.SizeOverLife = Curve(1, 1.6f, 2.8f, 4.1f);
                    value.Opacity = .65f;
                    value.Whiten = 0;
                    value.Spin = 1;
                    value.Height = 0;
                }
            );
            var rune = Create<ParticleLayerAsset>(
                Recipes + "/Particles/GroundRunes.asset",
                value =>
                {
                    value.Kind = ParticleMotionKind.Ground;
                    value.Count = 4;
                    value.Profile = Profile("Profile3");
                    value.MinimumSize = 2.8f;
                    value.MaximumSize = 3.7f;
                    value.Opacity = .3f;
                    value.Whiten = 0;
                    value.Spin = .3f;
                    value.Height = .19f;
                    value.SourceScale = 1;
                    value.TargetScale = 1.22f;
                }
            );
            var definitions = new List<EffectDefinition>();
            string[] names = { "Renewal", "StormLance", "SolarTether" };
            string[] keys = { "renewal", "storm-lance", "solar-tether" };
            Color[] linearTints =
            {
                new Color(.16f, 1, .62f),
                new Color(.22f, .54f, 1),
                new Color(1, .39f, .075f),
            };
            for (int i = 0; i < names.Length; i++)
            {
                int style = i;
                string effectName = names[i];
                var path = Create<LinePathAsset>(
                    Recipes + "/Paths/" + effectName + "Path.asset",
                    value =>
                    {
                        value.Kind = (LinePathKind)style;
                        if (style == 1)
                        {
                            value.UpAmplitude = 1.25f;
                            value.SideAmplitude = .75f;
                        }
                        else if (style == 2)
                        {
                            value.UpAmplitude = .14f;
                            value.SideAmplitude = .12f;
                            value.SpatialFrequency = 26;
                            value.Speed = 11;
                        }
                    }
                );
                var motes = Create<ParticleLayerAsset>(
                    Recipes + "/Particles/" + effectName + "Motes.asset",
                    value =>
                    {
                        value.Profile = Profile("Profile0");
                        value.CyclesPerSecond = style == 0 ? .38f : .68f;
                        value.AspectRatio = style == 2 ? 2.8f : 1;
                        value.SizeOverLife = new AnimationCurve(
                            new Keyframe(0, .3f),
                            new Keyframe(.5f, 1),
                            new Keyframe(1, .3f)
                        );
                    }
                );
                var sparks = Create<ParticleLayerAsset>(
                    Recipes + "/Particles/" + effectName + "Sparks.asset",
                    value =>
                    {
                        value.Kind = ParticleMotionKind.Ballistic;
                        value.Count = 24;
                        value.Profile = Profile("Profile1");
                        value.MinimumSize = .035f;
                        value.MaximumSize = .08f;
                        value.AspectRatio = .25f;
                        value.CyclesPerSecond = style == 0 ? .65f : 1.7f;
                        value.RadialSpeed = style == 0 ? 1.35f : 2.7f;
                        value.UpwardSpeed = .6f * value.RadialSpeed;
                        value.UpwardVariation = 1.6f * value.RadialSpeed;
                        value.Gravity = style == 0 ? -.3f : 1.3f;
                        value.Opacity = 1;
                        value.Whiten = 0;
                        value.ReleaseSeconds = 1.1f;
                        value.FadeExponent = 2;
                        value.Height = 0;
                        value.SizeOverLife = AnimationCurve.Linear(0, 1, 1, .3f);
                    }
                );
                definitions.Add(
                    Create<EffectDefinition>(
                        Recipes + "/" + effectName + ".asset",
                        value =>
                        {
                            value.StableKey = keys[style];
                            value.Tint = linearTints[style].gamma;
                            value.Path = path;
                            value.Ribbons = new List<RibbonLayerAsset> { core, halo, filament };
                            value.Layers = new List<ParticleLayerAsset>
                            {
                                motes,
                                sparks,
                                corona,
                                rune,
                            };
                        }
                    )
                );
            }

            var voidPath = Create<LinePathAsset>(
                Recipes + "/Paths/VoidPath.asset",
                value =>
                {
                    value.Kind = LinePathKind.Helix;
                    value.UpAmplitude = .9f;
                    value.SideAmplitude = .65f;
                    value.SpatialFrequency = 12;
                    value.Speed = -2;
                }
            );
            var voidRibbon = Create<RibbonLayerAsset>(
                Recipes + "/Ribbons/VoidGradient.asset",
                value =>
                {
                    value.Profile = Profile("CoreProfile");
                    value.Whiten = .1f;
                    value.WidthAlongLine = new AnimationCurve(
                        new Keyframe(0, .25f),
                        new Keyframe(.4f, 1.8f),
                        new Keyframe(1, .5f)
                    );
                    value.ColorAlongLine = Gradient(new Color(.3f, 1, 1), new Color(1, .15f, .7f));
                }
            );
            var dust = Create<ParticleLayerAsset>(
                Recipes + "/Particles/VoidDrift.asset",
                value =>
                {
                    value.Profile = Profile("Profile1"); // An actual texture swap from the usual mote profile.
                    value.Count = 20;
                    value.CyclesPerSecond = -.22f;
                    value.MinimumSize = .05f;
                    value.MaximumSize = .17f;
                    value.AspectRatio = 4;
                    value.OrbitRadius = .5f;
                    value.OrbitSpread = .8f;
                    value.ReleaseSeconds = 2.4f;
                    value.ColorOverLife = Gradient(new Color(.3f, 1, 1), new Color(1, .15f, .7f));
                    value.SizeOverLife = new AnimationCurve(
                        new Keyframe(0, .1f),
                        new Keyframe(.3f, 1.8f),
                        new Keyframe(1, .1f)
                    );
                }
            );
            var voidEffect = Create<EffectDefinition>(
                Recipes + "/VoidConduit.asset",
                value =>
                {
                    value.StableKey = "void-conduit";
                    value.Tint = new Color(.6f, .2f, 1).gamma;
                    value.Width = .18f;
                    value.Path = voidPath;
                    value.Ribbons = new List<RibbonLayerAsset> { voidRibbon, halo };
                    // RenewalMotes is intentionally reused. Its edits propagate to both effects.
                    value.Layers = new List<ParticleLayerAsset>
                    {
                        definitions[0].Layers[0],
                        dust,
                        corona,
                        rune,
                        definitions[1].Layers[1],
                    };
                }
            );
            Create<EffectLibrary>(
                ResourcesRoot + "/EffectLibrary.asset",
                value => value.Effects = new List<EffectDefinition>(definitions)
            );
            Create<EffectLibrary>(
                ResourcesRoot + "/ArtistLibrary.asset",
                value =>
                {
                    value.Effects = new List<EffectDefinition>(definitions) { voidEffect };
                }
            );
            AssetDatabase.SaveAssets();
            Debug.Log(
                "Missing authoring samples created. Existing recipes, textures, graphs and scenes were preserved."
            );
        }

        static T Create<T>(string path, Action<T> initialize)
            where T : ScriptableObject
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                return existing;
            }

            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                throw new InvalidOperationException(
                    "Cannot initialize " + path + ": a different asset already exists there."
                );
            }

            T asset = ScriptableObject.CreateInstance<T>();
            asset.name = System.IO.Path.GetFileNameWithoutExtension(path);
            initialize(asset);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static Texture2D Profile(string name)
        {
            return Resources.Load<Texture2D>("Production/" + name);
        }

        static Gradient Gradient(Color source, Color target)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(source, 0), new GradientColorKey(target, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) }
            );
            return gradient;
        }

        static AnimationCurve Curve(params float[] values)
        {
            var keys = new Keyframe[values.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = new Keyframe(
                    i / (float)(keys.Length - 1),
                    values[i] / values[values.Length - 1]
                );
            }

            return new AnimationCurve(keys);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
