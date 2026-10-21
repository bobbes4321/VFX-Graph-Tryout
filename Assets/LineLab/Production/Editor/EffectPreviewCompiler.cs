using UnityEditor;

namespace LineLab.Production.Editor
{
    /// <summary>Editor preview can compile a newly requested pool size; the player never authors graphs.</summary>
    [InitializeOnLoad]
    static class EffectPreviewCompiler
    {
        static EffectPreviewCompiler()
        {
            ProductionController.BeforePreviewConfiguration = (library, count) =>
                EffectGraphCompiler.CompileLibrary(library, false, count);
        }
    }
}
