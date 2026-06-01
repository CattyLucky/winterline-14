namespace Content.Shared._WL.FrozenWorld.Components;

/// <summary>
/// Marks an anchored entity that prevents a FrozenWorld shelter room from becoming valid
/// when it is located on one of the room floor tiles.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenShelterForbiddenInRoomComponent : Component
{
}
