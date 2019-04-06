using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;

namespace BrightnessControl.Driver
{
    class Monitors : IEnumerable, IDisposable
    {
        private readonly NativeStructures.PHYSICAL_MONITOR primaryMonitor;
        private readonly NativeStructures.PHYSICAL_MONITOR[] monitors;


        /// <summary>
        /// Returns the primary monitor
        /// </summary>
        public NativeStructures.PHYSICAL_MONITOR Primary { get { return this.primaryMonitor; } }



        public Monitors()
        {
            this.monitors = this.getMonitors(out this.primaryMonitor);
        }


        /// <summary>
        /// Setting up the monitors
        /// </summary>
        private NativeStructures.PHYSICAL_MONITOR[] getMonitors(out NativeStructures.PHYSICAL_MONITOR primaryMonitor)
        {
            primaryMonitor = new NativeStructures.PHYSICAL_MONITOR();
            var monitors = new List<NativeStructures.PHYSICAL_MONITOR>();

            try
            {                
                var field = typeof(Screen).GetField("hmonitor", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    // enumerating the screens
                    foreach (var screen in Screen.AllScreens)
                    {
                        var hMonitor = (IntPtr)field.GetValue(screen);

                        // getting the number of monitors
                        uint noOfMonitors = 0;
                        if (NativeCalls.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, ref noOfMonitors))
                        {
                            // loading the monitor instances
                            var screenMonitors = new NativeStructures.PHYSICAL_MONITOR[noOfMonitors];
                            NativeCalls.GetPhysicalMonitorsFromHMONITOR(hMonitor, noOfMonitors, screenMonitors);
                            monitors.AddRange(screenMonitors);

                            // setting the primary monitor
                            if (screen.Primary && screenMonitors.Length > 0)
                                primaryMonitor = screenMonitors[0];
                        }
                    }
                }
            }
            catch { }
            return monitors.ToArray();
        }


        public IEnumerator GetEnumerator()
        {
            return this.monitors.GetEnumerator();
        }


        public void Dispose()
        {
            NativeCalls.DestroyPhysicalMonitors((uint)this.monitors.Length, this.monitors);
        }
    }
}
