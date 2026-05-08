using System.Numerics;
using Content.Shared._WL.FrozenWorld.UI;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._WL.FrozenWorld.UI;

/// <summary>
/// Separate diagnostic window for FrozenWorld thermal calculations.
/// Intended for a dedicated debug BUI/admin/debug item, not for the normal player-facing thermometer window.
/// </summary>
public sealed class FrozenThermometerDebugWindow : DefaultWindow
{
    private readonly BoxContainer _content;

    private readonly Label _temperatureBase = FrozenWorldUiTheme.MakeMutedLabel();
    private readonly Label _temperatureDayNight = FrozenWorldUiTheme.MakeMutedLabel();
    private readonly Label _temperatureWeather = FrozenWorldUiTheme.MakeMutedLabel();
    private readonly Label _temperatureZone = FrozenWorldUiTheme.MakeMutedLabel();
    private readonly Label _temperatureShelter = FrozenWorldUiTheme.MakeMutedLabel();
    private readonly Label _temperatureHeat = FrozenWorldUiTheme.MakeMutedLabel();
    private readonly Label _temperatureFoot = FrozenWorldUiTheme.MakeMutedLabel();
    private readonly Label _temperatureFinal = FrozenWorldUiTheme.MakeValueLabel();
    private readonly Label _temperatureClamp = FrozenWorldUiTheme.MakeMutedLabel();

    private readonly Label _weatherName = FrozenWorldUiTheme.MakeMutedLabel();
    private readonly Label _weatherExposure = FrozenWorldUiTheme.MakeMutedLabel();
    private readonly Label _shelterName = FrozenWorldUiTheme.MakeMutedLabel();
    private readonly Label _multipliers = FrozenWorldUiTheme.MakeMutedLabel();

    private readonly Label _coldStage = FrozenWorldUiTheme.MakeMutedLabel();
    private readonly Label _coldExposure = FrozenWorldUiTheme.MakeMutedLabel();
    private readonly Label _coldWeakest = FrozenWorldUiTheme.MakeMutedLabel();

    public FrozenThermometerDebugWindow()
    {
        Title = "Thermal debug";
        SetSize = new Vector2(760, 560);
        MinWidth = 680;
        MinHeight = 460;
        Resizable = true;

        _content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(12),
        };

        var scroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        scroll.AddChild(_content);
        Contents.AddChild(scroll);

        AddSection("Temperature layers", _temperatureBase, _temperatureDayNight, _temperatureWeather, _temperatureZone,
            _temperatureShelter, _temperatureHeat, _temperatureFoot, _temperatureFinal, _temperatureClamp);
        AddSection("Weather and shelter", _weatherName, _weatherExposure, _shelterName, _multipliers);
        AddSection("Cold calculation", _coldStage, _coldExposure, _coldWeakest);
    }

    public void SetState(FrozenThermometerBoundUserInterfaceState state)
    {
        if (!state.Available)
        {
            SetUnavailable();
            return;
        }

        _temperatureBase.Text = $"Base ambient: {FormatSigned(state.BaseAmbientTemperatureCelsius)}°C";
        _temperatureDayNight.Text = $"Day/night: {FormatSigned(state.DayNightTemperatureOffsetCelsius)}°C | phase {state.DayNightPhase:0.000}";
        _temperatureWeather.Text = $"Weather offset: {FormatSigned(state.WeatherTemperatureOffsetCelsius)}°C";
        _temperatureZone.Text = $"Zone offset: {FormatSigned(state.ZoneTemperatureOffsetCelsius)}°C";
        _temperatureShelter.Text = $"Shelter bonus: {FormatSigned(state.ShelterBonusCelsius)}°C";
        _temperatureHeat.Text = $"Heat: static {FormatSigned(state.StaticHeatBonusCelsius)}°C | dynamic {FormatSigned(state.DynamicHeatBonusCelsius)}°C";
        _temperatureFoot.Text = $"Foot contact penalty: {FormatSigned(state.FootContactPenaltyCelsius)}°C";
        _temperatureFinal.Text = $"Final environmental: {FormatSigned(state.EnvironmentalTemperatureCelsius)}°C";
        _temperatureClamp.Text = state.IsEnvironmentalTemperatureClamped
            ? $"Clamp: YES | raw {FormatSigned(state.UnclampedEnvironmentalTemperatureCelsius)}°C | [{FormatSigned(state.MinEffectiveTemperatureCelsius)}; {FormatSigned(state.MaxEffectiveTemperatureCelsius)}]°C"
            : $"Clamp: no | raw {FormatSigned(state.UnclampedEnvironmentalTemperatureCelsius)}°C";

        _weatherName.Text = $"Weather: {state.ActiveWeatherName ?? "None"} | intensity {state.WeatherIntensity:0.00}";
        _weatherExposure.Text = $"Weather exposure here: {state.WeatherExposureFactor * 100f:0.#}% | affects: {state.WeatherAffectsPosition}";
        _shelterName.Text = $"Shelter: {state.ShelterName ?? "Outside"}";
        _multipliers.Text = $"Multipliers: exposure x{state.ExposureGainMultiplier:0.00} | recovery x{state.RecoveryMultiplier:0.00} | damage x{state.ColdDamageMultiplier:0.00}";

        _coldStage.Text = $"Stage: {state.Stage}";
        _coldExposure.Text = $"Exposure: {state.Exposure:0.#}/{state.MaxExposure:0.#} | severity {state.TotalColdSeverity:0.000}";
        _coldWeakest.Text = $"Weakest: {state.WeakestBodyPart} | severity {state.WeakestBodyPartSeverity:0.000}";
    }

    private void SetUnavailable()
    {
        _temperatureBase.Text = "No thermal data.";
        _temperatureDayNight.Text = string.Empty;
        _temperatureWeather.Text = string.Empty;
        _temperatureZone.Text = string.Empty;
        _temperatureShelter.Text = string.Empty;
        _temperatureHeat.Text = string.Empty;
        _temperatureFoot.Text = string.Empty;
        _temperatureFinal.Text = string.Empty;
        _temperatureClamp.Text = string.Empty;
        _weatherName.Text = string.Empty;
        _weatherExposure.Text = string.Empty;
        _shelterName.Text = string.Empty;
        _multipliers.Text = string.Empty;
        _coldStage.Text = string.Empty;
        _coldExposure.Text = string.Empty;
        _coldWeakest.Text = string.Empty;
    }

    private void AddSection(string title, params Label[] labels)
    {
        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = FrozenWorldUiTheme.Panel(
                FrozenWorldUiTheme.SurfacePanel,
                FrozenWorldUiTheme.Border,
                1,
                12,
                12,
                10,
                10),
            Margin = new Thickness(0, 0, 0, 8),
        };

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };

        var header = FrozenWorldUiTheme.MakeValueLabel(title);
        header.FontColorOverride = FrozenWorldUiTheme.ColdAccent;
        box.AddChild(header);
        box.AddChild(new Control { MinSize = new Vector2(1, 6) });

        foreach (var label in labels)
        {
            label.HorizontalExpand = true;
            box.AddChild(label);
        }

        panel.AddChild(box);
        _content.AddChild(panel);
    }

    private static string FormatSigned(float value)
    {
        return value >= 0f ? $"+{value:0.#}" : $"{value:0.#}";
    }
}
