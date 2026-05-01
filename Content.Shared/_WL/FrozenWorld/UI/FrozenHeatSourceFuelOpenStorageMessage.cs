using System;
using Content.Shared.UserInterface;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.FrozenWorld.UI;

/// <summary>
/// Client request from the FrozenWorld fuel status UI to open the normal Storage UI
/// for the same heat-source entity. The server validates the actor and storage component.
/// </summary>
[Serializable, NetSerializable]
public sealed class FrozenHeatSourceFuelOpenStorageMessage : BoundUserInterfaceMessage
{
}
