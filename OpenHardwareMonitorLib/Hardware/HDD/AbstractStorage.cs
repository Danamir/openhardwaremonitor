/*
 
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at http://mozilla.org/MPL/2.0/.
 
  Copyright (C) 2009-2015 Michael Möller <mmoeller@openhardwaremonitor.org>
	Copyright (C) 2010 Paul Werelds
  Copyright (C) 2011 Roland Reinl <roland-reinl@gmx.de>
	
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using OpenHardwareMonitorLib;

namespace OpenHardwareMonitor.Hardware.HDD {
  internal abstract class AbstractStorage : Hardware {

    // SMART and used space only every 5 updates; the performance counters
    // (throughput, active time) at every update
    private const int UPDATE_DIVIDER = 5;
    private const double BYTES_TO_GIGABYTES = 1.0 / (1024 * 1024 * 1024);
    private const double BYTES_TO_MEGABYTES = 1.0 / (1024 * 1024);

    protected string firmwareRevision;

    protected readonly int index;
    private int count;

    private DriveInfo[] driveInfos;
    private Sensor usageSensor;
    private List<Sensor> performanceSensors;
    private DrivePerformanceValues lastPerformanceValues;
    private ISmart smart;

    protected AbstractStorage(string name, string firmwareRevision,
      string id, int index, ISettings settings)
      : base(name, new Identifier(id,
        index.ToString(CultureInfo.InvariantCulture)), settings) {
      this.firmwareRevision = firmwareRevision;

      this.index = index;
      this.count = 0;

      performanceSensors = new List<Sensor>();
      lastPerformanceValues = null;

      string[] logicalDrives = WindowsStorage.GetLogicalDrives(index);
      List<DriveInfo> driveInfoList = new List<DriveInfo>(logicalDrives.Length);
      foreach (string logicalDrive in logicalDrives) {
        try {
          DriveInfo di = new DriveInfo(logicalDrive);
          if (di.TotalSize > 0)
            driveInfoList.Add(new DriveInfo(logicalDrive));
        } catch (Exception x) when (x is ArgumentException || x is IOException || x is UnauthorizedAccessException) {
          Logger.LogError(x, $"Unable to obtain drive info for {logicalDrive}");
        }
      }
      driveInfos = driveInfoList.ToArray();

      smart = new WindowsSmart(index);
    }

    protected override void Dispose(bool disposing) {
      if (disposing) {
        if (smart != null) {
          smart.Dispose();
          smart = null;
        }
      }
      base.Dispose(disposing);
    }

    public static AbstractStorage CreateInstance(int driveNumber, NVMeGeneric previousNvMe, ISettings settings) {
      StorageInfo info = WindowsStorage.GetStorageInfo(driveNumber);
      if (info == null) {
        Logging.LogInfo($"Could not retrieve storage information for drive number {driveNumber}");
        return null;
      }

      bool alsoShowRemovables;
      if (!bool.TryParse(settings.GetValue("hddMenuItemRemovable", "true"), out alsoShowRemovables)) {
        alsoShowRemovables = true;
      }

      if (info.Removable && alsoShowRemovables) {
        return null;
      }
      AbstractStorage ret = null;
      if (info.BusType == StorageBusType.BusTypeNvme) {
        ret = NVMeGeneric.CreateInstance(info, previousNvMe, settings);
      }

      // If the disk uses Nvme, but does not support the required interfaces, we try Sata instead.
      if (ret == null && (info.BusType == StorageBusType.BusTypeAta || info.BusType == StorageBusType.BusTypeSata ||
                          info.BusType == StorageBusType.BusTypeNvme)) {
        ret = ATAStorage.CreateInstance(info, settings);
      }

      if (ret == null) {
        ret = StorageGeneric.CreateInstance(info, settings);
      }

      return ret;
    }

    protected virtual void CreateSensors() {
      if (driveInfos.Length > 0) {
        usageSensor =
          new Sensor("Used Space", 0, SensorType.Load, this, _settings);
        ActivateSensor(usageSensor);
      }

      var performanceValues = smart.ReadThroughputValues();

      // Our sensor indices just need to be different from any existing sensors
      if (performanceValues != null) {
        int idx = Sensors.Length + 1;
        foreach (var (name, type) in PerformanceSensorTypes) {
          Sensor sensor = new Sensor(name, idx++, type, this, _settings);
          ActivateSensor(sensor);
          performanceSensors.Add(sensor);
        }
        lastPerformanceValues = performanceValues;
      }
    }

    // The performance sensors, in the order of ComputePerformanceValues. New
    // sensors go last: the index, part of the identifier, follows this order.
    private static readonly (string Name, SensorType Type)[] PerformanceSensorTypes = {
      ("Bytes read total", SensorType.Data),
      ("Bytes written total", SensorType.Data),
      ("Read time total", SensorType.TimeSpan),
      ("Write time total", SensorType.TimeSpan),
      ("Idle time total", SensorType.TimeSpan),
      ("Read active time", SensorType.Load),
      ("Write active time", SensorType.Load),
      ("Job queue length", SensorType.RawValue),
      ("Read throughput", SensorType.Throughput),
      ("Write throughput", SensorType.Throughput),
      ("Active time", SensorType.Load),
    };

    // The performance sensor of the given default name (the user can rename
    // the sensors), or null when the drive has no performance values
    internal Sensor GetPerformanceSensor(string defaultName) {
      int i = Array.FindIndex(PerformanceSensorTypes, t => t.Name == defaultName);
      return i >= 0 && i < performanceSensors.Count ? performanceSensors[i] : null;
    }

    // The rates and percentages are averaged since the previous values; they
    // are null when there is no previous value to compare to.
    private double?[] ComputePerformanceValues(DrivePerformanceValues current,
      DrivePerformanceValues last) {
      double seconds = last != null ?
        (current.QueryTime - last.QueryTime).TotalSeconds : 0;

      double? Rate(double delta) {
        return seconds > 0 ? delta / seconds : (double?)null;
      }

      // Read and write times add up the time of every request: with
      // concurrent requests, they can exceed the elapsed time
      double? Percent(TimeSpan delta) {
        double? rate = Rate(delta.TotalSeconds);
        return rate.HasValue ? Math.Min(100, Math.Max(0, 100 * rate.Value)) :
          (double?)null;
      }

      // Like the "active time" of the task manager: the time the drive
      // wasn't idle
      double? activeTime = null;
      if (last != null) {
        double? idle = Percent(current.IdleTime - last.IdleTime);
        activeTime = 100 - idle;
      }

      return new double?[] {
        current.BytesRead * BYTES_TO_GIGABYTES,
        current.BytesWritten * BYTES_TO_GIGABYTES,
        current.ReadTime.TotalSeconds,
        current.WriteTime.TotalSeconds,
        current.IdleTime.TotalSeconds,
        last != null ? Percent(current.ReadTime - last.ReadTime) : null,
        last != null ? Percent(current.WriteTime - last.WriteTime) : null,
        current.QueueDepth,
        last != null ?
          Rate((current.BytesRead - last.BytesRead) * BYTES_TO_MEGABYTES) : null,
        last != null ?
          Rate((current.BytesWritten - last.BytesWritten) * BYTES_TO_MEGABYTES) : null,
        activeTime,
      };
    }

    public override HardwareType HardwareType {
      get { return HardwareType.Storage; }
    }

    // SMART values, every UPDATE_DIVIDER updates
    protected virtual void UpdateSensors() {
    }

    private void UpdatePerformanceSensors() {
      if (performanceSensors.Count > 0) {
        var newValues = smart.ReadThroughputValues();
        if (newValues != null) {
          double?[] values = ComputePerformanceValues(newValues, lastPerformanceValues);
          for (int i = 0; i < performanceSensors.Count; i++)
            performanceSensors[i].Value = values[i];
          lastPerformanceValues = newValues;
        }
      }
    }

    public override void Update() {
      UpdatePerformanceSensors();

      if (count == 0) {
        UpdateSensors();

        if (usageSensor != null) {
          long totalSize = 0;
          long totalFreeSpace = 0;

          for (int i = 0; i < driveInfos.Length; i++) {
            if (!driveInfos[i].IsReady)
              continue;
            try {
              totalSize += driveInfos[i].TotalSize;
              totalFreeSpace += driveInfos[i].TotalFreeSpace;
            } catch (Exception x) when (x is IOException || x is UnauthorizedAccessException) {
              Logger.LogError($"Unable to read drive info for volume {driveInfos[i].Name}");
            }
          }
          if (totalSize > 0) {
            usageSensor.Value = 100.0f - (100.0f * totalFreeSpace) / totalSize;
          } else {
            usageSensor.Value = null;
          }
        }
      }

      count++;
      count %= UPDATE_DIVIDER;
    }

    protected abstract void GetReport(StringBuilder r);

    public override string GetReport() {
      StringBuilder r = new StringBuilder();

      r.AppendLine(this.GetType().Name);
      r.AppendLine();
      r.AppendLine("Drive name: " + _name);
      r.AppendLine("Firmware version: " + firmwareRevision);
      r.AppendLine();

      GetReport(r);

      foreach (DriveInfo di in driveInfos) {
        if (!di.IsReady)
          continue;
        try {
          r.AppendLine("Logical drive name: " + di.Name);
          r.AppendLine("Format: " + di.DriveFormat);
          r.AppendLine("Total size: " + di.TotalSize);
          r.AppendLine("Total free space: " + di.TotalFreeSpace);
          r.AppendLine();
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
      }

      return r.ToString();
    }

    public override void Traverse(IVisitor visitor) {
      foreach (ISensor sensor in Sensors)
        sensor.Accept(visitor);
    }
  }
}
