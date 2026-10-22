// Run through Unity CLI eval_file with scene 03 playing. The callback waits for
// real rendered frames and writes Logs/technique-presentation-validation.txt.
var lab = UnityEngine.Object.FindFirstObjectByType<LineLab.Production.ProductionController>();
if (lab == null) throw new System.InvalidOperationException("Open scene 03 in Play mode.");
var original = (lab.Technique, lab.Count, lab.Full, lab.Scenario, lab.Hero, lab.Benchmarking, lab.Freeze);
var techniques = (LineLab.Technique[])System.Enum.GetValues(typeof(LineLab.Technique));
string path = "Logs/technique-presentation-validation.txt";
System.IO.File.WriteAllText(path, "Running all techniques, core and full\n");
lab.Benchmarking = true;
lab.Freeze = false;
int condition = 0;
int startedFrame = UnityEngine.Time.frameCount;
lab.Configure(techniques[0], 3, false, 0, true);
UnityEditor.EditorApplication.CallbackFunction tick = null;
tick = () =>
{
    if (UnityEngine.Time.frameCount - startedFrame < 20) return;
    try
    {
        var technique = techniques[condition % techniques.Length];
        bool full = condition >= techniques.Length;
        bool vfx = technique == LineLab.Technique.VfxBuffer || technique == LineLab.Technique.VfxPerLine
            || technique == LineLab.Technique.VfxPerLineBatched;
        bool particles = technique >= LineLab.Technique.ParticleBillboardShared && technique <= LineLab.Technique.ParticleJobs;
        int cards = lab.Catalog.AllocatedCoreQuadsPerAbility;
        if (full) foreach (int capacity in lab.Catalog.LayerCapacities) cards += capacity;
        int expected = vfx || particles ? 3 * cards : 0;
        int actual = lab.Backend.LiveParticles();
        if (actual != expected) throw new System.Exception(technique + " population " + actual + " != " + expected);
        if (technique == LineLab.Technique.ParticleJobs && lab.Backend.CallbackMs <= 0)
            throw new System.Exception("Direct particle callbacks did not run.");
        int streams = 0;
        foreach (int count in lab.Backend.StreamParticleCounts()) streams += count;
        if (streams != actual) throw new System.Exception(technique + " stream census differs from population.");
        System.IO.File.AppendAllText(path, technique + " full=" + full + " particles=" + actual
            + " callbacks=" + lab.Backend.CallbackMs + " PASS\n");
        condition++;
        if (condition == techniques.Length * 2)
        {
            System.IO.File.AppendAllText(path, "PASS: all 24 technique/core/full conditions advanced and presented.\n");
            UnityEditor.EditorApplication.update -= tick;
            lab.Benchmarking = original.Benchmarking;
            lab.Freeze = original.Freeze;
            lab.Configure(original.Technique, original.Count, original.Full, original.Scenario, original.Hero);
            return;
        }
        startedFrame = UnityEngine.Time.frameCount;
        lab.Configure(techniques[condition % techniques.Length], 3, condition >= techniques.Length, 0, true);
    }
    catch (System.Exception error)
    {
        System.IO.File.AppendAllText(path, "FAIL " + error + "\n");
        UnityEditor.EditorApplication.update -= tick;
        lab.Benchmarking = original.Benchmarking;
        lab.Freeze = original.Freeze;
        lab.Configure(original.Technique, original.Count, original.Full, original.Scenario, original.Hero);
    }
};
UnityEditor.EditorApplication.update += tick;
return "Scheduled all-technique presentation probe; results in " + path;
