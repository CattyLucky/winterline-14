namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Marks a grid as fully protected from FrozenWorld weather and gameplay cold.
/// Used for sealed vehicles such as the evacuation shuttle.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenWeatherProtectedGridComponent : Component
{
    [DataField]
    public float AmbientTemperature = 293.15f;

    [DataField]
    public float EnvironmentalTemperature = 293.15f;

    [DataField]
    public float RecoveryMultiplier = 1.5f;

    [DataField]
    public string ShelterName = "Evacuation shuttle";
}
