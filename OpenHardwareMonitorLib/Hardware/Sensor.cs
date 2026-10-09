/*
 
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at http://mozilla.org/MPL/2.0/.
 
  Copyright (C) 2009-2012 Michael Möller <mmoeller@openhardwaremonitor.org>
	
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using OpenHardwareMonitor.Collections;

namespace OpenHardwareMonitor.Hardware {

  internal class Sensor : ISensor {

    private readonly string defaultName;
    private string name;
    private readonly int index;
    private readonly bool defaultHidden;
    private readonly SensorType sensorType;
    private readonly Hardware hardware;
    private readonly ReadOnlyArray<IParameter> parameters;
    private double? currentValue;
    private double? minValue;
    private double? maxValue;
    private readonly RingCollection<SensorValue> 
      values = new RingCollection<SensorValue>();
    // every update over the last RecentHistory, where values only has the
    // averages of 4 updates
    private readonly RingCollection<SensorValue>
      recentValues = new RingCollection<SensorValue>();
    private static readonly TimeSpan RecentHistory = TimeSpan.FromHours(1);
    private readonly ISettings settings;
    private IControl control;
    
    private double sum;
    private int count;
   
    public Sensor(string name, int index, SensorType sensorType,
      Hardware hardware, ISettings settings) : 
      this(name, index, sensorType, hardware, null, settings) { }

    public Sensor(string name, int index, SensorType sensorType,
      Hardware hardware, ParameterDescription[] parameterDescriptions, 
      ISettings settings) :
      this(name, index, false, sensorType, hardware,
        parameterDescriptions, settings) { }

    public Sensor(string name, int index, bool defaultHidden, 
      SensorType sensorType, Hardware hardware, 
      ParameterDescription[] parameterDescriptions, ISettings settings) 
    {           
      this.index = index;
      this.defaultHidden = defaultHidden;
      this.sensorType = sensorType;
      this.hardware = hardware;
      Parameter[] parameters = new Parameter[parameterDescriptions == null ?
        0 : parameterDescriptions.Length];
      for (int i = 0; i < parameters.Length; i++ ) 
        parameters[i] = new Parameter(parameterDescriptions[i], this, settings);
      this.parameters = parameters;

      this.settings = settings;
      this.defaultName = name; 
      this.name = settings.GetValue(
        new Identifier(Identifier, "name").ToString(), name);

      GetSensorValuesFromSettings();      

      hardware.Closing += delegate(IHardware h) {
        SetSensorValuesToSettings();
      };
    }

    private void SetSensorValuesToSettings() {
      SetValuesToSettings(values, "values");
      SetValuesToSettings(recentValues, "recentValues");
    }

    private void SetValuesToSettings(RingCollection<SensorValue> ring,
      string key) {
      string id = new Identifier(Identifier, key).ToString();
      if (ring.Count == 0) {
        // not to read an old history at the next start
        if (ring == recentValues)
          settings.Remove(id);
        return;
      }
      using (MemoryStream m = new MemoryStream()) {
        using (GZipStream c = new GZipStream(m, CompressionMode.Compress))
        using (BufferedStream b = new BufferedStream(c, 65536))
        using (BinaryWriter writer = new BinaryWriter(b)) {
          long t = 0;
          foreach (SensorValue sensorValue in ring) {
            long v = sensorValue.Time.ToBinary();
            writer.Write(v - t);
            t = v;
            writer.Write(sensorValue.Value);
          }
          writer.Flush();
        }
        settings.SetValue(id, Convert.ToBase64String(m.ToArray()));
      }
    }

    private void GetSensorValuesFromSettings() {
      DateTime now = DateTime.UtcNow;
      GetValuesFromSettings(values, "values", DateTime.MinValue);
      GetValuesFromSettings(recentValues, "recentValues", now - RecentHistory);

      // gap of the restart
      if (values.Count > 0)
        AppendValue(values, float.NaN, now);
      if (recentValues.Count > 0)
        AppendValue(recentValues, float.NaN, now);
    }

    // Reads the saved values from the given time on
    private void GetValuesFromSettings(RingCollection<SensorValue> ring,
      string key, DateTime from) {
      string name = new Identifier(Identifier, key).ToString();
      string s = settings.GetValue(name, null);

      if (s == null) {
        settings.Remove(name);
        return;
      }

      byte[] array = Convert.FromBase64String(s);
      DateTime now = DateTime.UtcNow;
      // decompress at once: reading the values one by one from the
      // GZipStream takes ~12 µs per value, over 7 s at startup for a full
      // day of history
      using (MemoryStream m = new MemoryStream(array))
      using (GZipStream c = new GZipStream(m, CompressionMode.Decompress))
      using (MemoryStream d = new MemoryStream())
      using (BinaryReader reader = new BinaryReader(d)) {
        c.CopyTo(d);
        d.Position = 0;

        // read until the end of the stream (EndOfStreamException)
        long t = 0;
        try {
          while (true) {
            t += reader.ReadInt64();
            DateTime time = DateTime.FromBinary(t);
            if (time > now)
              break;
            // must match SetValuesToSettings, which writes doubles
            double value = reader.ReadDouble();
            if (time >= from)
              AppendValue(ring, value, time);
          }
        } catch (EndOfStreamException) {
        } catch (ArgumentException) {
          // invalid date in a corrupted history: keep what was read so far
        }

      }

      // remove the value string from the settings to reduce memory usage
      settings.Remove(name);
    }

    // A run of identical values keeps only its first and last points
    private static void AppendValue(RingCollection<SensorValue> ring,
      double value, DateTime time) {
      if (ring.Count >= 2 && ring.Last.Value == value &&
        ring[ring.Count - 2].Value == value) {
        ring.Last = new SensorValue(value, time);
        return;
      }

      ring.Append(new SensorValue(value, time));
    }

    public IHardware Hardware {
      get { return hardware; }
    }

    public SensorType SensorType {
      get { return sensorType; }
    }

    public Identifier Identifier {
      get {
        return new Identifier(hardware.Identifier,
          sensorType.ToString().ToLowerInvariant(),
          index.ToString(CultureInfo.InvariantCulture));
      }
    }

    public string Name {
      get { 
        return name; 
      }
      set {
        if (!string.IsNullOrEmpty(value)) 
          name = value;          
        else 
          name = defaultName;
        settings.SetValue(new Identifier(Identifier, "name").ToString(), name);
      }
    }

    public int Index {
      get { return index; }
    }

    public bool IsDefaultHidden {
      get { return defaultHidden; }
    }

    public IReadOnlyArray<IParameter> Parameters {
      get { return parameters; }
    }

    public double? Value {
      get { 
        return currentValue; 
      }
      set {
        DateTime now = DateTime.UtcNow;
        while (values.Count > 0 && (now - values.First.Time).TotalDays > 1)
          values.Remove();
        while (recentValues.Count > 0 &&
          now - recentValues.First.Time > RecentHistory)
          recentValues.Remove();

        if (value.HasValue) {
          AppendValue(recentValues, value.Value, now);
          sum += value.Value;
          count++;
          if (count == 4) {
            AppendValue(values, sum / count, now);
            sum = 0;
            count = 0;
          }
        }

        this.currentValue = value;
        if (minValue > value || !minValue.HasValue)
          minValue = value;
        if (maxValue < value || !maxValue.HasValue)
          maxValue = value;
      }
    }

    public double? Min { get { return minValue; } }
    public double? Max { get { return maxValue; } }

    public void ResetMin() {
      minValue = null;
    }

    public void ResetMax() {
      maxValue = null;
    }

    public IEnumerable<SensorValue> Values {
      get { return values; }
    }    

    public IEnumerable<SensorValue> DetailedValues {
      get {
        // the averages until the first detailed value
        DateTime start = recentValues.Count > 0 ?
          recentValues.First.Time : DateTime.MaxValue;
        foreach (SensorValue value in values) {
          if (value.Time >= start)
            break;
          yield return value;
        }
        foreach (SensorValue value in recentValues)
          yield return value;
      }
    }

    public void Accept(IVisitor visitor) {
      if (visitor == null)
        throw new ArgumentNullException("visitor");
      visitor.VisitSensor(this);
    }

    public void Traverse(IVisitor visitor) {
      foreach (IParameter parameter in parameters)
        parameter.Accept(visitor);
    }

    public IControl Control {
      get {
        return control;
      }
      internal set {
        this.control = value;
      }
    }
  }
}
