using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Client._VDS.Audio.Components;
using Content.Shared._VDS.Audio.Components;
using Content.Shared._VDS.Physics;
using Content.Shared.Coordinates;
using Content.Shared.Physics;
using JetBrains.Annotations;
using Robust.Shared.Physics.Systems;

namespace Content.Client._VDS.Audio;

public sealed partial class AdvancedAcousticsSystem
{
    /// <summary>
    /// Max amount of times single acoustic ray is allowed to bounce
    /// Set by VCCVars
    /// </summary>
    private int _acousticMaxReflections;

    /// <summary>
    /// The directions that are raycasted.
    /// Used relative to the grid.
    /// </summary>
    private Angle[] _calculatedDirections =
    [
        Direction.North.ToAngle(),
        Direction.West.ToAngle(),
        Direction.South.ToAngle(),
        Direction.East.ToAngle(),
    ];

    /// <summary>
    /// Attempts to cast and gather environmental <see cref="AcousticRayResults"/> around <paramref name="originEnt"/>.
    /// <seealso cref="ReflectiveRaycastSystem"/>
    /// </summary>
    /// <param name="originEnt">The origin of our raycasts.</param>
    /// <param name="maxRange">Maximum range of a ray.</param>
    /// <param name="maxBounces">How many times a ray is allowed to bounce before terminating early.</param>
    /// <param name="castDirections">What angles our rays will shoot out from.</param>
    /// <param name="acousticResults">A list of <see cref="AcousticRayResults"/>.</param>
    /// <returns>True if <paramref name="acousticResults"/> has data, false if <paramref name="acousticResults"/> is null or empty.</returns>
    [PublicAPI]
    public bool TryCastAndGetEnvironmentAcousticData(
        in EntityUid originEnt,
        in int maxBounces,
        in Angle[] castDirections,
        [NotNullWhen(true)] out List<AcousticRayResults>? acousticResults,
        AcousticSettingsComponent? settings = null)
    {
        if (_reverbPresets.Count == 0 || !_acousticSettingsQuery.Resolve(originEnt, ref settings))
        {
            acousticResults = null;
            return false;
        }

        acousticResults = new List<AcousticRayResults>(castDirections.Length);

        if (!originEnt.IsValid() || !_transformQuery.HasComponent(originEnt))
        {
            return false;
        }

        // in space nobody can hear your awesome freaking acoustics
        if (!_turfSystem.TryGetTileRef(originEnt.ToCoordinates(), out var tileRef)
            || _turfSystem.IsSpace(tileRef.Value))
        {
            return false;
        }

        var clientTransform = Transform(originEnt);
        var clientMapId = clientTransform.MapID;
        var clientCoords = _transformSystem.ToMapCoordinates(clientTransform.Coordinates).Position;

        // our path filter, which will return AcousticDataComponent entities our ray passes through
        var pathFilter = new QueryFilter
        {
            MaskBits = (int)CollisionGroup.AllMask,
            IsIgnored = ent => !_acousticQuery.HasComp(ent), // ideally we'd pass _acousticQuery via state, but the new ray system doesn't allow that for some reason
            Flags = QueryFlags.Static | QueryFlags.Dynamic
        };

        // our probe filter, which determines what our rays will bounce off of.
        var probeFilter = new QueryFilter
        {
            MaskBits = (int)CollisionGroup.AllMask,
            LayerBits = (int)CollisionGroup.None,
            IsIgnored = ent => _acousticQuery.TryGetComponent(ent, out var comp) && !comp.ReflectRay,
            Flags = QueryFlags.Static | QueryFlags.Dynamic
        };

        // our current ray state, which is passed through and altered by ref.
        // instead of making states for each ray we will just reuse one and reset it
        // before passing it back in for the next direction. for performance or whatever.
        var state = new ReflectiveRayState(
            probeFilter,
            pathFilter,
            origin: clientCoords,
            direction: Vector2.Zero, // we change the dir later
            maxRange: _reverbPresets.Keys[^1],
            clientMapId);

        // cast our rays and get our results
        acousticResults = CastManyReflectiveAcousticRays(
            in originEnt,
            clientCoords,
            in maxBounces,
            in castDirections,
            in settings,
            ref state);

        return acousticResults.Count != 0;
    }

