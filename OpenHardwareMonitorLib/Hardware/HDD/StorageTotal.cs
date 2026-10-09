/*

  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at http://mozilla.org/MPL/2.0/.

*/

using System.Collections.Generic;
using System.Linq;

namespace OpenHardwareMonitor.Hardware.HDD {
  // Virtual drive combining the performance sensors of all the drives, to
  // plot a global reading. It must be updated after the drives.
  internal sealed class StorageTotal : Hardware {

    private readonly IReadOnlyList<AbstractStorage> drives;
    private readonly Sensor readThroughput;
    private readonly Sensor writeThroughput;
    private readonly Sensor maxActiveTime;
    private readonly Sensor averageActiveTime;

    public StorageTotal(IReadOnlyList<AbstractStorage> drives,
      ISettings settings)
      : base("All Drives", new Identifier("storage", "total"), settings) {
      this.drives = drives;

      readThroughput = new Sensor("Read throughput", 0,
        SensorType.Throughput, this, settings);
      writeThroughput = new Sensor("Write throughput", 1,
        SensorType.Throughput, this, settings);
      maxActiveTime = new Sensor("Max active time", 0,
        SensorType.Load, this, settings);
      averageActiveTime = new Sensor("Average active time", 1,
        SensorType.Load, this, settings);

      ActivateSensor(readThroughput);
      ActivateSensor(writeThroughput);
      ActivateSensor(maxActiveTime);
      ActivateSensor(averageActiveTime);
    }

    public override HardwareType HardwareType {
      get { return HardwareType.Storage; }
    }

    public override void Update() {
      List<double> read = DriveValues("Read throughput");
      List<double> write = DriveValues("Write throughput");
      List<double> active = DriveValues("Active time");

      readThroughput.Value = read.Count > 0 ? read.Sum() : (double?)null;
      writeThroughput.Value = write.Count > 0 ? write.Sum() : (double?)null;
      maxActiveTime.Value = active.Count > 0 ? active.Max() : (double?)null;
      averageActiveTime.Value =
        active.Count > 0 ? active.Average() : (double?)null;
    }

    // current values of the given performance sensor of the drives having one
    private List<double> DriveValues(string sensorName) {
      List<double> values = new List<double>();
      foreach (AbstractStorage drive in drives) {
        Sensor sensor = drive.GetPerformanceSensor(sensorName);
        if (sensor != null && sensor.Value.HasValue)
          values.Add(sensor.Value.Value);
      }
      return values;
    }
  }
}
