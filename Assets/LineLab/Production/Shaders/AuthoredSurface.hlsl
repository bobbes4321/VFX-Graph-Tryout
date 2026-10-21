#ifndef AUTHORED_SURFACE_INCLUDED
#define AUTHORED_SURFACE_INCLUDED
float2 AuthoredAtlasUv(float2 uv, float tile, float grid)
{
    return (float2(fmod(tile, grid), floor(tile / grid)) + uv) / grid;
}
#endif
