/*
 
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at http://mozilla.org/MPL/2.0/.
 
  Copyright (C) 2009-2013 Michael Möller <mmoeller@openhardwaremonitor.org>
	
*/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using OpenHardwareMonitor.Hardware;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.WindowsForms;
using OxyPlot.Series;
using OpenHardwareMonitor.Collections;

namespace OpenHardwareMonitor.GUI {
  public class PlotPanel : UserControl {

    private readonly PersistentSettings settings;
    private readonly UnitManager unitManager;

    // gap between stacked panels, in pixels
    private const double StackGap = 4;
    // margin kept around the values of a panel, in pixels
    private const double ValueMargin = 3;

    private readonly PlotView plot;
    private readonly StackedPlotModel model;
    private readonly ViewTimeSpanAxis timeAxis = new ViewTimeSpanAxis();
    // value axes, one per sensor type plus the network throughput, from the
    // top panel to the bottom one
    private readonly List<ViewLinearAxis> axes = new List<ViewLinearAxis>();
    private readonly Dictionary<SensorType, ViewLinearAxis> typeAxes =
      new Dictionary<SensorType, ViewLinearAxis>();
    // the network and GPU (PCIe) throughputs have their own panels, apart from
    // the drives: their scales are too different
    private ViewLinearAxis networkAxis;
    private ViewLinearAxis pcieAxis;

    private UserOption stackedAxes;
    private UserOption axisLabels;
    private UserOption blendBars;

    private DateTime now;
    private bool paused;

    // right button drag pans the plot, so the context menu must not open
    // when the button is released after a drag
    private Rectangle rightDragBox = Rectangle.Empty;
    private bool rightDragged;

    public PlotPanel(PersistentSettings settings, UnitManager unitManager) {
      this.settings = settings;
      this.unitManager = unitManager;

      this.Text = "Time Plot";

      this.model = CreatePlotModel();

      this.plot = new PlotView();
      this.plot.Dock = DockStyle.Fill;
      this.plot.Model = model;
      this.plot.Controller = CreateController();
      this.plot.BackColor = Color.White;
      this.plot.ContextMenuStrip = CreateMenu();
      this.plot.MouseDown += PlotMouseDown;
      this.plot.MouseMove += PlotMouseMove;
      this.plot.ContextMenuStrip.Opening += (sender, e) => {
        if (rightDragged) {
          e.Cancel = true;
          rightDragged = false;
        }
      };

      UpdateAxesPosition();

      this.SuspendLayout();
      this.Controls.Add(plot);
      this.ResumeLayout(true);
    }

    private void PlotMouseDown(object sender, MouseEventArgs e) {
      if (e.Button != MouseButtons.Right)
        return;
      Size dragSize = SystemInformation.DragSize;
      rightDragBox = new Rectangle(
        e.X - dragSize.Width / 2, e.Y - dragSize.Height / 2,
        dragSize.Width, dragSize.Height);
      rightDragged = false;
    }

    private void PlotMouseMove(object sender, MouseEventArgs e) {
      if ((e.Button & MouseButtons.Right) != 0 && !rightDragBox.Contains(e.Location))
        rightDragged = true;
    }

    public void SetCurrentSettings() {
      // not "plotPanel.Min/MaxTimeSpan": these keys belong to the value axis
      // of SensorType.TimeSpan below
      settings.SetValue("plotPanel.MinTimeWindow", (float)timeAxis.ZoomMinimum);
      settings.SetValue("plotPanel.MaxTimeWindow", (float)timeAxis.ZoomMaximum);

      foreach (var axis in axes) {
        settings.SetValue("plotPanel.Min" + axis.Key, (float)axis.ZoomMinimum);
        settings.SetValue("plotPanel.Max" + axis.Key, (float)axis.ZoomMaximum);
      }
    }

