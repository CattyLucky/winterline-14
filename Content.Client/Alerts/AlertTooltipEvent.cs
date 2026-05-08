using Content.Shared.Alert;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client.Alerts;

// WL-Change Start: dynamic alert tooltip extension point.
/// <summary>
/// Raised before a HUD alert tooltip is built.
/// Client systems may replace Name/Description without making AlertControl depend on game-specific code.
/// </summary>
[ByRefEvent]
public record struct AlertTooltipEvent
{
    public readonly AlertPrototype Alert;
    public readonly short? Severity;

    public FormattedMessage Name;
    public FormattedMessage Description;

    public AlertTooltipEvent(
        AlertPrototype alert,
        short? severity,
        FormattedMessage name,
        FormattedMessage description)
    {
        Alert = alert;
        Severity = severity;
        Name = name;
        Description = description;
    }
}
// WL-Change End
