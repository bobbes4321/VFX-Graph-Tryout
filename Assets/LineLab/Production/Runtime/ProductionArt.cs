using UnityEngine;

namespace LineLab.Production
{
    // All art is generated in-project. No downloaded assets or license dependencies.
    public static class ProductionArt
    {
        static Material Mat(string name)
        {
            return Resources.Load<Material>("Production/" + name);
        }

        static Transform Shape(
            Transform parent,
            string name,
            PrimitiveType type,
            Vector3 p,
            Vector3 scale,
            string mat,
            Vector3 euler = default
        )
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = p;
            go.transform.localScale = scale;
            go.transform.localEulerAngles = euler;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (type == PrimitiveType.Cube)
            {
                go.GetComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>(
                    "Production/BevelBox"
                );
            }

            go.GetComponent<Renderer>().sharedMaterial = Mat(mat);
            return go.transform;
        }

        static Transform Rock(Transform parent, Vector3 p, Vector3 scale, int seed)
        {
            var go = new GameObject("Wind-cut obsidian");
            go.transform.SetParent(parent);
            go.transform.localPosition = p;
            go.transform.localScale = scale;
            go.transform.localEulerAngles = new Vector3(seed % 17, seed * 137.5f, seed % 23);
            go.AddComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>("Production/Rock");
            go.AddComponent<MeshRenderer>().sharedMaterial = Mat(
                seed % 3 == 0 ? "StoneLight" : "Stone"
            );
            return go.transform;
        }

