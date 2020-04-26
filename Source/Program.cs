using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace BrightnessControl
{
    static class Program
    {
        private static readonly Mutex mutex = new Mutex(false, Assembly.GetExecutingAssembly().GetName().Name);

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            if (!mutex.WaitOne(0, false))
                return;

            Application.Run(new TrayIcon());
        }
    }
}
