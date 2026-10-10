# Open Hardware Monitor (Danamir's fork)

A very opinionated version of the [Hexagon OSS fork](https://github.com/hexagon-oss/openhardwaremonitor) of [Open Hardware Monitor](https://openhardwaremonitor.org/), the Windows tool that shows the temperatures, fan speeds, voltages, loads, clocks and throughputs of a computer.

This fork is tuned for one person's daily use, mostly around the **sensor plot**. Its choices may not suit everyone, and it isn't meant to be merged back upstream.

## Additional features

### Plot

- **Bars or lines, per sensor.** Each plotted sensor can be shown as a line or as bars (steps that hold each value). Line and Bar keep separate width, style and fill settings; the color is shared.
- **1-second precision.** Each sensor keeps every update of the last hour, next to the 4-second averages of the whole day, and saves both across restarts. Bars show every second. Lines can average over 1 to 4 seconds (4 s by default).
- **Blended overlapping bars.** Bars in the same panel are drawn per pixel column, the tallest behind, so every top stays visible. Where bars overlap, their colors add up without washing out to white, like in the Bandwidth Monitor (blue and red give magenta, blue and yellow a pale blue).
- **Line customization.** Color (with a color picker and screen eyedropper), width from 0 to 4 px, dash pattern and fill opacity under the line, previewed live on the plot. Open it with a double click on a sensor.
- **Separate panels.** In stacked mode, each sensor type has its own framed panel. The network, GPU PCIe and drive (I/O) throughputs have one panel each, so a busy drive doesn't flatten the network plot.
- **Pause.** Freezes the plot while the sensors keep recording.
- **Mouse controls.** Left drag pans the time, right drag pans the values, the mouse wheel zooms the values, and Shift + middle drag zooms on a time range. Shift + click shows a tooltip with the value under the mouse, its unit and its time.
- **Steadier plot.** The value axes stop at 0, and at 100 for percentages. Lines are drawn over all the fills. The text uses ClearType.

### Sensors

- **All Drives.** A virtual device with the total read and write throughput of the drives, and their highest and average active time.
- **Drive active time,** like the Task Manager. The drive throughput and active time are updated every second instead of every 5 seconds.
- **Durations** are shown as `2h11m35s` instead of `0:02:11:35.0000000`.

### Application

- **Faster startup:** the window shows in about 1 s instead of about 10 s with a day of history.
- **Crash-safe history.** The settings and the sensor history are saved every 10 minutes, in the background, and the line settings as soon as the customization is confirmed. Before, they were only saved on exit, so a crash lost them all.

## Fixes

Bugs of the upstream version, fixed in this fork:

- The sensor history was saved but never restored at startup.
- The plot's time window and zoom were saved as empty values, so they were lost at each restart.
- The app hung at startup when an older version was running.
- The app froze when any device was plugged or unplugged, e.g. a game controller disconnecting: each notification reopened the drives and network adapters. It now reacts only to drives and network adapters, once per burst of notifications.
- NVIDIA: the PCIe throughput was 1024 times too high, and the GPU memory load shared its settings and history with the GPU bus load.
- The drives' read and write active times were fractions shown as percents.

## Build

Requires Windows and a .NET SDK with Roslyn 4.14 or later (SDK 10.x, or 9.0.300+).

```
build.cmd
```

or `dotnet build OpenHardwareMonitor.sln -c Release`. The output goes to `OpenHardwareMonitor/bin/<Config>/`.

The app runs as administrator. Low-level sensor access goes through the PawnIO driver (`installer/PawnIO_setup.exe`).

To build Release and install it into `C:\Program Files\OpenHardwareMonitor`, keeping the config and history there, run `install-release.ps1` from PowerShell, or `./install-release.sh` from Git Bash; it asks for elevation when needed. The monitor must not be running. Options: `-CopyDebugConfig` copies the Debug build's config over (the previous one is backed up), `-Start` launches the monitor.

## License

[Mozilla Public License 2.0](LICENSE.txt), like Open Hardware Monitor.
