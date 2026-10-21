// Execute through Unity CLI eval with scene 03 playing. Results arrive asynchronously.
var lab = UnityEngine.Object.FindFirstObjectByType<LineLab.Production.ProductionController>();
lab.Benchmarking = true;
lab.Freeze = false;
lab.Configure(LineLab.Technique.ParticleBillboardPerLine, 3, true, 1, true);
lab.SetFocus(-1, true);
var frameProperty = typeof(LineLab.Production.ProductionController).GetProperty("Frame");
int stage = 0;
int changedFrame = 0;
UnityEngine.ParticleSystem particles = null;
UnityEditor.EditorApplication.CallbackFunction tick = null;
tick = () =>
{
    try
    {
        if (stage == 0 && lab.Frame >= 30)
        {
            particles = lab.Backend.Root.GetComponentsInChildren<UnityEngine.ParticleSystem>(true)[0];
            System.IO.File.WriteAllText("Logs/particle-reactivation.txt", "initial particles=" + particles.particleCount + "\n");
            frameProperty.SetValue(lab, 430);
            changedFrame = UnityEngine.Time.frameCount;
            stage = 1;
        }
        else if (stage == 1 && UnityEngine.Time.frameCount - changedFrame >= 8)
        {
            bool disabled = !particles.gameObject.activeSelf;
            System.IO.File.AppendAllText("Logs/particle-reactivation.txt", "retired disabled=" + disabled + "\n");
            if (!disabled)
            {
                throw new System.Exception("Retired owner was not disabled.");
            }
            frameProperty.SetValue(lab, 500);
            changedFrame = UnityEngine.Time.frameCount;
            stage = 2;
        }
        else if (stage == 2 && UnityEngine.Time.frameCount - changedFrame >= 12)
        {
            var mesh = new UnityEngine.Mesh();
            try
            {
                particles.GetComponent<UnityEngine.ParticleSystemRenderer>().BakeMesh(mesh, UnityEngine.Camera.main, false);
                float maximumAlpha = 0;
                foreach (var color in mesh.colors)
                {
                    maximumAlpha = System.Math.Max(maximumAlpha, color.a);
                }
                bool passed = particles.gameObject.activeSelf && particles.particleCount == 96
                    && mesh.vertexCount > 0 && maximumAlpha > 0;
                System.IO.File.AppendAllText("Logs/particle-reactivation.txt",
                    "reactivated particles=" + particles.particleCount + " vertices=" + mesh.vertexCount
                    + " alpha=" + maximumAlpha + " passed=" + passed + "\n");
                if (!passed)
                {
                    throw new System.Exception("Reactivated owner has no visible particle geometry.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
            UnityEditor.EditorApplication.update -= tick;
        }
    }
    catch (System.Exception error)
    {
        System.IO.File.AppendAllText("Logs/particle-reactivation.txt", "FAIL " + error + "\n");
        UnityEditor.EditorApplication.update -= tick;
    }
};
UnityEditor.EditorApplication.update += tick;
return "Particle release/retire/reactivate geometry probe scheduled";