        public static void Build()
        {
            var root = new GameObject("AETHER / Outpost environment").transform;
            var terrain = new GameObject(
                "Sculpted basalt terrain",
                typeof(MeshFilter),
                typeof(MeshRenderer)
            );
            terrain.transform.SetParent(root);
            terrain.GetComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>(
                "Production/Terrain"
            );
            terrain.GetComponent<MeshRenderer>().sharedMaterial = Mat("TerrainMaterial");
            for (int lane = 0; lane < 3; lane++)
            {
                float x = (lane - 1) * 12;
                var platform = Shape(
                    root,
                    "Expedition landing deck",
                    PrimitiveType.Cylinder,
                    new Vector3(x, -.07f, .5f),
                    new Vector3(11.6f, .18f, 10.8f),
                    "Platform"
                );
                Shape(
                    root,
                    "Deck inner plate",
                    PrimitiveType.Cylinder,
                    new Vector3(x, .12f, .5f),
                    new Vector3(10.9f, .035f, 10.1f),
                    "Deck"
                );
                for (int j = 0; j < 8; j++)
                {
                    float a = j * Mathf.PI / 4;
                    Shape(
                        root,
                        "Recessed deck marker",
                        PrimitiveType.Cube,
                        new Vector3(x + Mathf.Cos(a) * 5.15f, .17f, .5f + Mathf.Sin(a) * 4.85f),
                        new Vector3(.09f, .025f, .58f),
                        lane == 0 ? "MintGlow"
                            : lane == 1 ? "BlueGlow"
                            : "GoldGlow",
                        new Vector3(0, -j * 45, 0)
                    );
                }

                for (int j = 0; j < 3; j++)
                {
                    Shape(
                        root,
                        "Expansion joint",
                        PrimitiveType.Cube,
                        new Vector3(x + (j - 1) * 2.4f, .16f, .5f),
                        new Vector3(.035f, .012f, 8.8f),
                        "Dark"
                    );
                    Shape(
                        root,
                        "Recessed service conduit",
                        PrimitiveType.Cube,
                        new Vector3(x, -.01f, -5),
                        new Vector3(8.4f, .18f, .28f),
                        "Dark"
                    );
                }

                for (int j = 0; j < 2; j++)
                {
                    Vector3 p = new Vector3(
                        x + (j == 0 ? -4.4f : 4.4f),
                        .3f,
                        j == 0 ? -3.5f : 4.2f
                    );
                    Shape(
                        root,
                        "Deck clamp",
                        PrimitiveType.Cube,
                        p,
                        new Vector3(.8f, .5f, .55f),
                        "ArmorDark",
                        new Vector3(0, 25, 0)
                    );
                    Shape(
                        root,
                        "Clamp brass",
                        PrimitiveType.Cube,
                        p + Vector3.up * .27f,
                        new Vector3(.62f, .06f, .4f),
                        "Brass",
                        new Vector3(0, 25, 0)
                    );
                }

                Unit(root, "Caster " + lane, new Vector3(x - 3.5f, .18f, -1.6f), lane, false);
                Unit(root, "Receiver " + lane, new Vector3(x + 3.5f, .18f, 2.1f), lane, true);
            }

            var rng = new System.Random(9042);
            for (int i = 0; i < 74; i++)
            {
                float x = (float)rng.NextDouble() * 65 - 32.5f;
                float z = (float)rng.NextDouble() * 49 - 22;
                if (Mathf.Abs(x) < 19 && z > -6 && z < 7)
                {
                    continue;
                }

                float h = .6f + (float)rng.NextDouble() * 3.3f;
                Rock(root, new Vector3(x, -.5f, z), new Vector3(h * 1.4f, h, h), i);
                if (i % 5 == 0)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        Shape(
                            root,
                            "Luminous mineral",
                            PrimitiveType.Cube,
                            new Vector3(x + c * .3f, .2f + c * .16f, z),
                            new Vector3(.18f, .7f + c * .23f, .16f),
                            "Mineral",
                            new Vector3(0, 37 * c, 20 - c * 14)
                        );
                    }
                }
            }

            for (int i = 0; i < 7; i++)
            {
                float x = (i - 3) * 8.5f;
                Rock(
                    root,
                    new Vector3(x, -1, 23 + (i % 2) * 4),
                    new Vector3(5, 8 + i % 3 * 3, 4),
                    i + 201
                );
            }

            // Silhouette architecture makes the arena read as a place, not a test grid.
            for (int i = 0; i < 4; i++)
            {
                float x = (i - 1.5f) * 14;
                Shape(
                    root,
                    "Ancient alloy pylon",
                    PrimitiveType.Cube,
                    new Vector3(x, 2.4f, 11.5f),
                    new Vector3(1.3f, 5.3f, 1.3f),
                    "Platform",
                    new Vector3(0, 18, 4)
                );
                Shape(
                    root,
                    "Pylon spine",
                    PrimitiveType.Cube,
                    new Vector3(x - .18f, 2.5f, 10.85f),
                    new Vector3(.11f, 3.8f, .08f),
                    "GoldGlow",
                    new Vector3(0, 18, 4)
                );
                Shape(
                    root,
                    "Pylon crown",
                    PrimitiveType.Cube,
                    new Vector3(x + .08f, 5.2f, 11.5f),
                    new Vector3(1.65f, .4f, 1.7f),
                    "Brass",
                    new Vector3(0, 18, 4)
                );
            }

            for (int i = 0; i < 12; i++)
            {
                float x = -24 + i * 4.4f;
                Shape(
                    root,
                    "Perimeter inset lamp",
                    PrimitiveType.Cylinder,
                    new Vector3(x, .2f, -9),
                    new Vector3(.32f, .25f, .32f),
                    "ArmorDark"
                );
                Shape(
                    root,
                    "Amber guidance lamp",
                    PrimitiveType.Sphere,
                    new Vector3(x, .47f, -9),
                    new Vector3(.18f, .08f, .18f),
                    "GoldGlow"
                );
            }
        }

        static void Unit(Transform parent, string name, Vector3 p, int style, bool heavy)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent);
            root.localPosition = p;
            root.localEulerAngles = new Vector3(0, heavy ? -118 : 62, 0);
            string glow =
                style == 0 ? "MintGlow"
                : style == 1 ? "BlueGlow"
                : "GoldGlow";
            float s = heavy ? 1.14f : 1;
            root.localScale = Vector3.one * s;
            Shape(
                root,
                "Reactor backpack",
                PrimitiveType.Cube,
                new Vector3(0, 1.45f, -.3f),
                new Vector3(.72f, .8f, .38f),
                "ArmorDark"
            );
            for (int side = -1; side <= 1; side += 2)
            {
                Shape(
                    root,
                    "Splayed boot",
                    PrimitiveType.Cube,
                    new Vector3(side * .25f, .15f, .10f),
                    new Vector3(.34f, .27f, .64f),
                    "ArmorDark",
                    new Vector3(0, -side * 6, 0)
                );
                Shape(
                    root,
                    "Greave",
                    PrimitiveType.Capsule,
                    new Vector3(side * .25f, .50f, .01f),
                    new Vector3(.29f, .34f, .33f),
                    "Armor"
                );
                Shape(
                    root,
                    "Knee plate",
                    PrimitiveType.Sphere,
                    new Vector3(side * .25f, .78f, .17f),
                    new Vector3(.34f, .29f, .22f),
                    "Brass"
                );
                Shape(
                    root,
                    "Thigh actuator",
                    PrimitiveType.Capsule,
                    new Vector3(side * .23f, 1.0f, -.05f),
                    new Vector3(.27f, .29f, .30f),
                    "ArmorDark"
                );
                Shape(
                    root,
                    "Shoulder pauldron",
                    PrimitiveType.Sphere,
                    new Vector3(side * .52f, 1.78f, 0),
                    new Vector3(.52f, .40f, .52f),
                    "Armor"
                );
                Shape(
                    root,
                    "Arm servo",
                    PrimitiveType.Capsule,
                    new Vector3(side * .56f, 1.46f, .15f),
                    new Vector3(.21f, .28f, .24f),
                    "ArmorDark",
                    new Vector3(-30, 0, side * 12)
                );
                Shape(
                    root,
                    "Forearm emitter",
                    PrimitiveType.Cube,
                    new Vector3(side * .55f, 1.40f, .40f),
                    new Vector3(.3f, .29f, .5f),
                    "Armor",
                    new Vector3(-25, 0, 0)
                );
                Shape(
                    root,
                    "Emitter lens",
                    PrimitiveType.Sphere,
                    new Vector3(side * .55f, 1.5f, .65f),
                    new Vector3(.16f, .16f, .09f),
                    glow
                );
                Shape(
                    root,
                    "Rear cooling fin",
                    PrimitiveType.Cube,
                    new Vector3(side * .28f, 1.65f, -.56f),
                    new Vector3(.12f, .65f, .18f),
                    "Brass",
                    new Vector3(-12, 0, 0)
                );
            }

            Shape(
                root,
                "Armored torso",
                PrimitiveType.Capsule,
                new Vector3(0, 1.51f, 0),
                new Vector3(.80f, .45f, .54f),
                "Armor"
            );
            Shape(
                root,
                "Chest plate",
                PrimitiveType.Cube,
                new Vector3(0, 1.63f, .28f),
                new Vector3(.54f, .34f, .12f),
                "ArmorDark",
                new Vector3(-15, 0, 0)
            );
            Shape(
                root,
                "Chest reactor",
                PrimitiveType.Sphere,
                new Vector3(0, 1.65f, .39f),
                new Vector3(.20f, .20f, .09f),
                glow
            );
            Shape(
                root,
                "Neck",
                PrimitiveType.Cylinder,
                new Vector3(0, 1.95f, 0),
                new Vector3(.19f, .14f, .19f),
                "ArmorDark"
            );
            Shape(
                root,
                "Helmet",
                PrimitiveType.Cube,
                new Vector3(0, 2.14f, 0),
                new Vector3(.48f, .49f, .49f),
                "Armor"
            );
            Shape(
                root,
                "Visor recess",
                PrimitiveType.Cube,
                new Vector3(0, 2.17f, .22f),
                new Vector3(.40f, .14f, .10f),
                "Dark"
            );
            Shape(
                root,
                "Visor",
                PrimitiveType.Cube,
                new Vector3(0, 2.18f, .28f),
                new Vector3(.32f, .035f, .03f),
                glow
            );
            Shape(
                root,
                "Antenna",
                PrimitiveType.Cylinder,
                new Vector3(.2f, 2.48f, -.05f),
                new Vector3(.025f, .19f, .025f),
                "Brass"
            );
            Shape(
                root,
                "Antenna signal",
                PrimitiveType.Sphere,
                new Vector3(.2f, 2.68f, -.05f),
                Vector3.one * .08f,
                glow
            );
        }
    }
}
