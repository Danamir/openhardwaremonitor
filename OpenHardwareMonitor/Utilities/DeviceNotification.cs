using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace OpenHardwareMonitor.Utilities {
  /// <summary>
  /// This was borrowed from https://stackoverflow.com/questions/16245706/check-for-device-change-add-remove-events
  /// </summary>
  static class DeviceNotification {
    //https://msdn.microsoft.com/en-us/library/aa363480(v=vs.85).aspx
    public const int DbtDeviceArrival = 0x8000; // system detected a new device        
    public const int DbtDeviceRemoveComplete = 0x8004; // device is gone     
    public const int DbtDevNodesChanged = 0x0007; //A device has been added to or removed from the system.

    public const int WmDevicechange = 0x0219; // device change event
    private const int DbtDevtypVolume = 2;
    private const int DbtDevtypDeviceinterface = 5;
    private static readonly Guid GuidDevinterfaceDisk = new Guid("53F56307-B6BF-11D0-94F2-00A0C91EFB8B");
    private static readonly Guid GuidDevinterfaceVolume = new Guid("53F5630D-B6BF-11D0-94F2-00A0C91EFB8B");
    private static readonly Guid GuidDevinterfaceNet = new Guid("CAC88484-7515-4C03-82E6-71A87ABAC361");
    private static readonly List<IntPtr> notificationHandles = new List<IntPtr>();

    [Flags]
    public enum DeviceKind {
      None = 0,
      Storage = 1,
      Network = 2
    }

    /// <summary>
    /// Registers a window to receive notifications when disks, volumes or
    /// network adapters are added or removed. Other devices (game controllers,
    /// HID, audio...) are ignored: each notification reopens hardware groups.
    /// </summary>
    /// <param name="windowHandle">Handle to the window receiving notifications.</param>
    public static void RegisterDeviceNotification(IntPtr windowHandle) {
      foreach (Guid classGuid in new[] { GuidDevinterfaceDisk, GuidDevinterfaceVolume, GuidDevinterfaceNet }) {
        var dbi = new DevBroadcastDeviceinterface {
          DeviceType = DbtDevtypDeviceinterface,
          Reserved = 0,
          ClassGuid = classGuid,
          Name = 0
        };

        dbi.Size = Marshal.SizeOf(dbi);
        IntPtr buffer = Marshal.AllocHGlobal(dbi.Size);
        try {
          Marshal.StructureToPtr(dbi, buffer, false);
          IntPtr handle = RegisterDeviceNotification(windowHandle, buffer, 0);
          if (handle != IntPtr.Zero)
            notificationHandles.Add(handle);
        } finally {
          Marshal.FreeHGlobal(buffer);
        }
      }
    }

    /// <summary>
    /// Unregisters the window for device notifications
    /// </summary>
    public static void UnregisterDeviceNotification() {
      foreach (IntPtr handle in notificationHandles)
        UnregisterDeviceNotification(handle);
      notificationHandles.Clear();
    }

    /// <summary>
    /// Tells which hardware a WM_DEVICECHANGE arrival or removal message
    /// concerns, from the DEV_BROADCAST_HDR its lParam points to.
    /// </summary>
    public static DeviceKind GetDeviceKind(IntPtr lParam) {
      if (lParam == IntPtr.Zero)
        return DeviceKind.None;
      int deviceType = Marshal.ReadInt32(lParam, 4);
      // Volume messages are broadcast to all top-level windows, unregistered
      if (deviceType == DbtDevtypVolume)
        return DeviceKind.Storage;
      if (deviceType != DbtDevtypDeviceinterface)
        return DeviceKind.None;
      Guid classGuid = Marshal.PtrToStructure<DevBroadcastDeviceinterface>(lParam).ClassGuid;
      if (classGuid == GuidDevinterfaceDisk || classGuid == GuidDevinterfaceVolume)
        return DeviceKind.Storage;
      if (classGuid == GuidDevinterfaceNet)
        return DeviceKind.Network;
      return DeviceKind.None;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr RegisterDeviceNotification(IntPtr recipient, IntPtr notificationFilter, int flags);

    [DllImport("user32.dll")]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct DevBroadcastDeviceinterface {
      internal int Size;
      internal int DeviceType;
      internal int Reserved;
      internal Guid ClassGuid;
      internal short Name;
    }
  }
}
