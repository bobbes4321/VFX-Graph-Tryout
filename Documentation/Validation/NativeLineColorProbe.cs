// Execute through Unity CLI eval with the complete-abilities scene playing.
// This probes Unity's native vertex format, before the adapter's HDR reconstruction.
var lineObject = new UnityEngine.GameObject("Native HDR color probe");
var mesh = new UnityEngine.Mesh();
try
{
    var line = lineObject.AddComponent<UnityEngine.LineRenderer>();
    line.positionCount = 2;
    line.SetPositions(new[] { UnityEngine.Vector3.left * 3, UnityEngine.Vector3.right * 3 });
    line.widthMultiplier = .3f;
    line.applyActiveColorSpace = false;
    line.startColor = new UnityEngine.Color(4, 2, .5f, 1);
    line.endColor = line.startColor;
    line.BakeMesh(mesh, UnityEngine.Camera.main, false);
    float maximumRed = 0;
    foreach (var color in mesh.colors)
    {
        maximumRed = System.Math.Max(maximumRed, color.r);
    }
    return new
    {
        vertices = mesh.vertexCount,
        nativeMaximumRed = maximumRed,
        nativeFormat = mesh.GetVertexAttributeFormat(UnityEngine.Rendering.VertexAttribute.Color).ToString(),
        nativeClampsHdr = maximumRed <= 1,
    };
}
finally
{
    UnityEngine.Object.DestroyImmediate(mesh);
    UnityEngine.Object.DestroyImmediate(lineObject);
}