    /// <summary>
    /// Casts many bouncing rays.
    /// <seealso cref="ReflectiveRaycastSystem"/>
    /// <seealso cref="CastReflectiveAcousticRay(in EntityUid, in int, ref ReflectiveRayState)"/>
    /// </summary>
    [PublicAPI]
    public List<AcousticRayResults> CastManyReflectiveAcousticRays(
        in EntityUid originEnt,
        Vector2 originCoords,
        in int maxBounces,
        in Angle[] castDirections,
        in AcousticSettingsComponent settings,
        ref ReflectiveRayState state)
    {
        var acousticResults = new List<AcousticRayResults>();

        foreach (var direction in castDirections)
        {
            state.CurrentPos = originCoords;
            state.OldPos = originCoords;
            state.Direction = (direction + _random.NextFloat(-settings.DirectionRandomOffset, settings.DirectionRandomOffset)).ToVec();
            state.Translation = state.Direction * state.MaxRange;
            state.ProbeTranslation = state.Translation;
            state.RemainingDistance = state.MaxRange;

            // handle individual bounces
            var results = CastReflectiveAcousticRay(in originEnt, in maxBounces, in settings, ref state);
            acousticResults.Add(results);
        }

        return acousticResults;
    }

    /// <summary>
    /// Casts a bouncing ray.
    /// <seealso cref="ReflectiveRaycastSystem"/>
    /// </summary>
    /// <param name="originEnt">The entity to compare absorption falloff to. <see cref="GetAcousticAbsorption(RayHit,
    /// in EntityUid, in float, in AcousticDataComponent)/></param>
    /// <param name="state"><see cref="ReflectiveRayState"/></param>
    /// <returns><see cref="AcousticRayResults"/>, in order hit, including whatever the ray bounced off.</returns>
    [PublicAPI]
    public AcousticRayResults CastReflectiveAcousticRay(
        in EntityUid originEnt,
        in int maxBounces,
        in AcousticSettingsComponent settings,
        ref ReflectiveRayState state)
    {
        var results = new AcousticRayResults();
        for (var bounce = 0; bounce <= maxBounces; bounce++)
        {
            /*
                our raycast state will constantly be fed by reference into the reflective raycast API,
                which updates the reference's positional data for us, including the handling of
                bounces with each iteration (provided we pass the ref back through).
                we also get a new list of entities for each iteration so we
                can do component data gathering on them.
            */
            var (probeResult, pathResults) = _reflectiveRaycast.CastAndUpdateReflectiveRayStateRef(ref state);

            results.TotalRange += state.CurrentSegmentDistance;
            if (probeResult.Hit)
            {
                pathResults.Results.Add(probeResult.Results[0]); // we wanna include what we hit to our data too
                results.TotalBounces++;
            }

            // gather acoustic component data
            if (pathResults.Results.Count > 0)
            {
                foreach (var result in pathResults.Results)
                {
                    if (!_acousticQuery.TryGetComponent(result.Entity, out var comp))
                        continue;

                    // TODO: more component data can be gathered here in the future
                    results.TotalAbsorption += GetAcousticAbsorption(result, in originEnt, in comp, in settings);
                }
            }

            // this ray is long enough to be considered in an open area and now shall be ignored
            if (state.CurrentSegmentDistance >= state.MaxRange * settings.EscapeDistancePercentage)
            {
                results.TotalEscapes++;
                break;
            }

            // expended our range budget, break the loop
            if (results.TotalRange >= state.MaxRange)
                break;
        }

        return results;
    }

    /// <summary>
    /// Gets an absorption percentage, scaled by distance.
    /// </summary>
    private float GetAcousticAbsorption(
        RayHit result,
        in EntityUid originEnt,
        in AcousticDataComponent comp,
        in AcousticSettingsComponent settings)
    {
        if (!result.Entity.ToCoordinates().TryDistance(
            EntityManager,
            originEnt.ToCoordinates(),
            out var distance))
        {
            return 0f;
        }
        var normalized = (25f - Math.Clamp(distance, 5f, 25f)) / (25f - 5f);

        // Log.Info($"abosrb rfays :{MathF.Log(normalized + 1f) * 2f}");
        return MathF.Log(normalized + 1f) * 2f;
    }

    /// <summary>
    /// Returns all four cardinal directions when <paramref name="highResolution"/> is false.
    /// Otherwise, returns all eight intercardinal and cardinal directions as listed in
    /// <see cref="DirectionExtensions.AllDirections"/>.
    /// </summary>
    private static Angle[] GetEffectiveDirections(bool highResolution)
    {
        if (highResolution)
        {
            var allDirections = DirectionExtensions.AllDirections;
            var directions = new Angle[allDirections.Length];

            for (var i = 0; i < allDirections.Length; i++)
                directions[i] = allDirections[i].ToAngle();

            return directions;
        }

        return
        [
            Direction.North.ToAngle(),
            Direction.West.ToAngle(),
            Direction.South.ToAngle(),
            Direction.East.ToAngle(),
        ];
    }
}
