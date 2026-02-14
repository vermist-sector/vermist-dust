using System.Data;
using System.Numerics;
using Content.Shared.Light.Components;
using Content.Shared.Weather;
using Robust.Client.Graphics;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Configuration; // imp
using Content.Shared._Impstation.CCVar;
using Robust.Shared.Serialization.Manager.Exceptions; // imp

namespace Content.Client.Overlays;

public sealed partial class StencilOverlay
{
    [Dependency] private readonly IConfigurationManager _configManager = default!; // imp

    private List<Entity<MapGridComponent>> _grids = new();

    private void DrawWeather(
        in OverlayDrawArgs args,
        CachedResources res,
        WeatherPrototype weatherProto,
        float alpha,
        Matrix3x2 invMatrix)
    {
        var worldHandle = args.WorldHandle;
        var mapId = args.MapId;
        var worldAABB = args.WorldAABB;
        var worldBounds = args.WorldBounds;
        var position = args.Viewport.Eye?.Position.Position ?? Vector2.Zero;
        var VisionSensitivity = _configManager.GetCVar(ImpCCVars.DisableWeather);// vds edit. since we use it more than once we get it once and store it here.

        if (VisionSensitivity&&!weatherProto.Veil) //imp also vds(added &&!weatherProto.Veil)
            return;

        if(VisionSensitivity && weatherProto.Veil && weatherProto.AltSprite == null)// VDS if we have the veil set to true, check that the weather proto actually has an alt texture otherwise return
            return;
        // Cut out the irrelevant bits via stencil
        // This is why we don't just use parallax; we might want specific tiles to get drawn over
        // particularly for planet maps or stations.
        worldHandle.RenderInRenderTarget(res.Blep!, () =>
        {
            var xformQuery = _entManager.GetEntityQuery<TransformComponent>();
            _grids.Clear();

            // idk if this is safe to cache in a field and clear sloth help
            _mapManager.FindGridsIntersecting(mapId, worldAABB, ref _grids);

            foreach (var grid in _grids)
            {
                var matrix = _transform.GetWorldMatrix(grid, xformQuery);
                var matty =  Matrix3x2.Multiply(matrix, invMatrix);
                worldHandle.SetTransform(matty);
                _entManager.TryGetComponent(grid.Owner, out RoofComponent? roofComp);

                foreach (var tile in _map.GetTilesIntersecting(grid.Owner, grid, worldAABB))
                {
                    // Ignored tiles for stencil
                    if (_weather.CanWeatherAffect(grid.Owner, grid, tile, roofComp))
                    {
                        continue;
                    }

                    var gridTile = new Box2(tile.GridIndices * grid.Comp.TileSize,
                        (tile.GridIndices + Vector2i.One) * grid.Comp.TileSize);

                    worldHandle.DrawRect(gridTile, Color.White);
                }
            }

        }, Color.Transparent);

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(_protoManager.Index(StencilMask).Instance());
        worldHandle.DrawTextureRect(res.Blep!.Texture, worldBounds);
        var curTime = _timing.RealTime;
        Texture sprite;
        if (VisionSensitivity && weatherProto.Veil) //check if we want to get an alt texture
            sprite = _sprite.GetFrame(weatherProto.AltSprite!, curTime); //we already checked if it was null
        else
            sprite = _sprite.GetFrame(weatherProto.Sprite, curTime);

        // Draw the rain
        worldHandle.UseShader(_protoManager.Index(StencilDraw).Instance());
        _parallax.DrawParallax(worldHandle, worldAABB, sprite, curTime, position, Vector2.Zero, modulate: (weatherProto.Color ?? Color.White).WithAlpha(alpha));

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
    }
}
