/*
 
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at http://mozilla.org/MPL/2.0/.
 
  Copyright (C) 2009-2020 Michael Möller <mmoeller@openhardwaremonitor.org>
	
*/

using OpenHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;

namespace OpenHardwareMonitor.GUI;

public class SensorNode : Node
{
    private ISensor sensor;
    private PersistentSettings settings;
    private UnitManager unitManager;
    private string fixedFormat;
    private bool plot = false;
    private Color? penColor = null;
    private PlotPanel.LineDisplay lineDisplay = PlotPanel.LineDisplay.Line;
    private int lineAveraging = DefaultLineAveraging;

    // the stored averages of 4 updates
    public const int DefaultLineAveraging = 4;
    // width, style and fill of each display, kept when switching between
    // them; the color is shared
    private readonly Dictionary<PlotPanel.LineDisplay, PlotPanel.PlotLine> lines =
        new Dictionary<PlotPanel.LineDisplay, PlotPanel.PlotLine>();

    public string ValueToString(double? value)
    {
        if (value.HasValue)
        {
            switch (sensor.SensorType)
            {
                case SensorType.Temperature:
                    if (unitManager.TemperatureUnit == TemperatureUnit.Fahrenheit)
                        return string.Format("{0:F1} °F", value * 1.8 + 32);
                    else
                        return string.Format("{0:F1} °C", value);
                case SensorType.Throughput:
                    if (value < 1)
                        return string.Format("{0:F1} KB/s", value * 0x400);
                    else
                        return string.Format("{0:F1} MB/s", value);
                case SensorType.TimeSpan:
                    return FormatDuration(TimeSpan.FromSeconds(value.Value));
                default:
                    return string.Format(fixedFormat, value);
            }
        }
        else
            return "-";
    }

