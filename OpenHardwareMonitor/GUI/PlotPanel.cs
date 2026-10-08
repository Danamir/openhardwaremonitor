/*
 
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at http://mozilla.org/MPL/2.0/.
 
  Copyright (C) 2009-2013 Michael Möller <mmoeller@openhardwaremonitor.org>
	
*/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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

    private readonly PlotView plot;
    private readonly StackedPlotModel model;
    private readonly ViewTimeSpanAxis timeAxis = new ViewTimeSpanAxis();
    private readonly SortedDictionary<SensorType, ViewLinearAxis> axes =
      new SortedDictionary<SensorType, ViewLinearAxis>();

    private UserOption stackedAxes;
    private UserOption axisLabels;

    private DateTime now;

    public PlotPanel(PersistentSettings settings, UnitManager unitManager) {
      this.settings = settings;
      this.unitManager = unitManager;

      this.Text = "Time Plot";

      this.model = CreatePlotModel();

      this.plot = new PlotView();
      this.plot.Dock = DockStyle.Fill;
      this.plot.Model = model;
      this.plot.BackColor = Color.White;
      this.plot.ContextMenuStrip = CreateMenu();

      UpdateAxesPosition();

      this.SuspendLayout();
      this.Controls.Add(plot);
      this.ResumeLayout(true);
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
            (s, e) => { timeAxis.Zoom(0, double.NaN); InvalidatePlot(); }),
          new ToolStripMenuItem("5 min",  null,
            (s, e) => { timeAxis.Zoom(0, 5 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("10 min",  null,
            (s, e) => { timeAxis.Zoom(0, 10 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("20 min",  null,
            (s, e) => { timeAxis.Zoom(0, 20 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("30 min",  null,
            (s, e) => { timeAxis.Zoom(0, 30 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("45 min",  null,
            (s, e) => { timeAxis.Zoom(0, 45 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("1 h",  null,
            (s, e) => { timeAxis.Zoom(0, 60 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("1.5 h",  null,
            (s, e) => { timeAxis.Zoom(0, 1.5 * 60 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("2 h",  null,
            (s, e) => { timeAxis.Zoom(0, 2 * 60 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("3 h",  null,
            (s, e) => { timeAxis.Zoom(0, 3 * 60 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("6 h",  null,
            (s, e) => { timeAxis.Zoom(0, 6 * 60 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("12 h",  null,
            (s, e) => { timeAxis.Zoom(0, 12 * 60 * 60); InvalidatePlot(); }),
          new ToolStripMenuItem("24 h",  null,
            (s, e) => { timeAxis.Zoom(0, 24 * 60 * 60); InvalidatePlot(); }) };
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
      timeAxis.Zoom(
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

        axis.Zoom(
          settings.GetValue("plotPanel.Min" + axis.Key, float.NaN),
          settings.GetValue("plotPanel.Max" + axis.Key, float.NaN));

        if (units.ContainsKey(type))
          axis.Unit = units[type];
        axes.Add(type, axis);
      }

      var model = new StackedPlotModel(LayoutStackedAxes, StackGap);
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
          series.ItemsSource = sensor.Values.Select(value => new DataPoint(
            (now - value.Time).TotalSeconds,
            unitManager.TemperatureUnit == TemperatureUnit.Celsius ? 
              value.Value : UnitManager.CelsiusToFahrenheit(value.Value).Value
          ));
        } else {
          series.ItemsSource = sensor.Values.Select(value => new DataPoint(
            (now - value.Time).TotalSeconds, value.Value));
        }
        series.Color = colors[sensor].ToOxyColor();
        series.StrokeThickness = 1;
        series.YAxisKey = axes[sensor.SensorType].Key;
        series.Title = sensor.Hardware.Name + " " + sensor.Name;
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

      this.plot.InvalidatePlot(true);
    }

    // OxyPlot 2 keeps the range set by Zoom() (time window menu, mouse zoom
    // and pan) in the protected ViewMinimum/ViewMaximum, not in Minimum and
    // Maximum. These axes expose it so it can be saved; NaN means automatic.
    private class ViewTimeSpanAxis : TimeSpanAxis {
      public double ZoomMinimum { get { return ViewMinimum; } }
      public double ZoomMaximum { get { return ViewMaximum; } }
    }

    private class ViewLinearAxis : LinearAxis {
      public double ZoomMinimum { get { return ViewMinimum; } }
      public double ZoomMaximum { get { return ViewMaximum; } }
    }

    // All panels share one plot area: each value axis only covers a slice of
    // it. In stacked mode this model separates the slices with white gaps and
    // frames each of them.
    private class StackedPlotModel : PlotModel {

      private readonly Action<double> layoutAxes;
      private readonly double gapSize;

      public StackedPlotModel(Action<double> layoutAxes, double gapSize) {
        this.layoutAxes = layoutAxes;
        this.gapSize = gapSize;
      }

      public bool IsStacked { get; set; }

      protected override void RenderOverride(IRenderContext rc, double width,
        double height) {
        if (IsStacked) {
          // the plot area of the previous render is a good estimate of the
          // current one, as it only depends on the size and the margins
          var plotHeight = PlotArea.Height > 0 ? PlotArea.Height : height;
          layoutAxes(gapSize / plotHeight);
        }

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
