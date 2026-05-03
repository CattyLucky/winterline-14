using System;
using Content.Shared.UserInterface;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.FrozenWorld.UI;

/// <summary>
/// Client request from the fuel UI to start or stop a fuel-driven heat source.
/// The server validates fuel availability, current burn state and whether stopping is allowed.
/// </summary>
[Serializable, NetSerializable]
public sealed class FrozenHeatSourceFuelToggleIgnitionMessage : BoundUserInterfaceMessage
{
}
