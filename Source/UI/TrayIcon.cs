using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using BrightnessControl.Driver;
using Common;

namespace BrightnessControl.UI
{
    class TrayIcon : TrayIconBase
    {
        private int brightnessValue;
        private Brightness brightness;
        private readonly Dictionary<int, MenuItem> levelButtons = new Dictionary<int, MenuItem>();


        public TrayIcon() : base("Brightness Control", "https://github.com/poulicek/BrightnessControl")
        {
        }

        #region UI

        protected override void OnLoad(EventArgs e)
        {
            this.brightness = new Brightness();
            this.brightness.BrightnessChanged += this.onBrightnessChanged;
            this.brightnessValue = this.brightness.CurrentValue;

            base.OnLoad(e);
        }

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
                this.brightness.Dispose();

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
            return base.getIconFromBitmap(this.makeBitmapPartlyTransparent(bmp, this.brightnessValue / 100f));
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
        /// Sets the tooltip
        /// </summary>
        protected override void updateLook()
        {
            this.brightnessValue = this.brightness.CurrentValue;

            foreach (var btn in this.levelButtons)
                btn.Value.Checked = btn.Key == this.brightnessValue;

            if (this.brightnessValue >= 0)
            {
                this.trayIcon.Text = brightness + "% - Brightness Control";
                base.updateLook();
            }
        }


        /// <summary>
        /// Creates the context menu
        /// </summary>
        protected override List<MenuItem> getMenuItems()
        {
            this.loadBrightnessLevels();

            var items = new List<MenuItem>(this.levelButtons.Values);
            items.Add(new MenuItem("-"));
            items.Add(new MenuItem("Turn off screen", this.onScreenTurnOffClick));
            items.Add(new MenuItem("-"));
            items.AddRange(base.getMenuItems());

            return items;
        }


        /// <summary>
        /// Loads the brightness levels according to the capabilities
        /// </summary>
        private void loadBrightnessLevels()
        {
            this.levelButtons.Clear();

            var value = this.brightness.CurrentValue;
            var levels = this.brightness.GetDefaultLevels();
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
            this.brightness.SetBrightness(level);
            this.updateLook();
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

        #endregion
    }
}
