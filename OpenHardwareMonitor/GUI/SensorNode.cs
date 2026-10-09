/*
 
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at http://mozilla.org/MPL/2.0/.
 
  Copyright (C) 2009-2020 Michael Möller <mmoeller@openhardwaremonitor.org>
	
*/

using OpenHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
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
    private int fillOpacity = 0;
    private float lineWidth = DefaultLineWidth;
    private PlotPanel.LinePattern linePattern = PlotPanel.LinePattern.Solid;
    private PlotPanel.LineDisplay lineDisplay = PlotPanel.LineDisplay.Line;

    public const float DefaultLineWidth = 1;

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

        this.fillOpacity = settings.GetValue(new Identifier(sensor.Identifier,
            "fillOpacity").ToString(), 0);
        this.lineWidth = settings.GetValue(new Identifier(sensor.Identifier,
            "lineWidth").ToString(), DefaultLineWidth);
        if (!Enum.TryParse(settings.GetValue(new Identifier(sensor.Identifier,
                "lineStyle").ToString(), null), out linePattern))
            linePattern = PlotPanel.LinePattern.Solid;
        if (!Enum.TryParse(settings.GetValue(new Identifier(sensor.Identifier,
                "lineDisplay").ToString(), null), out lineDisplay))
            lineDisplay = PlotPanel.LineDisplay.Line;
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

    // Opacity in percent of the area filled under the plot line, 0 for none
    public int FillOpacity
    {
        get { return fillOpacity; }
    }

    public float LineWidth
    {
        get { return lineWidth; }
    }

    public PlotPanel.LinePattern LinePattern
    {
        get { return linePattern; }
    }

    public PlotPanel.LineDisplay LineDisplay
    {
        get { return lineDisplay; }
    }

    // Sets the whole customization of the plot line at once, so the plot is
    // updated only once. The default values aren't stored.
    public void SetLine(PlotPanel.LineDisplay display, Color? color, float width,
        PlotPanel.LinePattern pattern, int fill)
    {
        lineDisplay = display;
        penColor = color;
        lineWidth = width;
        linePattern = pattern;
        fillOpacity = fill;

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

        id = new Identifier(sensor.Identifier, "lineWidth").ToString();
        if (width != DefaultLineWidth)
            settings.SetValue(id, width);
        else
            settings.Remove(id);

        id = new Identifier(sensor.Identifier, "lineStyle").ToString();
        if (pattern != PlotPanel.LinePattern.Solid)
            settings.SetValue(id, pattern.ToString());
        else
            settings.Remove(id);

        id = new Identifier(sensor.Identifier, "fillOpacity").ToString();
        if (fill > 0)
            settings.SetValue(id, fill);
        else
            settings.Remove(id);

        if (PlotSelectionChanged != null)
            PlotSelectionChanged(this, null);
    }

    public void ResetLine()
    {
        SetLine(PlotPanel.LineDisplay.Line, null, DefaultLineWidth,
            PlotPanel.LinePattern.Solid, 0);
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
