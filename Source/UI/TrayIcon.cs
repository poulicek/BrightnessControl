using System;
using System.Collections.Generic;
using System.Deployment.Application;
using System.Drawing;
using System.Windows.Forms;
using TrayToolkit.Helpers;
using TrayToolkit.OS.Display;
using TrayToolkit.OS.Input;
using TrayToolkit.UI;

namespace BrightnessControl.UI
{
    class TrayIcon : TrayIconBase
    {
        private int brightnessValue;
        private bool settingBrightness;
        private InputListener input = new InputListener();
        private DisplayController brightness;
        private readonly Dictionary<int, MenuItem> levelButtons = new Dictionary<int, MenuItem>();


        public TrayIcon() : base("Brightness Control", "https://github.com/poulicek/BrightnessControl")
        {
            this.input.MouseWheel += this.onMouseWheel;
        }

        #region UI

        protected override void OnLoad(EventArgs e)
        {
            this.brightness = new DisplayController();
            this.brightness.BrightnessChanged += this.onBrightnessChanged;
            this.brightnessValue = this.brightness.CurrentValue;

            this.input.Listen(true, false);

            base.OnLoad(e);

            // The following code doesn't work realiably. After publishing to store, the app shows the tooltip with every launch.
            //
            //            var displayToolTip = ApplicationDeployment.IsNetworkDeployed && ApplicationDeployment.CurrentDeployment.IsFirstRun;
            //#if DEBUG
            //            displayToolTip = true;
            //#endif
            //
            //            if (displayToolTip)
            //            {
            //                BalloonTooltip.Show("Brightness Control",
            //                    ResourceHelper.GetResourceImage("Resources.Icon.png"),
            //                    $"Use mouse wheel over the app icon to adjust screen brightness.{Environment.NewLine}" +
            //                    $"(Make sure your monitor has DDC/CI enabled.)");
            //            }
        }

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                this.brightness.Dispose();
                this.input.Dispose();
            }

            base.Dispose(isDisposing);
        }

        protected override string getIconName(bool lightMode)
        {
            return lightMode
                ? "Resources.IconLight.png"
                : "Resources.IconDark.png";
        }


        protected override Icon getIconFromBitmap(Bitmap bmp)
        {
            return base.getIconFromBitmap(this.adjustIconLook(bmp, this.brightnessValue / 100f));
        }


        /// <summary>
        /// Makes the bitmap partly transparent by given percantage
        /// </summary>
        private Bitmap adjustIconLook(Bitmap src, float percentage = 1)
        {
            if (percentage > 1)
                percentage = 1;

            var shapeSize = 57;
            var shapeRect = new Rectangle((src.Width - shapeSize) / 2, (src.Height - shapeSize) / 2, shapeSize, shapeSize);

            using (var g = Graphics.FromImage(src))
            using (var b = new SolidBrush(src.GetPixel(src.Width / 2, 2)))
            using (var p = new Pen(b.Color, 5))
            {
                g.SetHighQuality();
                g.DrawEllipse(p, shapeRect);
                shapeRect.Inflate(-10, -10);
                g.SetClip(new Rectangle(shapeRect.Left, 0, (int)(percentage * shapeRect.Width), src.Height));
                g.FillEllipse(b, shapeRect);
            }

            return src;
        }


        /// <summary>
        /// Sets the tooltip
        /// </summary>
        protected override void updateLook()
        {
            this.brightnessValue = this.brightness?.CurrentValue ?? -1;

            foreach (var btn in this.levelButtons)
                btn.Value.Checked = btn.Key == this.brightnessValue;

            if (this.brightnessValue >= 0)
                this.setTitle($"Brightness Control - {brightnessValue}%");

            base.updateLook();
        }


        /// <summary>
        /// Creates the context menu
        /// </summary>
        protected override List<MenuItem> getContextMenuItems()
        {
            this.loadBrightnessLevels();

            var items = new List<MenuItem>(this.levelButtons.Values);
            items.Add(new MenuItem("-"));
            items.Add(new MenuItem("Turn off screen", this.onScreenTurnOffClick));
            items.Add(new MenuItem("-"));
            items.AddRange(base.getContextMenuItems(false));

            return items;
        }


        /// <summary>
        /// Loads the brightness levels according to the capabilities
        /// </summary>
        private void loadBrightnessLevels()
        {
            this.levelButtons.Clear();

            var value = this.brightness.CurrentValue;
            var levels = this.brightness.GetBrightnessLevels();
            for (int i = levels.Length - 1; i >= 0; i--)
            {
                var level = levels[i];
                var btn = new MenuItem(levels[i] + "%", onBrightnessLevel);
                btn.Checked = level == value;

                this.levelButtons[level] = btn;
            }
        }

        #endregion

        #region Actions

        /// <summary>
        /// Setting the brightness level
        /// </summary>
        private void setBrightness(int level)
        {
            try
            {
                if (settingBrightness)
                    return;

                this.settingBrightness = true;
                lock (this.brightness)
                {
                    this.brightness.SetBrightness(level);
                    this.updateLook();
                }
            }
            finally
            {
                this.settingBrightness = false;
            }
        }


        /// <summary>
        /// Switches the brightness
        /// </summary>
            private void switchBrightness(bool forward, bool cycle = false)
        {
            var value = this.brightness.CurrentValue;
            var levels = this.brightness.GetBrightnessLevels();


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
                    this.setBrightness(value);

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
                    this.setBrightness(value);
            }
        }


        #endregion

        #region Event Handlers

        protected override void onTrayIconClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                this.switchBrightness((Control.ModifierKeys & Keys.Shift) != Keys.Shift);
            else if (e.Button == MouseButtons.Middle)
                this.switchBrightness(false);
        }

        private void onBrightnessChanged(int brightness)
        {
            this.updateLook();
        }

        private void onScreenTurnOffClick(object sender, EventArgs e)
        {
            this.brightness.TurnOff();
        }

        private void onBrightnessLevel(object sender, EventArgs e)
        {
            var btn = (sender as MenuItem);
            short level;

            if (short.TryParse(btn.Text.TrimEnd('%'), out level))
                this.setBrightness(level);
        }

        private void onMouseWheel(Point mousePosition, int val)
        {
            if (!IconBounds.Contains(mousePosition))
                return;

            ThreadingHelper.DoAsync(() =>
            {
                this.setBrightness(this.brightness.CurrentValue + Math.Sign(val) * 10);
            });
        }

#endregion
    }
}
