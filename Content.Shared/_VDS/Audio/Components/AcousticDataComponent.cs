using Robust.Shared.GameStates;

namespace Content.Shared._VDS.Audio.Components;

/// <summary>
/// Data that alters audio if the client has acoustics enabled.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AcousticDataComponent : Component
{
    /// <summary>
    /// How much sound should be absorbed by this entity. 
    /// 0f to 1f, where 1f absorbs everything and reflects nothing.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Absorption = 0f;

    /// <summary>
    /// Represents how focused this material's reflections are. 
    /// 0f to inf, where 1f is diffused (scattered), and higher
    /// values are more specular.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Reflectivity = 1f;

    /// <summary>
    /// How much sound passes through this entity.
    /// 0f to 1f, where 1f means sound passes through unimpeded.
    /// Currently doesn't do anything actually related to occlusion, but
    /// is used in reverb calculations.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Transmission = 1f;

    /// <summary>
    /// If the acoustic ray will pass through this entity, or bounce off of it (like a wall) instead.
    /// </summary>
    [DataField]
    public bool ReflectRay = false;
}
