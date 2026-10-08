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
    private readonly SortedDictionary<SensorType, ViewLinearAxis> axes =
      new SortedDictionary<SensorType, ViewLinearAxis>();

    private UserOption stackedAxes;
    private UserOption axisLabels;

    private DateTime now;

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

      foreach (var axis in axes.Values) {
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

      foreach (SensorType type in Enum.GetValues(typeof(SensorType))) {
        var axis = new ViewLinearAxis();
        axis.Position = AxisPosition.Left;
        axis.MajorGridlineStyle = LineStyle.Solid;
        axis.MajorGridlineThickness = 1;
        axis.MajorGridlineColor = timeAxis.MajorGridlineColor;
        axis.MinorGridlineStyle = LineStyle.Solid;
        axis.MinorGridlineThickness = 1;
        axis.MinorGridlineColor = timeAxis.MinorGridlineColor;
        axis.AxislineStyle = LineStyle.Solid;
        axis.Title = type.ToString();
        axis.Key = type.ToString();
        axis.LabelFormatter = FormatTickLabel;

        axis.Zoom(
          settings.GetValue("plotPanel.Min" + axis.Key, float.NaN),
          settings.GetValue("plotPanel.Max" + axis.Key, float.NaN));

        if (units.ContainsKey(type))
          axis.Unit = units[type];
        axes.Add(type, axis);
      }

      var model = new StackedPlotModel(BeforeRender);
      model.Axes.Add(timeAxis);
      foreach (var axis in axes.Values)
        model.Axes.Add(axis);
      model.PlotMargins = new OxyThickness(0);
      model.IsLegendVisible = false;

      return model;
    }

    public void SetSensors(List<ISensor> sensors,
      IDictionary<ISensor, Color> colors) {
      this.model.Series.Clear();

      ListSet<SensorType> types = new ListSet<SensorType>();

      foreach (ISensor sensor in sensors) {
        var series = new LineSeries();
        if (sensor.SensorType == SensorType.Temperature) {
          series.ItemsSource = sensor.Values.Select(value => new PlotValue(
            value.Time,
            unitManager.TemperatureUnit == TemperatureUnit.Celsius ?
              value.Value : UnitManager.CelsiusToFahrenheit(value.Value).Value
          ));
        } else {
          series.ItemsSource = sensor.Values.Select(value => new PlotValue(
            value.Time, value.Value));
        }
        series.Mapping = item => {
          var value = (PlotValue)item;
          return new DataPoint((now - value.UtcTime).TotalSeconds, value.Value);
        };
        series.Color = colors[sensor].ToOxyColor();
        series.StrokeThickness = 1;
        series.YAxisKey = axes[sensor.SensorType].Key;
        series.Title = sensor.Hardware.Name + " - " + sensor.Name;
        this.model.Series.Add(series);

        types.Add(sensor.SensorType);
      }

      foreach (var pair in axes.Reverse()) {
        var axis = pair.Value;
        var type = pair.Key;
        axis.IsAxisVisible = types.Contains(type);
      } 

      UpdateAxesPosition();
      InvalidatePlot();
    }

    private void UpdateAxesPosition() {
      model.IsStacked = stackedAxes.Value;
      if (stackedAxes.Value) {
        // the panel frames drawn by StackedPlotModel replace the axis
        // lines and the plot area border
        model.PlotAreaBorderThickness = new OxyThickness(0);
        foreach (var axis in axes.Values) {
          axis.PositionTier = 0;
          axis.AxislineStyle = LineStyle.None;
          axis.MajorGridlineStyle = LineStyle.Solid;
          axis.MinorGridlineStyle = LineStyle.Solid;
        }
        LayoutStackedAxes(0);
      } else {
        model.PlotAreaBorderThickness = new OxyThickness(1);
        var tier = 0;
        foreach (var pair in axes.Reverse()) {
          var axis = pair.Value;
          var type = pair.Key;
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
      foreach (var axis in axes.Values) {
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
      var count = axes.Values.Count(axis => axis.IsAxisVisible);
      if (count == 0)
        return;
      var height = Math.Max(0, (1.0 - gap * (count - 1)) / count);
      var start = 0.0;
      foreach (var axis in axes.Reverse().Select(pair => pair.Value)) {
        axis.StartPosition = start;
        if (axis.IsAxisVisible) {
          axis.EndPosition = Math.Min(1, start + height);
          start = Math.Min(1, axis.EndPosition + gap);
        } else {
          axis.EndPosition = start;
        }
      }
    }

    public void InvalidatePlot() {
      this.now = DateTime.UtcNow;

      foreach (var pair in axes) {
        var axis = pair.Value;
        var type = pair.Key;
        if (type == SensorType.Temperature)
          axis.Unit = unitManager.TemperatureUnit == TemperatureUnit.Celsius ?
          "°C" : "°F";
      }

      // set here rather than in SetSensors, so it follows the temperature unit
      foreach (var series in model.Series.OfType<LineSeries>()) {
        var type = (SensorType)Enum.Parse(typeof(SensorType), series.YAxisKey);
        series.TrackerFormatString = TrackerFormat(type, axes[type].Unit);
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
        default:
          valueFormat = "0.0"; break;
      }
      return "{0}\n{Value:" + valueFormat + "}" +
        (string.IsNullOrEmpty(unit) || unit == "1" ? "" : " " + unit) +
        " at {Time:HH:mm:ss}";
    }

    // Item behind each plotted point: the x coordinate is relative to now,
    // so the tooltip needs the original time of the value.
    private class PlotValue {
      public PlotValue(DateTime utcTime, double value) {
        UtcTime = utcTime;
        Value = value;
      }

      public DateTime UtcTime { get; }
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

      // OxyPlot's render context draws the text with grayscale antialiasing
      // (AntiAliasGridFit); ClearType, like the rest of the window, is sharper.
      // The Graphics it draws on isn't exposed, hence the reflection.
      private static readonly FieldInfo graphicsField =
        typeof(GraphicsRenderContext).GetField("g",
          BindingFlags.Instance | BindingFlags.NonPublic);

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