    private ContextMenuStrip CreateMenu() {
      ContextMenuStrip menu = new ContextMenuStrip();

      ToolStripMenuItem stackedAxesMenuItem = new ToolStripMenuItem("Stacked Axes");
      stackedAxes = new UserOption("stackedAxes", true,
        stackedAxesMenuItem, settings);
      stackedAxes.Changed += (sender, e) => {
        UpdateAxesPosition();
        InvalidatePlot();
      };
      menu.Items.Add(stackedAxesMenuItem);

      ToolStripMenuItem axisLabelsMenuItem = new ToolStripMenuItem("Axis Labels");
      axisLabels = new UserOption("axisLabels", true,
        axisLabelsMenuItem, settings);
      axisLabels.Changed += (sender, e) => {
        model.PlotMargins = ((UserOption)sender).Value ? new OxyThickness(double.NaN) : new OxyThickness(0);
      };
      menu.Items.Add(axisLabelsMenuItem);

      ToolStripMenuItem blendBarsMenuItem = new ToolStripMenuItem("Blend Overlapping Bars");
      blendBars = new UserOption("plotBlendBars", true,
        blendBarsMenuItem, settings);
      model.BlendBars = blendBars.Value;
      blendBars.Changed += (sender, e) => {
        model.BlendBars = blendBars.Value;
        InvalidatePlot();
      };
      menu.Items.Add(blendBarsMenuItem);

      // the sensors keep recording; not saved, the plot isn't paused on start
      ToolStripMenuItem pauseMenuItem = new ToolStripMenuItem("Pause");
      pauseMenuItem.Click += (sender, e) => {
        paused = !paused;
        pauseMenuItem.Checked = paused;
        InvalidatePlot();
      };
      menu.Items.Add(pauseMenuItem);

      ToolStripMenuItem timeWindow = new ToolStripMenuItem("Time Window");
      ToolStripMenuItem[] timeWindowMenuItems =
        {
          new ToolStripMenuItem("Auto", null,
            (s, e) => { timeAxis.SetWindow(0, double.NaN); InvalidatePlot(); }),
          new ToolStripMenuItem("5 min",  null,
            (s, e) => { timeAxis.SetWindow(0, 5 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("10 min",  null,
            (s, e) => { timeAxis.SetWindow(0, 10 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("20 min",  null,
            (s, e) => { timeAxis.SetWindow(0, 20 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("30 min",  null,
            (s, e) => { timeAxis.SetWindow(0, 30 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("45 min",  null,
            (s, e) => { timeAxis.SetWindow(0, 45 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("1 h",  null,
            (s, e) => { timeAxis.SetWindow(0, 60 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("1.5 h",  null,
            (s, e) => { timeAxis.SetWindow(0, 1.5 * 60 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("2 h",  null,
            (s, e) => { timeAxis.SetWindow(0, 2 * 60 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("3 h",  null,
            (s, e) => { timeAxis.SetWindow(0, 3 * 60 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("6 h",  null,
            (s, e) => { timeAxis.SetWindow(0, 6 * 60 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("12 h",  null,
            (s, e) => { timeAxis.SetWindow(0, 12 * 60 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("24 h",  null,
            (s, e) => { timeAxis.SetWindow(0, 24 * 60 * 60); InvalidatePlot(); }) };
      foreach (ToolStripMenuItem mi in timeWindowMenuItems)
        timeWindow.DropDownItems.Add(mi);
      menu.Items.Add(timeWindow);

      return menu;
    }

    private StackedPlotModel CreatePlotModel() {

      timeAxis.Position = AxisPosition.Bottom;
      timeAxis.MajorGridlineStyle = LineStyle.Solid;
      timeAxis.MajorGridlineThickness = 1;
      timeAxis.MajorGridlineColor = OxyColor.FromRgb(192, 192, 192);
      timeAxis.MinorGridlineStyle = LineStyle.Solid;
      timeAxis.MinorGridlineThickness = 1;
      timeAxis.MinorGridlineColor = OxyColor.FromRgb(232, 232, 232);
      timeAxis.StartPosition = 1;
      timeAxis.EndPosition = 0;
      timeAxis.MinimumPadding = 0;
      timeAxis.MaximumPadding = 0;
      timeAxis.AbsoluteMinimum = 0;
      timeAxis.Minimum = 0;
      timeAxis.AbsoluteMaximum = 24 * 60 * 60;
      timeAxis.SetWindow(
        settings.GetValue("plotPanel.MinTimeWindow", 0.0f),
        settings.GetValue("plotPanel.MaxTimeWindow", 10.0f * 60));
      timeAxis.StringFormat = "h:mm";

      var units = new Dictionary<SensorType, string>();
      units.Add(SensorType.Voltage, "V");
      units.Add(SensorType.Clock, "MHz");
      units.Add(SensorType.Temperature, "°C");
      units.Add(SensorType.Load, "%");
      units.Add(SensorType.Fan, "RPM");
      units.Add(SensorType.Flow, "L/h");
      units.Add(SensorType.Control, "%");
      units.Add(SensorType.Level, "%");
      units.Add(SensorType.Factor, "1");
      units.Add(SensorType.Power, "W");
      units.Add(SensorType.Data, "GB");
      units.Add(SensorType.Throughput, "MB/s");

      foreach (SensorType type in Enum.GetValues(typeof(SensorType))) {
        var axis = CreateValueAxis(type, type.ToString(), type.ToString());
        if (units.ContainsKey(type))
          axis.Unit = units[type];
        axes.Add(axis);
        typeAxes.Add(type, axis);

        if (type == SensorType.Throughput) {
          // the throughput axis is left to the drives; the keys of the others
          // don't match a sensor type, so their zoom settings don't collide
          axis.Title = "I/O";
          networkAxis = CreateValueAxis(type, "Network", "NetworkThroughput");
          networkAxis.Unit = axis.Unit;
          axes.Add(networkAxis);
          pcieAxis = CreateValueAxis(type, "PCIe", "PcieThroughput");
          pcieAxis.Unit = axis.Unit;
          axes.Add(pcieAxis);
        }
      }

      var model = new StackedPlotModel(BeforeRender);
      model.Axes.Add(timeAxis);
      foreach (var axis in axes)
        model.Axes.Add(axis);
      model.PlotMargins = new OxyThickness(0);
      model.IsLegendVisible = false;

      return model;
    }

    private ViewLinearAxis CreateValueAxis(SensorType type, string title,
      string key) {
      var axis = new ViewLinearAxis();
      axis.SensorType = type;
      axis.Position = AxisPosition.Left;
      axis.MajorGridlineStyle = LineStyle.Solid;
      axis.MajorGridlineThickness = 1;
      axis.MajorGridlineColor = timeAxis.MajorGridlineColor;
      axis.MinorGridlineStyle = LineStyle.Solid;
      axis.MinorGridlineThickness = 1;
      axis.MinorGridlineColor = timeAxis.MinorGridlineColor;
      axis.AxislineStyle = LineStyle.Solid;
      axis.Title = title;
      axis.Key = key;
      axis.LabelFormatter = FormatTickLabel;

      axis.Zoom(
        settings.GetValue("plotPanel.Min" + axis.Key, float.NaN),
        settings.GetValue("plotPanel.Max" + axis.Key, float.NaN));
      return axis;
    }

    private ViewLinearAxis AxisOf(ISensor sensor) {
      if (sensor.SensorType == SensorType.Throughput) {
        switch (sensor.Hardware.HardwareType) {
          case HardwareType.Network:
            return networkAxis;
          case HardwareType.GpuNvidia:
          case HardwareType.GpuAmd:
          case HardwareType.GpuIntel:
            return pcieAxis;
        }
      }
      return typeAxes[sensor.SensorType];
    }

    // Appearance of the plot line of a sensor
    // Dash patterns of the plot lines: OxyPlot's line styles of the same
    // names, plus our own. Stored by name in the settings.
    public enum LinePattern {
      Solid, Dash, Dot, DenseDot, DashDot, DashDotDot, DashDashDot,
      DashDashDotDot, LongDash, LongDashDot, LongDashDotDot
    }

    // How the values of a sensor are drawn. Bar: each value is held back to
    // the previous point (it averages the updates since then), as a step
    // line; filled, it looks like touching bars. Stored by name.
    public enum LineDisplay {
      Line, Bar
    }

    public sealed class PlotLine {
      public LineDisplay Display { get; set; } = LineDisplay.Line;
      public Color Color { get; set; }
      public float Width { get; set; } = 1;
      public LinePattern Style { get; set; } = LinePattern.Solid;
      // in percent, 0 for no fill
      public int FillOpacity { get; set; }
      // seconds the line averages over: 4 for the stored averages of 4
      // updates, less for the updates of the last hour (1 for none). The
      // bars show every update.
      public int Averaging { get; set; } = 4;
    }

    // Line widths below this one get the dash pattern of this width
    private const double MinDashScaleWidth = 3;

    // GDI+ draws antialiased lines up to 1.5 px wide with the ink of a 1 px
    // line, so these widths all looked the same. Thinner lines are emulated
    // with transparency, like antialiasing does: a 1 px line under 1 px, a
    // 1.75 px line (the first width drawn as is) between 1 and 1.75 px.
    public static void LinePen(double width, out double penWidth,
      out double alpha) {
      if (width >= 1.75) {
        penWidth = width;
        alpha = 1;
      } else if (width <= 1) {
        penWidth = 1;
        alpha = Math.Max(0, width);
      } else {
        penWidth = 1.75;
        alpha = width / 1.75;
      }
    }

    // Dash pattern of the style for a line of the given width, as given to
    // the renderers: in multiples of the pen width (never less than 1 for
    // GDI+). OxyPlot's patterns scale with the width, too short to be seen on
    // thin lines: scale them as if the line was at least MinDashScaleWidth
    // wide. Null for a solid line.
    public static double[] DashPattern(LinePattern pattern, double width) {
      // dots as long as the pen is wide, separated by as much: not scaled
      if (pattern == LinePattern.DenseDot)
        return new[] { 1.0, 1.0 };

      double[] dashes = ((LineStyle)Enum.Parse(typeof(LineStyle),
        pattern.ToString())).GetDashArray();
      if (dashes == null)
        return null;
      LinePen(width, out double penWidth, out double alpha);
      double scale = Math.Max(width, MinDashScaleWidth) / Math.Max(penWidth, 1);
      return dashes.Select(d => d * scale).ToArray();
    }

    public void SetSensors(List<ISensor> sensors,
      IDictionary<ISensor, PlotLine> lines) {
      this.model.Series.Clear();

      HashSet<ViewLinearAxis> usedAxes = new HashSet<ViewLinearAxis>();

      foreach (ISensor sensor in sensors) {
        PlotLine line = lines[sensor];
        var series = new FilledLineSeries();
        series.IsBar = line.Display == LineDisplay.Bar;
        IEnumerable<SensorValue> history = series.IsBar ?
          History(sensor, 1) : History(sensor, line.Averaging);
        IEnumerable<PlotValue> values;
        if (sensor.SensorType == SensorType.Temperature) {
          values = history.Select(value => new PlotValue(
            value.Time,
            unitManager.TemperatureUnit == TemperatureUnit.Celsius ?
              value.Value : UnitManager.CelsiusToFahrenheit(value.Value).Value
          ));
        } else {
          values = history.Select(value => new PlotValue(
            value.Time, value.Value));
        }
        if (series.IsBar) {
          series.Values = Steps(values);
          // OxyPlot skips the points closer than MinimumSegmentLength (2 px)
          // to the last drawn one: skipping a corner would slant a step
          series.MinimumSegmentLength = 0;
        } else {
          series.Values = values;
        }
        series.Mapping = item => {
          var value = (PlotValue)item;
          return new DataPoint((now - value.PlotTime).TotalSeconds, value.Value);
        };
        OxyColor color = line.Color.ToOxyColor();
        LinePen(line.Width, out double penWidth, out double alpha);
        series.Color = OxyColor.FromAColor(
          (byte)Math.Round(color.A * alpha), color);
        series.StrokeThickness = penWidth;
        // the dashes set the pattern
        series.LineStyle = LineStyle.Solid;
        series.Dashes = DashPattern(line.Style, line.Width);
        if (line.FillOpacity > 0)
          series.Fill = OxyColor.FromAColor(
            (byte)Math.Round(255 * Math.Min(line.FillOpacity, 100) / 100.0),
            color);
        var axis = AxisOf(sensor);
        series.YAxisKey = axis.Key;
        series.Title = sensor.Hardware.Name + " - " + sensor.Name;
        this.model.Series.Add(series);

        usedAxes.Add(axis);
      }

      foreach (var axis in axes)
        axis.IsAxisVisible = usedAxes.Contains(axis);

      UpdateAxesPosition();
      InvalidatePlot();
    }

    // Values of a sensor as plotted: the stored averages of 4 updates, or
    // for less averaging, the updates of the last hour averaged over that
    // many seconds (the stored averages before). Lazy, like the history.
    private static IEnumerable<SensorValue> History(ISensor sensor,
      int averaging) {
      if (averaging >= 4) {
        foreach (SensorValue value in sensor.Values)
          yield return value;
        yield break;
      }

      List<SensorValue> recent = sensor.RecentValues.ToList();
      DateTime start = recent.Count > 0 ? recent[0].Time : DateTime.MaxValue;
      foreach (SensorValue value in sensor.Values) {
        if (value.Time >= start)
          break;
        yield return value;
      }
      IEnumerable<SensorValue> detail = averaging > 1 ?
        MovingAverage(recent, TimeSpan.FromSeconds(averaging)) : recent;
      foreach (SensorValue value in detail)
        yield return value;
    }

    // Average of the values over the window before each point. Each value
    // holds from the previous point to its own one, as it is measured since
    // the previous update; a run of identical values only has its first and
    // last points.
    private static IEnumerable<SensorValue> MovingAverage(
      IEnumerable<SensorValue> values, TimeSpan window) {
      List<(DateTime Start, DateTime End, double Value)> held =
        new List<(DateTime Start, DateTime End, double Value)>();
      SensorValue? previous = null;
      foreach (SensorValue value in values) {
        if (double.IsNaN(value.Value) || !previous.HasValue ||
          double.IsNaN(previous.Value.Value)) {
          // nothing before it to average with
          held.Clear();
          previous = value;
          yield return value;
          continue;
        }

        DateTime start = previous.Value.Time;
        held.Add((start, value.Time, value.Value));
        previous = value;
        // a long run: the average reaches its value once the window is in it
        if (value.Time - start > window)
          yield return new SensorValue(value.Value, start + window);
        yield return new SensorValue(Average(held, value.Time, window),
          value.Time);

        while (held.Count > 0 && held[0].End <= value.Time - window)
          held.RemoveAt(0);
      }
    }

    // Time weighted average of the held values over the window before time
    private static double Average(
      List<(DateTime Start, DateTime End, double Value)> held, DateTime time,
      TimeSpan window) {
      DateTime from = time - window;
      double sum = 0;
      double duration = 0;
      foreach ((DateTime Start, DateTime End, double Value) value in held) {
        DateTime start = value.Start > from ? value.Start : from;
        DateTime end = value.End < time ? value.End : time;
        double seconds = (end - start).TotalSeconds;
        if (seconds > 0) {
          sum += seconds * value.Value;
          duration += seconds;
        }
      }
      return duration > 0 ? sum / duration : held[held.Count - 1].Value;
    }

    // Values as a step line: each value is held from the previous point to
    // its own one. The corner point keeps the time of the value for the
    // tooltip. Lazy, like the history it reads, so it follows the updates.
    private static IEnumerable<PlotValue> Steps(IEnumerable<PlotValue> values) {
      PlotValue previous = null;
      foreach (PlotValue value in values) {
        if (previous != null && !double.IsNaN(previous.Value) &&
          !double.IsNaN(value.Value) && previous.Value != value.Value)
          yield return new PlotValue(value.UtcTime, value.Value,
            previous.UtcTime);
        yield return value;
        previous = value;
      }
    }

    private void UpdateAxesPosition() {
      model.IsStacked = stackedAxes.Value;
      if (stackedAxes.Value) {
        // the panel frames drawn by StackedPlotModel replace the axis
        // lines and the plot area border
        model.PlotAreaBorderThickness = new OxyThickness(0);
        foreach (var axis in axes) {
          axis.PositionTier = 0;
          axis.AxislineStyle = LineStyle.None;
          axis.MajorGridlineStyle = LineStyle.Solid;
          axis.MinorGridlineStyle = LineStyle.Solid;
        }
        LayoutStackedAxes(0);
      } else {
        model.PlotAreaBorderThickness = new OxyThickness(1);
        var tier = 0;
        foreach (var axis in Enumerable.Reverse(axes)) {
          if (axis.IsAxisVisible) {
            axis.StartPosition = 0;
            axis.EndPosition = 1;
            axis.PositionTier = tier;
            tier++;
          } else {
            axis.StartPosition = 0;
            axis.EndPosition = 0;
            axis.PositionTier = 0;
          }
          axis.AxislineStyle = LineStyle.Solid;
          axis.MajorGridlineStyle = LineStyle.None;
          axis.MinorGridlineStyle = LineStyle.None;
        }
      }

    }

    // The plot area starts after the widest tick label, so pad the labels to
    // 3 digits with figure spaces (as wide as a digit): the plot doesn't
    // shift when a "100" appears among 2 digit labels.
    private static string FormatTickLabel(double value) {
      var label = value.ToString("g6", CultureInfo.CurrentCulture);
      return label.Length < 3 ? new string(' ', 3 - label.Length) + label :
        label;
    }

    private void BeforeRender(double plotHeight) {
      if (stackedAxes.Value)
        LayoutStackedAxes(StackGap / plotHeight);
      UpdateValueLimits(plotHeight);
    }

    // Value axes can't be panned or zoomed below 0, nor above 100 for
    // percentages, except for a margin of a few pixels. The automatic range
    // (also after "reset axes") keeps the same margin around the values.
    private void UpdateValueLimits(double plotHeight) {
      foreach (var axis in axes) {
        if (!axis.IsAxisVisible)
          continue;
        var height = (axis.EndPosition - axis.StartPosition) * plotHeight;
        if (height <= 2 * ValueMargin)
          continue;
        var range = axis.ActualMaximum - axis.ActualMinimum;
        var margin = range > 0 && !double.IsInfinity(range) ?
          ValueMargin * range / height : 0;
        axis.AbsoluteMinimum = -margin;
        axis.AbsoluteMaximum = axis.Unit == "%" ? 100 + margin : double.MaxValue;
        // padding is a fraction of the data range, added on each side
        axis.MinimumPadding = ValueMargin / (height - 2 * ValueMargin);
        axis.MaximumPadding = axis.MinimumPadding;
      }
    }

    // Splits the plot area height between the visible axes, leaving a gap
    // (as a fraction of the plot area height) between consecutive panels.
    private void LayoutStackedAxes(double gap) {
      var count = axes.Count(axis => axis.IsAxisVisible);
      if (count == 0)
        return;
      var height = Math.Max(0, (1.0 - gap * (count - 1)) / count);
      var start = 0.0;
      foreach (var axis in Enumerable.Reverse(axes)) {
        axis.StartPosition = start;
        if (axis.IsAxisVisible) {
          axis.EndPosition = Math.Min(1, start + height);
          start = Math.Min(1, axis.EndPosition + gap);
        } else {
          axis.EndPosition = start;
        }
      }
    }

    // Called on each sensor update: the plot follows the time, unless paused
    public void UpdatePlot() {
      if (!paused)
        InvalidatePlot();
    }

    public void InvalidatePlot() {
      // paused, the x coordinates stay relative to the time of the pause; the
      // values recorded since then are after it, out of the time axis
      if (!paused)
        this.now = DateTime.UtcNow;
      model.IsPaused = paused;

      typeAxes[SensorType.Temperature].Unit =
        unitManager.TemperatureUnit == TemperatureUnit.Celsius ? "°C" : "°F";

      // set here rather than in SetSensors, so it follows the temperature unit
      foreach (var series in model.Series.OfType<LineSeries>()) {
        var axis = axes.First(a => a.Key == series.YAxisKey);
        series.TrackerFormatString = TrackerFormat(axis.SensorType, axis.Unit);
      }

      this.plot.InvalidatePlot(true);
    }

    // Tooltip: sensor name, then the value rounded like in the sensor tree,
    // with its unit, and the local time of the point. {Value} and {Time} are
    // read from the PlotValue item of the point.
    private static string TrackerFormat(SensorType type, string unit) {
      string valueFormat;
      switch (type) {
        case SensorType.Voltage:
        case SensorType.Factor:
          valueFormat = "0.000"; break;
        case SensorType.Fan:
        case SensorType.Flow:
        case SensorType.RawValue:
        case SensorType.TimeSpan:
          valueFormat = "0"; break;
        case SensorType.Throughput:
          // idle network and disks are well below 0.1 MB/s
          valueFormat = "0.00"; break;
        default:
          valueFormat = "0.0"; break;
      }
      return "{0}\n{Value:" + valueFormat + "}" +
        (string.IsNullOrEmpty(unit) || unit == "1" ? "" : " " + unit) +
        " at {Time:HH:mm:ss}";
    }

    // Line with the area down to 0 optionally filled. OxyPlot's AreaSeries
    // fills nothing once a value is NaN, like the history gaps of restarts:
    // fill each run of defined points separately.
    private class FilledLineSeries : LineSeries {
      public OxyColor Fill { get; set; } = OxyColors.Undefined;

      // The values to plot, read from the live sensor history
      public IEnumerable<PlotValue> Values { get; set; }
      // drawn as bars: the values are steps (see Steps)
      public bool IsBar { get; set; }

      // The tooltip gets the item of a point by its index in ItemsSource. A
      // lazy ItemsSource is read again then, after the history changed (the
      // oldest values expire): another value would be shown. The items are
      // copied at each data update instead, the points are made from.
      protected override void UpdateData() {
        ItemsSource = Values?.ToList();
        base.UpdateData();
      }

      // The series are rendered one after the other: the fill of a series
      // would cover the lines of the previous ones. The first series renders
      // the fills of all of them, then each series only its line.
      public override void Render(IRenderContext rc) {
        List<FilledLineSeries> series = PlotModel.Series
          .OfType<FilledLineSeries>().Where(s => s.IsVisible).ToList();
        if (series.Count > 0 && series[0] == this) {
          Graphics g = StackedPlotModel.GraphicsOf(rc);
          bool blend = ((StackedPlotModel)PlotModel).BlendBars && g != null;
          foreach (FilledLineSeries s in series) {
            if (!blend || !s.IsBar)
              s.RenderFills(rc);
          }
          if (blend)
            RenderBlendedBars(g, series.Where(s => s.IsBar && s.Fill.IsVisible()));
        }
        base.Render(rc);
      }

      private void RenderFills(IRenderContext rc) {
        OxyRect clippingRect = GetClippingRect();
        foreach (List<ScreenPoint> polygon in FillPolygons())
          rc.DrawClippedPolygon(clippingRect, polygon, 1, Fill,
            OxyColors.Undefined, 0);
      }

      // The areas between each run of defined points and 0, in screen
      // coordinates; none without a fill
      private List<List<ScreenPoint>> FillPolygons() {
        List<List<ScreenPoint>> polygons = new List<List<ScreenPoint>>();
        if (!Fill.IsVisible() || ActualPoints == null || XAxis == null ||
          YAxis == null)
          return polygons;
        double baseline = YAxis.Transform(0);
        List<ScreenPoint> run = new List<ScreenPoint>();
        foreach (DataPoint point in ActualPoints) {
          if (point.IsDefined()) {
            run.Add(Transform(point));
          } else {
            AddFillPolygon(polygons, run, baseline);
            run = new List<ScreenPoint>();
          }
        }
        AddFillPolygon(polygons, run, baseline);
        return polygons;
      }

      private static void AddFillPolygon(List<List<ScreenPoint>> polygons,
        List<ScreenPoint> run, double baseline) {
        if (run.Count < 2)
          return;
        run.Add(new ScreenPoint(run[run.Count - 1].X, baseline));
        run.Add(new ScreenPoint(run[0].X, baseline));
        polygons.Add(run);
      }

      // The filled bars of each panel, drawn per pixel column from the
      // tallest to the shortest, like the Bandwidth Monitor: above the
      // shorter bars, a bar keeps its color; where bars overlap, their colors
      // add up (see BlendStrength), with the largest fill opacity among them.
      private static void RenderBlendedBars(Graphics g,
        IEnumerable<FilledLineSeries> bars) {
        foreach (IGrouping<Axis, FilledLineSeries> panel in
          bars.GroupBy(s => s.YAxis)) {
          List<FilledLineSeries> series = panel.ToList();
          OxyRect clip = series[0].GetClippingRect();
          int left = (int)Math.Floor(clip.Left);
          int columns = (int)Math.Ceiling(clip.Right) - left;
          if (columns <= 0)
            continue;
          double[][] maxima = series
            .Select(s => s.ColumnMaxima(left, columns)).ToArray();
          Axis yAxis = series[0].YAxis;
          double baseline = Math.Max(clip.Top, Math.Min(clip.Bottom,
            yAxis.Transform(0)));

          System.Drawing.Drawing2D.GraphicsState state = g.Save();
          try {
            g.SetClip(new RectangleF((float)clip.Left, (float)clip.Top,
              (float)clip.Width, (float)clip.Height));
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            List<int> order = new List<int>();
            for (int column = 0; column < columns; column++) {
              order.Clear();
              for (int i = 0; i < series.Count; i++) {
                if (maxima[i][column] > 0)
                  order.Add(i);
              }
              order.Sort((x, y) => maxima[y][column].CompareTo(maxima[x][column]));

              (double R, double G, double B) blend = (0, 0, 0);
              int alpha = 0;
              for (int k = 0; k < order.Count; k++) {
                OxyColor color = series[order[k]].Fill;
                (double R, double G, double B) front = (color.R, color.G, color.B);
                blend = k == 0 ? front : Blend(blend, front);
                alpha = Math.Max(alpha, color.A);

                // from the top of this bar down to the next shorter one
                double top = Math.Max(clip.Top - 1,
                  yAxis.Transform(maxima[order[k]][column]));
                double bottom = k + 1 < order.Count ? Math.Max(clip.Top - 1,
                  yAxis.Transform(maxima[order[k + 1]][column])) : baseline;
                if (bottom <= top)
                  continue;
                using (SolidBrush brush = new SolidBrush(
                  System.Drawing.Color.FromArgb(alpha, (int)blend.R,
                  (int)blend.G, (int)blend.B))) {
                  g.FillRectangle(brush, left + column, (float)top, 1,
                    (float)(bottom - top));
                }
              }
            }
          } finally {
            g.Restore(state);
          }
        }
      }

      // darkest channel an overlap is kept at or below, not to wash out
      private const double BlendLightness = 160;

      // The color of an overlap: one color added to the other (see
      // BlendStrength), the way that stands out the most from the white
      // background. Adding only lightens: blue added to yellow would give a
      // pale yellow, close to white, and yellow added to blue a pale blue,
      // which is kept whichever bar is in front.
      private static (double R, double G, double B) Blend(
        (double R, double G, double B) behind,
        (double R, double G, double B) front) {
        (double R, double G, double B) Add((double R, double G, double B) a,
          (double R, double G, double B) b) {
          double strength = BlendStrength(a, b);
          return (Math.Min(255, a.R + strength * b.R),
            Math.Min(255, a.G + strength * b.G),
            Math.Min(255, a.B + strength * b.B));
        }
        (double R, double G, double B) frontAdded = Add(behind, front);
        (double R, double G, double B) behindAdded = Add(front, behind);
        return DistanceToWhite(behindAdded) > DistanceToWhite(frontAdded) ?
          behindAdded : frontAdded;
      }

      // How much of color b is added to color a: as much as possible, up to
      // all of it, while the darkest channel stays under BlendLightness, but
      // at least half. Blue and red give magenta; blue and yellow, which
      // would add up to white, a pale blue (160, 160, 255).
      private static double BlendStrength((double R, double G, double B) a,
        (double R, double G, double B) b) {
        // the strength at which each channel reaches BlendLightness: the
        // overlap stays dark enough while one of them is below it
        double Reach(double channel, double added) {
          if (added == 0)
            return channel <= BlendLightness ? double.PositiveInfinity :
              double.NegativeInfinity;
          return (BlendLightness - channel) / added;
        }
        double strength = Math.Max(Reach(a.R, b.R),
          Math.Max(Reach(a.G, b.G), Reach(a.B, b.B)));
        return Math.Max(0.5, Math.Min(1, strength));
      }

      // Distance of a color to white, each channel weighted by its share of
      // the brightness: a pale yellow is close to white, a pale blue far
      private static double DistanceToWhite((double R, double G, double B) c) {
        return Math.Sqrt(0.2126 * (255 - c.R) * (255 - c.R) +
          0.7152 * (255 - c.G) * (255 - c.G) +
          0.0722 * (255 - c.B) * (255 - c.B));
      }

      // The highest value of the bars in each pixel column from left, NaN
      // for none: the spikes narrower than a pixel are kept
      private double[] ColumnMaxima(int left, int columns) {
        double[] maxima = new double[columns];
        for (int c = 0; c < columns; c++)
          maxima[c] = double.NaN;
        IList<DataPoint> points = ActualPoints;
        if (points == null || XAxis == null)
          return maxima;
        // the points are steps: between two points at different times, the
        // value of the second one
        for (int k = 0; k + 1 < points.Count; k++) {
          DataPoint a = points[k];
          DataPoint b = points[k + 1];
          if (!a.IsDefined() || !b.IsDefined() || a.X == b.X)
            continue;
          double xa = XAxis.Transform(a.X);
          double xb = XAxis.Transform(b.X);
          int first = Math.Max(0, (int)Math.Floor(Math.Min(xa, xb)) - left);
          int last = Math.Min(columns - 1,
            (int)Math.Ceiling(Math.Max(xa, xb)) - 1 - left);
          for (int c = first; c <= last; c++) {
            if (double.IsNaN(maxima[c]) || b.Y > maxima[c])
              maxima[c] = b.Y;
          }
        }
        return maxima;
      }

      // Every mouse down hit tests the series. A series added by SetSensors
      // has no points (nor axes) until the next paint updates the model:
      // OxyPlot would throw on the null point list.
      public override TrackerHitResult GetNearestPoint(ScreenPoint point,
        bool interpolate) {
        if (ActualPoints == null || XAxis == null || YAxis == null)
          return null;

        // only the sensors of the panel under the mouse (stacked axes)
        double top = Math.Min(YAxis.ScreenMin.Y, YAxis.ScreenMax.Y);
        double bottom = Math.Max(YAxis.ScreenMin.Y, YAxis.ScreenMax.Y);
        if (point.Y < top || point.Y > bottom)
          return null;

        // OxyPlot takes the point (or interpolated point) nearest on screen,
        // which can be another time than the one under the mouse: the foot
        // of an edge below it, when the top of a bar is above the panel.
        // Take the value at the time of the mouse instead, interpolated or
        // not: for bars, the one held there (from its point back to the
        // previous one), else the point nearest in time.
        double x = XAxis.InverseTransform(point.X);
        int index = -1;
        for (int i = 0; i < ActualPoints.Count; i++) {
          DataPoint p = ActualPoints[i];
          if (!p.IsDefined())
            continue;
          if (index < 0) {
            index = i;
            continue;
          }
          double best = ActualPoints[index].X;
          // x is in seconds ago: a bar point holds its value from its x to
          // the x of the previous point. On a tie with a step corner, keep
          // the point before it, which holds the value after its x. After
          // the last point, the last value.
          if (IsBar ? (p.X <= x ? best > x || p.X > best : best > x && p.X < best) :
              Math.Abs(p.X - x) < Math.Abs(best - x))
            index = i;
        }
        if (index < 0)
          return null;

        TrackerHitResult result =
          base.GetNearestPoint(Transform(ActualPoints[index]), false);
        if (result == null)
          return null;

        // OxyPlot picks the series whose position is nearest to the mouse,
        // and drops a position more than 20 px away from it. The position is
        // at the mouse when it is between the value and 0 (over the fill),
        // else on the nearest of them, both kept in the panel: the series
        // under the mouse wins, the one drawn last when they overlap.
        double value =Math.Max(top, Math.Min(bottom, result.Position.Y));
        double baseline = Math.Max(top, Math.Min(bottom, YAxis.Transform(0)));
        result.Position = new ScreenPoint(point.X,
          Math.Max(Math.Min(value, baseline),
            Math.Min(Math.Max(value, baseline), point.Y)));
        return result;
      }

    }

    // Item behind each plotted point: the x coordinate is relative to now,
    // so the tooltip needs the original time of the value. The point is
    // drawn at PlotTime, which differs from the time of the value at the
    // corners of the steps.
    private class PlotValue {
      public PlotValue(DateTime utcTime, double value) :
        this(utcTime, value, utcTime) { }

      public PlotValue(DateTime utcTime, double value, DateTime plotTime) {
        UtcTime = utcTime;
        Value = value;
        PlotTime = plotTime;
      }

      public DateTime UtcTime { get; }
      public DateTime PlotTime { get; }
      public DateTime Time { get { return UtcTime.ToLocalTime(); } }
      public double Value { get; }
    }

    // Mouse bindings: left drag pans the time, right drag pans the values,
    // shift + left button shows the tooltip of the nearest point.
    private static PlotController CreateController() {
      var controller = new PlotController();
      controller.BindMouseDown(OxyMouseButton.Left,
        new DelegatePlotCommand<OxyMouseDownEventArgs>((view, c, args) =>
          c.AddMouseManipulator(view, new AxisPanManipulator(view, true), args)));
      controller.BindMouseDown(OxyMouseButton.Right,
        new DelegatePlotCommand<OxyMouseDownEventArgs>((view, c, args) =>
          c.AddMouseManipulator(view, new AxisPanManipulator(view, false), args)));
      controller.BindMouseDown(OxyMouseButton.Left, OxyModifierKeys.Shift,
        PlotCommands.SnapTrack);
      controller.BindMouseDown(OxyMouseButton.Middle, OxyModifierKeys.Shift,
        new DelegatePlotCommand<OxyMouseDownEventArgs>((view, c, args) =>
          c.AddMouseManipulator(view, new TimeZoomManipulator(view), args)));
      return controller;
    }

    // Shift + middle button drag: selects a time range over the whole plot
    // height and zooms the time axis to it.
    private class TimeZoomManipulator : MouseManipulator {
      private ViewTimeSpanAxis axis;
      private OxyRect plotArea;

      public TimeZoomManipulator(IPlotView plotView) : base(plotView) {
      }

      public override void Started(OxyMouseEventArgs e) {
        base.Started(e);
        AssignAxes(e.Position);
        axis = XAxis as ViewTimeSpanAxis;
        if (axis != null) {
          plotArea = PlotView.ActualModel.PlotArea;
          PlotView.SetCursorType(CursorType.ZoomHorizontal);
        }
        e.Handled = true;
      }

      public override void Delta(OxyMouseEventArgs e) {
        base.Delta(e);
        if (axis != null)
          PlotView.ShowZoomRectangle(Selection(e.Position));
        e.Handled = true;
      }

      public override void Completed(OxyMouseEventArgs e) {
        base.Completed(e);
        if (axis != null) {
          PlotView.HideZoomRectangle();
          PlotView.SetCursorType(CursorType.Default);
          OxyRect selection = Selection(e.Position);
          // ignore a click without a real drag
          if (selection.Width > 2) {
            double t0 = axis.InverseTransform(selection.Left);
            double t1 = axis.InverseTransform(selection.Right);
            axis.SetWindow(Math.Max(0, Math.Min(t0, t1)), Math.Max(t0, t1));
            PlotView.InvalidatePlot(false);
          }
        }
        e.Handled = true;
      }

      private OxyRect Selection(ScreenPoint position) {
        double x0 = Math.Max(plotArea.Left,
          Math.Min(StartPosition.X, position.X));
        double x1 = Math.Min(plotArea.Right,
          Math.Max(StartPosition.X, position.X));
        return new OxyRect(x0, plotArea.Top, Math.Max(0, x1 - x0),
          plotArea.Height);
      }
    }

    // Like OxyPlot's pan, but only along the horizontal (time) or the
    // vertical (value) axis under the cursor.
    private class AxisPanManipulator : MouseManipulator {
      private readonly bool horizontal;
      private Axis axis;
      private ScreenPoint previousPosition;

      public AxisPanManipulator(IPlotView plotView, bool horizontal)
        : base(plotView) {
        this.horizontal = horizontal;
      }

      public override void Started(OxyMouseEventArgs e) {
        base.Started(e);
        AssignAxes(e.Position);
        axis = horizontal ? XAxis : YAxis;
        previousPosition = e.Position;
        if (axis != null && axis.IsPanEnabled)
          PlotView.SetCursorType(CursorType.Pan);
        e.Handled = true;
      }

      public override void Delta(OxyMouseEventArgs e) {
        base.Delta(e);
        if (axis != null && axis.IsPanEnabled) {
          axis.Pan(previousPosition, e.Position);
          PlotView.InvalidatePlot(false);
        }
        previousPosition = e.Position;
        e.Handled = true;
      }

      public override void Completed(OxyMouseEventArgs e) {
        base.Completed(e);
        PlotView.SetCursorType(CursorType.Default);
        e.Handled = true;
      }
    }

    // OxyPlot 2 keeps the range set by Zoom() (time window menu, mouse zoom
    // and pan) in the protected ViewMinimum/ViewMaximum, not in Minimum and
    // Maximum. These axes expose it so it can be saved; NaN means automatic.
    // The mouse wheel can't zoom the time axis: the window size is set from
    // the menu, or reset to the whole range ("reset axes", middle double
    // click). It can only be panned, by the left button drag.
    private class ViewTimeSpanAxis : TimeSpanAxis {
      public ViewTimeSpanAxis() {
        IsZoomEnabled = false;
      }

      public double ZoomMinimum { get { return ViewMinimum; } }
      public double ZoomMaximum { get { return ViewMaximum; } }

      public void SetWindow(double minimum, double maximum) {
        // Zoom() is ignored while zooming is disabled
        IsZoomEnabled = true;
        Zoom(minimum, maximum);
        IsZoomEnabled = false;
      }
    }

    private class ViewLinearAxis : LinearAxis {
      public SensorType SensorType { get; set; }
      public double ZoomMinimum { get { return ViewMinimum; } }
      public double ZoomMaximum { get { return ViewMaximum; } }
    }

    // All panels share one plot area: each value axis only covers a slice of
    // it. In stacked mode this model separates the slices with white gaps and
    // frames each of them.
    private class StackedPlotModel : PlotModel {

      private readonly Action<double> beforeRender;

      // beforeRender gets the plot area height, to lay out the axes in pixels
      public StackedPlotModel(Action<double> beforeRender) {
        this.beforeRender = beforeRender;
      }

      public bool IsStacked { get; set; }
      // shows "Paused" in the top right corner of the plot area
      public bool IsPaused { get; set; }
      // overlapping bars blended per pixel column (see FilledLineSeries)
      public bool BlendBars { get; set; }

      // OxyPlot's render context draws the text with grayscale antialiasing
      // (AntiAliasGridFit); ClearType, like the rest of the window, is sharper.
      // The Graphics it draws on isn't exposed, hence the reflection.
      private static readonly FieldInfo graphicsField =
        typeof(GraphicsRenderContext).GetField("g",
          BindingFlags.Instance | BindingFlags.NonPublic);

      // the Graphics the render context draws on, null if not found
      public static Graphics GraphicsOf(IRenderContext rc) {
        return graphicsField?.GetValue(rc) as Graphics;
      }

      protected override void RenderOverride(IRenderContext rc, double width,
        double height) {
        if (graphicsField != null && graphicsField.GetValue(rc) is Graphics g)
          g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // the plot area of the previous render is a good estimate of the
        // current one, as it only depends on the size and the margins
        beforeRender(PlotArea.Height > 0 ? PlotArea.Height : height);

        base.RenderOverride(rc, width, height);

        if (IsStacked)
          RenderPanels(rc);

        if (IsPaused)
          rc.DrawText(new ScreenPoint(PlotArea.Right - 6, PlotArea.Top + 4),
            "Paused", OxyColors.Gray, DefaultFont, DefaultFontSize,
            FontWeights.Bold, 0, OxyPlot.HorizontalAlignment.Right,
            OxyPlot.VerticalAlignment.Top);
      }

      private void RenderPanels(IRenderContext rc) {
        var area = PlotArea;
        var left = Math.Round(area.Left);
        var right = Math.Round(area.Right);

        var panels = Axes
          .Where(axis => axis.IsAxisVisible &&
            axis.Position == AxisPosition.Left &&
            axis.EndPosition > axis.StartPosition)
          .Select(axis => new {
            Top = Math.Round(Math.Min(axis.ScreenMin.Y, axis.ScreenMax.Y)),
            Bottom = Math.Round(Math.Max(axis.ScreenMin.Y, axis.ScreenMax.Y))
          })
          .OrderBy(panel => panel.Top)
          .ToList();
        if (panels.Count == 0)
          return;

        rc.ResetClip();

        // blank out everything between the panels (time axis gridlines)
        var gapTop = Math.Round(area.Top);
        foreach (var panel in panels) {
          if (panel.Top > gapTop)
            rc.FillRectangle(
              new OxyRect(left, gapTop, right - left, panel.Top - gapTop),
              OxyColors.White);
          gapTop = Math.Max(gapTop, panel.Bottom);
        }
        var bottom = Math.Round(area.Bottom);
        if (bottom > gapTop)
          rc.FillRectangle(new OxyRect(left, gapTop, right - left,
            bottom - gapTop), OxyColors.White);

        // 1px frames, on pixel centers so they stay sharp
        foreach (var panel in panels) {
          var x0 = left + 0.5;
          var x1 = right - 0.5;
          var y0 = panel.Top + 0.5;
          var y1 = panel.Bottom - 0.5;
          rc.DrawLine(new[] {
              new ScreenPoint(x0, y0), new ScreenPoint(x1, y0),
              new ScreenPoint(x1, y1), new ScreenPoint(x0, y1),
              new ScreenPoint(x0, y0) },
            OxyColors.Black, 1, null, LineJoin.Miter, true);
        }
      }
    }

  }
}
