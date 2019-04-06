using System;
using System.Management;

namespace BrightnessControl.Driver
{
    class WMI : IDisposable
    {
        private readonly ManagementEventWatcher watcher;

        public event Action<int> BrightnessChanged;


        public WMI()
        {
            try
            {
                this.watcher = this.createWatcher();
                this.watcher.Start();
            }
            catch { }
        }


        /// <summary>
        /// Sets the brightness via WMI
        /// </summary>
        public void SetBrightness(int brightness)
        {
            try
            {
                using (var mclass = new ManagementClass("WmiMonitorBrightnessMethods") { Scope = new ManagementScope(@"\\.\root\wmi") })
                using (var mObjects = mclass.GetInstances())
                    foreach (ManagementObject mObj in mObjects)
                        mObj.InvokeMethod("WmiSetBrightness", new object[] { 0, brightness });
            }
            catch { }
        }


        /// <summary>
        /// Gets the current brightness value
        /// </summary>
        public int GetBrightness()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(new ManagementScope("root\\WMI"), new SelectQuery("SELECT * FROM WmiMonitorBrightness")))
                using (var mObjects = searcher.Get())
                {
                    foreach (var mObj in mObjects)
                        foreach (var prop in mObj.Properties)
                            if (prop.Name == "CurrentBrightness")
                                return this.convertBrightnessValue((int)(byte)prop.Value);
                }
            }
            catch { }
            return -1;
        }


        /// <summary>
        /// Creates the event watcher
        /// </summary>
        private ManagementEventWatcher createWatcher()
        {
            // connecting the scope
            var scope = new ManagementScope(@"\\.\root\wmi");
            scope.Connect();

            // creating the watcher
            var watcher = new ManagementEventWatcher(scope, new EventQuery("Select * From WmiMonitorBrightnessEvent"));
            watcher.EventArrived += new EventArrivedEventHandler(this.onWMIEvent);

            return watcher;
        }



        /// <summary>
        /// Handles the WMI event
        /// </summary>
        private void onWMIEvent(object sender, EventArrivedEventArgs e)
        {
            this.BrightnessChanged?.Invoke(this.convertBrightnessValue((int)(byte)e.NewEvent.Properties["Brightness"].Value));
        }


        /// <summary>
        /// Converts the brightness value to match the full range
        /// </summary>
        private int convertBrightnessValue(int b)
        {
            return (int)Math.Ceiling(100 * b / 48.0);
        }


        public void Dispose()
        {
            this.watcher?.Dispose();
        }
    }
}
