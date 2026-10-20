#ifndef LINE_LAB_COMMON
#define LINE_LAB_COMMON
struct LabLine
{
    float4 start;
    float4 end;
    float4 color;
    float4 shape;
};
float3 LabPoint(LabLine l, float t, float clock)
{
    float3 p = lerp(l.start.xyz, l.end.xyz, t);
    float e = sin(t * 3.14159265);
    if (l.shape.x < .5)
        p.y += e * (.45 + .10 * sin(t * 16 - clock * 3 + l.shape.y));
    else if (l.shape.x < 1.5)
    {
        p.y += e * .65 * sin(t * 43 + floor(clock * 12) * 2.3 + l.shape.y);
        p.z += e * .25 * cos(t * 37 - clock * 9 + l.shape.y);
    }
    else if (l.shape.x < 2.5)
        p.y += e * .16 * sin(t * 23 - clock * 8);
    return p;
}
float4 LabTint(LabLine l, float t, float clock)
{
    float intensity = .65 + .35 * pow(.5 + .5 * cos(t * 19 - clock * 5 + l.shape.y), 3);
    if (l.shape.x > 2.5)
        intensity *= frac(t * 9 - clock * .8) < .65 ? 1 : .03;
    return float4(l.color.xyz * intensity, l.color.w * l.start.w);
}
#endif