    // In the style of Go durations, limited to 3 separators (units and decimal
    // separator) from the largest unit: 5d07h13m, 2h11m35s, 11m35.05s, 7.50s.
    // The units after the first one are zero padded, so the values keep their
    // width. Truncated rather than rounded, not to show 60s or 60m.
    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.Days > 0)
            return duration.Days + "d" + duration.Hours.ToString("00") + "h" +
                duration.Minutes.ToString("00") + "m";
        if (duration.Hours > 0)
            return duration.Hours + "h" + duration.Minutes.ToString("00") + "m" +
                duration.Seconds.ToString("00") + "s";

        double seconds = Math.Floor(
            (duration.Ticks % TimeSpan.TicksPerMinute) / (TimeSpan.TicksPerSecond / 100.0)) / 100;
        if (duration.Minutes > 0)
            return duration.Minutes + "m" + seconds.ToString("00.00") + "s";
        return seconds.ToString("0.00") + "s";
    }

    public string Format
    {
        get;
        set;
    } = String.Empty;

    public string Unit()
    {
        switch (sensor.SensorType)
        {
            case SensorType.Voltage: return "V";
            case SensorType.Clock: return "MHz";
            case SensorType.Load: return "%";
            case SensorType.Fan: return "RPM";
            case SensorType.Flow: return "L/h";
            case SensorType.Control: return "%";
            case SensorType.Level: return "%";
            case SensorType.Power: return "W";
            case SensorType.Data: return "GB";
            case SensorType.SmallData: return "MB";
            case SensorType.Throughput: return "MB/s";
            case SensorType.Temperature: return "C";
            default: return string.Empty;
        }
    }

    public SensorNode(ISensor sensor, PersistentSettings settings,
        UnitManager unitManager) : base()
    {
        this.sensor = sensor;
        this.settings = settings;
        this.unitManager = unitManager;
        switch (sensor.SensorType)
        {
            case SensorType.Voltage: fixedFormat = "{0:F3} V"; break;
            case SensorType.Clock: fixedFormat = "{0:F1} MHz"; break;
            case SensorType.Load: fixedFormat = "{0:F1} %"; break;
            case SensorType.Fan: fixedFormat = "{0:F0} RPM"; break;
            case SensorType.Flow: fixedFormat = "{0:F0} L/h"; break;
            case SensorType.Control: fixedFormat = "{0:F1} %"; break;
            case SensorType.Level: fixedFormat = "{0:F1} %"; break;
            case SensorType.Power: fixedFormat = "{0:F1} W"; break;
            case SensorType.Data: fixedFormat = "{0:F1} GB"; break;
            case SensorType.SmallData: fixedFormat = "{0:F1} MB"; break;
            case SensorType.Factor: fixedFormat = "{0:F3}"; break;
            case SensorType.RawValue:
                fixedFormat = "{0:F0}";
                break;
            default: fixedFormat = ""; break;
        }

        bool hidden = settings.GetValue(new Identifier(sensor.Identifier,
            "hidden").ToString(), sensor.IsDefaultHidden);
        base.IsVisible = !hidden;

        this.Plot = settings.GetValue(new Identifier(sensor.Identifier,
            "plot").ToString(), false);

        string id = new Identifier(sensor.Identifier, "penColor").ToString();
        if (settings.Contains(id))
            this.PenColor = settings.GetValue(id, Color.Black);

        foreach (PlotPanel.LineDisplay display in Enum.GetValues(typeof(PlotPanel.LineDisplay)))
        {
            PlotPanel.PlotLine line = DefaultLine(display);
            line.Width = settings.GetValue(SettingId(display, "Width"), line.Width);
            if (Enum.TryParse(settings.GetValue(SettingId(display, "Style"), null),
                    out PlotPanel.LinePattern pattern))
                line.Style = pattern;
            line.FillOpacity = settings.GetValue(SettingId(display, "FillOpacity"),
                line.FillOpacity);
            lines[display] = line;
        }
        if (!Enum.TryParse(settings.GetValue(new Identifier(sensor.Identifier,
                "lineDisplay").ToString(), null), out lineDisplay))
            lineDisplay = PlotPanel.LineDisplay.Line;
        lineAveraging = Math.Max(1, Math.Min(DefaultLineAveraging,
            settings.GetValue(new Identifier(sensor.Identifier,
                "lineAveraging").ToString(), DefaultLineAveraging)));
    }

    public override string Text
    {
        get { return sensor.Name; }
        set { sensor.Name = value; }
    }

    public override string NodeId => sensor.Identifier.ToString();

    public override bool IsVisible
    {
        get { return base.IsVisible; }
        set
        {
            base.IsVisible = value;
            settings.SetValue(new Identifier(sensor.Identifier,
                "hidden").ToString(), !value);
        }
    }

    public Color? PenColor
    {
        get { return penColor; }
        set
        {
            penColor = value;

            string id = new Identifier(sensor.Identifier, "penColor").ToString();
            if (value.HasValue)
                settings.SetValue(id, value.Value);
            else
                settings.Remove(id);

            if (PlotSelectionChanged != null)
                PlotSelectionChanged(this, null);
        }
    }

    // Width, style and fill of the current display.
    // Opacity in percent of the area filled under the plot line, 0 for none
    public int FillOpacity
    {
        get { return lines[lineDisplay].FillOpacity; }
    }

    public float LineWidth
    {
        get { return lines[lineDisplay].Width; }
    }

    public PlotPanel.LinePattern LinePattern
    {
        get { return lines[lineDisplay].Style; }
    }

    // Width, style and fill of a display, current or not (Color is unset)
    public PlotPanel.PlotLine GetLine(PlotPanel.LineDisplay display)
    {
        PlotPanel.PlotLine line = lines[display];
        return new PlotPanel.PlotLine
        {
            Display = display,
            Width = line.Width,
            Style = line.Style,
            FillOpacity = line.FillOpacity
        };
    }

    // Bars are filled and have no outline by default
    public static PlotPanel.PlotLine DefaultLine(PlotPanel.LineDisplay display)
    {
        bool bar = display == PlotPanel.LineDisplay.Bar;
        return new PlotPanel.PlotLine
        {
            Display = display,
            Width = bar ? 0 : 1,
            Style = PlotPanel.LinePattern.Solid,
            FillOpacity = bar ? 50 : 0
        };
    }

    // The line keeps the names of the settings from before the bars:
    // lineWidth, lineStyle, fillOpacity; then barWidth, barStyle, ...
    private string SettingId(PlotPanel.LineDisplay display, string name)
    {
        if (display == PlotPanel.LineDisplay.Line)
            name = name == "FillOpacity" ? "fillOpacity" : "line" + name;
        else
            name = display.ToString().ToLowerInvariant() + name;
        return new Identifier(sensor.Identifier, name).ToString();
    }

    public PlotPanel.LineDisplay LineDisplay
    {
        get { return lineDisplay; }
    }

    // Seconds the line averages over, 1 to 4 (see PlotPanel.PlotLine). The
    // plot is updated by the next SetLine or SetLines.
    public int LineAveraging
    {
        get { return lineAveraging; }
        set
        {
            lineAveraging = value;
            string id = new Identifier(sensor.Identifier, "lineAveraging").ToString();
            if (value != DefaultLineAveraging)
                settings.SetValue(id, value);
            else
                settings.Remove(id);
        }
    }

    // Sets the display, the color, and the width, style and fill of that
    // display at once, so the plot is updated only once. The default values
    // aren't stored.
    public void SetLine(PlotPanel.LineDisplay display, Color? color, float width,
        PlotPanel.LinePattern pattern, int fill)
    {
        SetLine(display, color, width, pattern, fill, true);
    }

    private void SetLine(PlotPanel.LineDisplay display, Color? color, float width,
        PlotPanel.LinePattern pattern, int fill, bool update)
    {
        lineDisplay = display;
        penColor = color;
        PlotPanel.PlotLine line = lines[display];
        line.Width = width;
        line.Style = pattern;
        line.FillOpacity = fill;

        string id = new Identifier(sensor.Identifier, "lineDisplay").ToString();
        if (display != PlotPanel.LineDisplay.Line)
            settings.SetValue(id, display.ToString());
        else
            settings.Remove(id);

        id = new Identifier(sensor.Identifier, "penColor").ToString();
        if (color.HasValue)
            settings.SetValue(id, color.Value);
        else
            settings.Remove(id);

        PlotPanel.PlotLine defaults = DefaultLine(display);

        id = SettingId(display, "Width");
        if (width != defaults.Width)
            settings.SetValue(id, width);
        else
            settings.Remove(id);

        id = SettingId(display, "Style");
        if (pattern != defaults.Style)
            settings.SetValue(id, pattern.ToString());
        else
            settings.Remove(id);

        id = SettingId(display, "FillOpacity");
        if (fill != defaults.FillOpacity)
            settings.SetValue(id, fill);
        else
            settings.Remove(id);

        if (update && PlotSelectionChanged != null)
            PlotSelectionChanged(this, null);
    }

    // Restores the display and the settings of all the displays, as got from
    // GetLine: when the customization is cancelled
    public void SetLines(PlotPanel.LineDisplay display, Color? color,
        IEnumerable<PlotPanel.PlotLine> displays)
    {
        foreach (PlotPanel.PlotLine line in displays)
            SetLine(line.Display, color, line.Width, line.Style, line.FillOpacity,
                false);
        PlotPanel.PlotLine current = lines[display];
        SetLine(display, color, current.Width, current.Style, current.FillOpacity);
    }

    public void ResetLine()
    {
        LineAveraging = DefaultLineAveraging;
        SetLines(PlotPanel.LineDisplay.Line, null,
            lines.Keys.ToList().Select(DefaultLine));
    }

    public bool Plot
    {
        get { return plot; }
        set
        {
            plot = value;
            settings.SetValue(new Identifier(sensor.Identifier, "plot").ToString(),
                value);
            if (PlotSelectionChanged != null)
                PlotSelectionChanged(this, null);
        }
    }

    public event EventHandler PlotSelectionChanged;

    public ISensor Sensor
    {
        get { return sensor; }
    }

    public string Value
    {
        get { return ValueToString(sensor.Value); }
    }

    public string Min
    {
        get { return ValueToString(sensor.Min); }
    }

    public string Max
    {
        get { return ValueToString(sensor.Max); }
    }

    public override bool Equals(System.Object obj)
    {
        if (obj == null)
            return false;

        SensorNode s = obj as SensorNode;
        if (s == null)
            return false;

        return (sensor == s.sensor);
    }

    public override int GetHashCode()
    {
        return sensor.GetHashCode();
    }

}
