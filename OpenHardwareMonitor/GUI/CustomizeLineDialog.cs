/*

  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at http://mozilla.org/MPL/2.0/.

*/

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace OpenHardwareMonitor.GUI;

// Customization of a plot line: color, width, dash style and fill opacity.
// Changes are previewed through LineChanged; the caller restores the initial
// values when the dialog is cancelled.
internal sealed class CustomizeLineDialog : Form
{
    // width steps of the slider, in pixels
    private const float WidthStep = 0.25f;


    private readonly Button colorButton;
    private readonly TrackBar widthSlider;
    private readonly NumericUpDown widthValue;
    private readonly ComboBox styleList;
    private readonly TrackBar fillSlider;
    private readonly NumericUpDown fillValue;

    public CustomizeLineDialog(string sensorName, Color color, float width,
        PlotPanel.LinePattern pattern, int fillOpacity)
    {
        Text = "Customize Line - " + sensorName;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Font;
        Font = SystemFonts.MessageBoxFont;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(8);

        TableLayoutPanel layout = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 4,
            Dock = DockStyle.Fill
        };

        // color
        colorButton = new Button
        {
            BackColor = color,
            FlatStyle = FlatStyle.Flat,
            Width = 60,
            Height = 23
        };
        colorButton.Click += (sender, e) =>
        {
            using (ColorDialog dialog = new ColorDialog { Color = colorButton.BackColor, FullOpen = true })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    colorButton.BackColor = dialog.Color;
                    ColorChanged = true;
                    styleList.Invalidate();
                    OnLineChanged();
                }
            }
        };
        AddRow(layout, "Color", colorButton, null, null);

        // width
        widthSlider = new TrackBar
        {
            // 0 hides the line, for example to only show its fill
            Minimum = 0,
            Maximum = (int)(4f / WidthStep),
            TickFrequency = (int)(0.5f / WidthStep),
            SmallChange = 1,
            LargeChange = 2,
            Width = 220
        };
        widthValue = new NumericUpDown
        {
            Minimum = 0m,
            Maximum = 4m,
            Increment = (decimal)WidthStep,
            DecimalPlaces = 2,
            Width = 60
        };
        widthSlider.Value = Math.Max(widthSlider.Minimum,
            Math.Min(widthSlider.Maximum, (int)Math.Round(width / WidthStep)));
        widthValue.Value = Math.Max(widthValue.Minimum, Math.Min(widthValue.Maximum, (decimal)width));
        widthSlider.ValueChanged += (sender, e) =>
        {
            widthValue.Value = (decimal)(widthSlider.Value * WidthStep);
        };
        widthValue.ValueChanged += (sender, e) =>
        {
            widthSlider.Value = (int)Math.Round((float)widthValue.Value / WidthStep);
            styleList.Invalidate();
            OnLineChanged();
        };
        AddRow(layout, "Width", widthSlider, widthValue, "px");

        // dash style, each one drawn in the list
        styleList = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            Width = 220
        };
        foreach (PlotPanel.LinePattern p in Enum.GetValues(typeof(PlotPanel.LinePattern)))
            styleList.Items.Add(p);
        styleList.SelectedItem = pattern;
        styleList.DrawItem += DrawStyleItem;
        styleList.SelectedIndexChanged += (sender, e) => OnLineChanged();
        AddRow(layout, "Style", styleList, null, null);

        // fill opacity
        fillSlider = new TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            SmallChange = 1,
            LargeChange = 10,
            Value = Math.Max(0, Math.Min(100, fillOpacity)),
            Width = 220
        };
        fillValue = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 100,
            Value = fillSlider.Value,
            Width = 60
        };
        fillSlider.ValueChanged += (sender, e) =>
        {
            fillValue.Value = fillSlider.Value;
        };
        fillValue.ValueChanged += (sender, e) =>
        {
            fillSlider.Value = (int)fillValue.Value;
            OnLineChanged();
        };
        AddRow(layout, "Fill opacity", fillSlider, fillValue, "%");

        // buttons
        Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        AcceptButton = ok;
        CancelButton = cancel;
        FlowLayoutPanel buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 0)
        };
        buttons.Controls.AddRange(new Control[] { cancel, ok });
        layout.Controls.Add(buttons);
        layout.SetColumnSpan(buttons, 4);

        Controls.Add(layout);
    }

    public Color LineColor
    {
        get { return colorButton.BackColor; }
    }

    // whether a color was picked in the dialog
    public bool ColorChanged { get; private set; }

    public float LineWidth
    {
        get { return (float)widthValue.Value; }
    }

    public PlotPanel.LinePattern LinePattern
    {
        get { return (PlotPanel.LinePattern)styleList.SelectedItem; }
    }

    public int FillOpacity
    {
        get { return fillSlider.Value; }
    }

    public event EventHandler LineChanged;

    private void OnLineChanged()
    {
        LineChanged?.Invoke(this, EventArgs.Empty);
    }

    // The labels, values and units are aligned on the top of the row: the
    // sliders are much higher than their thumb, at their top
    private static void AddRow(TableLayoutPanel layout, string label,
        Control control, Control value, string unit)
    {
        layout.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Margin = new Padding(3, 6, 8, 3)
        });
        control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        layout.Controls.Add(control);
        if (value != null)
        {
            value.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            layout.Controls.Add(value);
            layout.Controls.Add(new Label
            {
                Text = unit,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Margin = new Padding(3, 6, 3, 3)
            });
        }
        else
        {
            layout.SetColumnSpan(control, 3);
        }
    }

    // draws the dash pattern of the item with the current color and width,
    // like on the plot
    private void DrawStyleItem(object sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0)
            return;

        PlotPanel.LinePattern style = (PlotPanel.LinePattern)styleList.Items[e.Index];
        PlotPanel.LinePen(LineWidth, out double penWidth, out double alpha);
        Color color = Color.FromArgb(
            (int)Math.Round(colorButton.BackColor.A * alpha), colorButton.BackColor);
        using (Pen pen = new Pen(color, (float)penWidth))
        {
            double[] dashes = PlotPanel.DashPattern(style, LineWidth);
            if (dashes != null)
                pen.DashPattern = dashes.Select(d => (float)d).ToArray();

            Graphics g = e.Graphics;
            SmoothingMode smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float y = e.Bounds.Top + e.Bounds.Height / 2f;
            g.DrawLine(pen, e.Bounds.Left + 4, y, e.Bounds.Right - 4, y);
            g.SmoothingMode = smoothing;
        }
        e.DrawFocusRectangle();
    }
}
