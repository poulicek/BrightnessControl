using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using BrightnessControl.Driver;
using Microsoft.Win32;

namespace BrightnessControl
{
    class TrayIcon : Form
    {
        private NotifyIcon trayIcon;
        private Brightness brightness;
        private readonly Dictionary<int, MenuItem> levelButtons = new Dictionary<int, MenuItem>();


        public TrayIcon()
        {
            this.Text = "BrightnessControl";            
        }

        #region Control Design

        /// <summary>
        /// Returns the icon from the resource
        /// </summary>
        private Icon getIcon(int brightness = 100)
        {
            var darkMode = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", true)?.GetValue("SystemUsesLightTheme") as int? == 0;

            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(darkMode ? "BrightnessControl.IconDark.png" : "BrightnessControl.IconLight.png"))
            using (var bmp = this.makeBitmapPartlyTransparent(new Bitmap(s), brightness / 100f))
                return Icon.FromHandle(bmp.GetHicon());
        }


        /// <summary>
        /// Makes the bitmap partly transparent by given percantage
        /// </summary>
        private Bitmap makeBitmapPartlyTransparent(Bitmap src, float percentage = 1, int opacity = 255)
        {
            if (percentage >= 1)
                return src;

            var dst = new Bitmap(src.Width, src.Height);
            using (var g = Graphics.FromImage(dst))
            using (var ia = new ImageAttributes())
            {
                var y = (int)(src.Height * (1 - percentage));
                var rect1 = new Rectangle(0, 0, src.Width, src.Height);
                var rect2 = new Rectangle(0, y, src.Width, src.Height - y);

                ia.SetColorMatrix(new ColorMatrix() { Matrix33 = 0.6f });
                g.DrawImage(src, rect1, rect1.X, rect1.Y, rect1.Width, rect1.Height, GraphicsUnit.Pixel, ia);
                g.DrawImage(src, rect2, rect2.X, rect2.Y, rect2.Width, rect2.Height, GraphicsUnit.Pixel);
                src.Dispose();
            }

            return dst;
        }


        /// <summary>
        /// Creates a tray icon
        /// </summary>
        private NotifyIcon createTrayIcon()
        {
            var trayIcon = new NotifyIcon()
            {
                Text = "Brightness Control",
                Icon = this.getIcon(),
                ContextMenu = this.createContextMenu(),
                Visible = true
            };

            trayIcon.MouseDown += this.onTrayIconClick;
            return trayIcon;
        }


        /// <summary>
        /// Sets the tooltip
        /// </summary>
        private void updateLook(int brightness)
        {
            foreach (var btn in this.levelButtons)
                btn.Value.Checked = btn.Key == brightness;

            if (brightness >= 0)
            {
                this.trayIcon.Text = brightness + "% - Brightness Control";
                this.trayIcon.Icon = this.getIcon(brightness);
            }
        }


        /// <summary>
        /// Setting the brightness level
        /// </summary>
        private void setBrightness(int level)
        {
            this.brightness.SetBrightness(level);
            this.updateLook(level);
        }


        /// <summary>
        /// Switches the brightness
        /// </summary>
        private void switchBrightness(bool forward, bool cycle = false)
        {
            var value = this.brightness.CurrentValue;
            var levels = this.brightness.GetDefaultLevels();


            if (forward)
            {
                // looking for the next level
                for (int i = 0; i < levels.Length; i++)
                {
                    if (levels[i] > value)
                    {
                        this.setBrightness(levels[i]);
                        return;
                    }
                }

                // setting the minimum value
                if (cycle && levels.Length > 0)
                    this.setBrightness(levels[0]);
                else
                    this.setBrightness(this.brightness.CurrentValue);

            }
            else
            {
                // looking for the previous level
                for (int i = levels.Length - 1; i >= 0; i--)
                {
                    if (levels[i] < value)
                    {
                        this.setBrightness(levels[i]);
                        return;
                    }
                }

                // setting the maximum value
                if (cycle && levels.Length > 0)
                    this.setBrightness(levels[levels.Length - 1]);
                else
                    this.setBrightness(this.brightness.CurrentValue);
            }
        }


