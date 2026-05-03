using System;
using Content.Shared._WL.FrozenWorld;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Allocation-free value set indexed by FrozenBodyPart.
/// Used in hot thermal snapshots instead of per-snapshot dictionaries.
/// </summary>
public struct FrozenBodyPartValues
{
    public float Torso;
    public float Arms;
    public float Legs;
    public float Head;
    public float Face;
    public float Hands;
    public float Feet;

    public FrozenBodyPartValues(float value)
    {
        Torso = value;
        Arms = value;
        Legs = value;
        Head = value;
        Face = value;
        Hands = value;
        Feet = value;
    }

    public float Get(FrozenBodyPart part)
    {
        return part switch
        {
            FrozenBodyPart.Torso => Torso,
            FrozenBodyPart.Arms => Arms,
            FrozenBodyPart.Legs => Legs,
            FrozenBodyPart.Head => Head,
            FrozenBodyPart.Face => Face,
            FrozenBodyPart.Hands => Hands,
            FrozenBodyPart.Feet => Feet,
            _ => 0f,
        };
    }

    public void Set(FrozenBodyPart part, float value)
    {
        switch (part)
        {
            case FrozenBodyPart.Torso:
                Torso = value;
                break;
            case FrozenBodyPart.Arms:
                Arms = value;
                break;
            case FrozenBodyPart.Legs:
                Legs = value;
                break;
            case FrozenBodyPart.Head:
                Head = value;
                break;
            case FrozenBodyPart.Face:
                Face = value;
                break;
            case FrozenBodyPart.Hands:
                Hands = value;
                break;
            case FrozenBodyPart.Feet:
                Feet = value;
                break;
        }
    }

    public void ApplyMin(FrozenBodyPart part, float value)
    {
        Set(part, MathF.Min(Get(part), value));
    }
}