        /// <summary>
        /// Creates the context menu
        /// </summary>
        private ContextMenu createContextMenu()
        {
            var trayMenu = new ContextMenu();

            this.loadBrightnessLevels(trayMenu);

            trayMenu.MenuItems.Add("-");
            trayMenu.MenuItems.Add("Turn off screen", onTurnOff);
            trayMenu.MenuItems.Add("-");
            trayMenu.MenuItems.Add("Start with Windows", onStartUp).Checked = this.startsWithWindows();
            trayMenu.MenuItems.Add("About...", onAbout);
            trayMenu.MenuItems.Add("Exit", onMenuExit);

            return trayMenu;
        }


        /// <summary>
        /// Loads the brightness levels according to the capabilities
        /// </summary>
        private void loadBrightnessLevels(ContextMenu trayMenu)
        {
            this.levelButtons.Clear();

            var value = this.brightness.CurrentValue;
            var levels = this.brightness.GetDefaultLevels();
            for (int i = levels.Length - 1; i >= 0; i--)
            {
                var level = levels[i];
                var btn = trayMenu.MenuItems.Add(levels[i] + "%", onBrightnessLevel);
                btn.Checked = level == value;

                this.levelButtons[level] = btn;
            }
        }


        /// <summary>
        /// Hides the main window
        /// </summary>
        private void hideMainWindow()
        {
            this.Visible = false;
            this.ShowInTaskbar = false;
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Handls the tray icon click
        /// </summary>
        private void onTrayIconClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                this.switchBrightness((Control.ModifierKeys & Keys.Shift) != Keys.Shift);
            else if (e.Button == MouseButtons.Middle)
                this.switchBrightness(false);
        }


        /// <summary>
        /// Handles the change of the display
        /// </summary>
        async private void onDisplaySettingsChanged(object sender, EventArgs e)
        {
            await Task.Delay(500);
            this.updateLook(this.brightness.CurrentValue);
        }


        /// <summary>
        /// Handles the change of brightness
        /// </summary>
        private void onBrightnessChanged(int brightness)
        {
            this.updateLook(brightness);
        }

        #endregion

        #region Start-up Handling

        /// <summary>
        /// Setting the startup state
        /// </summary>
        private bool startsWithWindows()
        {
            try
            {
                return Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true).GetValue(this.Text) as string == Application.ExecutablePath.ToString();
            }
            catch { return false; }
        }


        /// <summary>
        /// Setting the startup state
        /// </summary>
        private bool setStartup(bool set)
        {
            try
            {
                var rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (set)
                    rk.SetValue(this.Text, Application.ExecutablePath.ToString());
                else
                    rk.DeleteValue(this.Text, false);

                return set;
            }
            catch { return !set; }
        }

        #endregion

        #region Menu Handlers

        private void onTurnOff(object sender, EventArgs e)
        {
            this.brightness.TurnOff();
        }

        private void onStartUp(object sender, EventArgs e)
        {
            var btn = (sender as MenuItem);
            btn.Checked = this.setStartup(!btn.Checked);
        }

        private void onAbout(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo("http://www.bitit.cz/#projects"));
        }

        private void onMenuExit(object sender, EventArgs e)
        {
            Application.Exit();
        }


        private void onBrightnessLevel(object sender, EventArgs e)
        {
            var btn = (sender as MenuItem);
            short level;

            if (short.TryParse(btn.Text.TrimEnd('%'), out level))
                this.setBrightness(level);
        }

        #endregion

        #region Overrides

        protected override void OnLoad(EventArgs e)
        {
            this.hideMainWindow();
            this.brightness = new Brightness();
            this.brightness.BrightnessChanged += onBrightnessChanged;
            this.trayIcon = this.createTrayIcon();
            this.updateLook(this.brightness.CurrentValue);

            SystemEvents.DisplaySettingsChanged += this.onDisplaySettingsChanged;
        }

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                this.brightness.Dispose();
                this.trayIcon.Icon = null;
                this.trayIcon.Dispose();
            }
            base.Dispose(isDisposing);
        }

        #endregion
    }
}
