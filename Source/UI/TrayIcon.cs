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
        private const int ICON_BOUNDS_TTL = 1000;
        private const int HOVER_REFRESH_INTERVAL = 3000;

        private int brightnessValue;
        private InputListener input = new InputListener();
        private DisplayController brightness;
        private readonly Dictionary<int, MenuItem> levelButtons = new Dictionary<int, MenuItem>();

        // the brightness is written by a single worker which always takes the latest requested level
        private readonly object applySync = new object();
        private int? pendingLevel;
        private int? writingLevel;
        private bool isApplying;

        private int lastRefreshTick;
        private int iconBoundsTick;
        private bool iconBoundsKnown;
        private Rectangle iconBoundsCache;


        public TrayIcon() : base("Brightness Control", "https://github.com/poulicek/BrightnessControl")
        {

        }

        #region UI

        protected override void OnLoad(EventArgs e)
        {
            this.brightness = new DisplayController();
            this.brightness.BrightnessChanged += this.onBrightnessChanged;
            this.brightnessValue = this.brightness.CurrentValue;

            this.input.MouseWheel += this.onMouseWheel;

            this.bindKeyboardShortcuts();

            base.OnLoad(e);

            // the value is cached, so it gets refreshed when the user shows interest in it
            this.trayIcon.MouseMove += this.onTrayIconHover;
            this.trayIcon.ContextMenu.Popup += this.onContextMenuPopup;

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
                this.brightness?.Dispose();
                this.input.Dispose();
            }

            base.Dispose(isDisposing);
        }


        /// <summary>
        /// Binds the keyboard keys contorlling the brightness
        /// </summary>
        private void bindKeyboardShortcuts()
        {
            if (!AppConfigHelper.Exists())
            {
                AppConfigHelper.Set("BrightnessUpKey", Keys.Control | Keys.Add);
                AppConfigHelper.Set("BrightnessDownKey", Keys.Control | Keys.Subtract);
            }

            var keyBrightnessUp = new ActionKey(AppConfigHelper.Get<Keys>("BrightnessUpKey"));
            var keyBrightnessDown = new ActionKey(AppConfigHelper.Get<Keys>("BrightnessDownKey"));

            keyBrightnessUp.Pressed += this.onKeyBrightnessUpPressed;
            keyBrightnessDown.Pressed += this.onKeyBrightnessDownPressed;

            this.input.Listen(true, true, keyBrightnessUp, keyBrightnessDown);
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
            // the brightness gets applied (and changed from the outside) on other threads
            if (this.InvokeRequired)
            {
                try { this.BeginInvoke((Action)this.updateLook); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }

            this.brightnessValue = this.brightness?.CurrentValue ?? 0;

            foreach (var btn in this.levelButtons)
                btn.Value.Checked = btn.Key == this.brightnessValue;

            if (this.brightness?.IsValueKnown == true)
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
            // the Store package starts with Windows by its manifest, so the option is offered only to the portable build
            items.AddRange(base.getContextMenuItems());

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
        /// Returns the level the brightness is heading to (including the changes not yet written to the display)
        /// </summary>
        private int getTargetBrightness()
        {
            lock (this.applySync)
                return this.pendingLevel ?? this.writingLevel ?? this.brightness.CurrentValue;
        }


        /// <summary>
        /// Requests the brightness level. The display is updated asynchronously.
        /// </summary>
        private void setBrightness(int level)
        {
            this.requestBrightness(basis => level);
        }


        /// <summary>
        /// Requests the brightness change relative to the level it is heading to
        /// </summary>
        private void changeBrightness(int delta)
        {
            // stepping from an unknown value would mean stepping from a made-up 0
            if (!this.brightness.IsValueKnown && !this.brightness.Refresh())
                return;

            this.requestBrightness(basis => basis + delta);
        }


        /// <summary>
        /// Stores the requested level and makes sure a worker is applying it
        /// </summary>
        private void requestBrightness(Func<int, int> calculateLevel)
        {
            lock (this.applySync)
            {
                var basis = this.pendingLevel ?? this.writingLevel ?? this.brightness.CurrentValue;
                this.pendingLevel = Math.Max(0, Math.Min(100, calculateLevel(basis)));

                if (this.isApplying)
                    return;

                this.isApplying = true;
            }

            ThreadingHelper.DoAsync(this.applyBrightness);
        }


        /// <summary>
        /// Writes the requested levels to the display until there is nothing left to write
        /// </summary>
        private void applyBrightness()
        {
            try
            {
                while (true)
                {
                    int level;
                    lock (this.applySync)
                    {
                        // leaving under the same lock that requests use to decide whether a worker is needed
                        if (this.pendingLevel == null)
                        {
                            this.writingLevel = null;
                            this.isApplying = false;
                            return;
                        }

                        level = this.pendingLevel.Value;
                        this.writingLevel = level;
                        this.pendingLevel = null;
                    }

                    this.brightness.SetBrightness(level);
                    this.updateLook();
                }
            }
            catch
            {
                lock (this.applySync)
                {
                    this.writingLevel = null;
                    this.isApplying = false;
                }

                throw;
            }
        }


        /// <summary>
        /// Reads the brightness from the display and updates the look if it differs
        /// </summary>
        private void refreshFromDisplay()
        {
            lock (this.applySync)
                if (this.isApplying)
                    return;

            if (this.brightness.Refresh() && this.brightness.CurrentValue != this.brightnessValue)
                this.updateLook();
        }


        /// <summary>
        /// Switches the brightness
        /// </summary>
        private void switchBrightness(bool forward, bool cycle = false)
        {
            var value = this.getTargetBrightness();
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


        /// <summary>
        /// Returns the tray icon bounds. The shell is asked at most once per interval as the call is expensive.
        /// </summary>
        private Rectangle getIconBounds()
        {
            if (!this.hasFreshIconBounds())
            {
                this.iconBoundsCache = this.IconBounds;
                this.iconBoundsTick = Environment.TickCount;
                this.iconBoundsKnown = true;
            }

            return this.iconBoundsCache;
        }


        private bool hasFreshIconBounds()
        {
            return this.iconBoundsKnown && Environment.TickCount - this.iconBoundsTick <= ICON_BOUNDS_TTL;
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

        private void onBrightnessLevel(object sender, EventArgs e)
        {
            var btn = (sender as MenuItem);
            short level;

            if (short.TryParse(btn.Text.TrimEnd('%'), out level))
                this.setBrightness(level);
        }

        private void onContextMenuPopup(object sender, EventArgs e)
        {
            this.refreshFromDisplay();
        }

        private void onTrayIconHover(object sender, MouseEventArgs e)
        {
            if (Environment.TickCount - this.lastRefreshTick < HOVER_REFRESH_INTERVAL)
                return;

            this.lastRefreshTick = Environment.TickCount;
            ThreadingHelper.DoAsync(this.refreshFromDisplay);
        }

        /// <summary>
        /// Called by the mouse hook for every wheel event in the system, so it must return fast.
        /// </summary>
        private void onMouseWheel(Point mousePosition, int val)
        {
            // the wheel is clearly elsewhere
            if (this.hasFreshIconBounds() && !this.iconBoundsCache.Contains(mousePosition))
                return;

            // asking the shell for the icon bounds outside of the hook
            this.BeginInvoke((Action)(() =>
            {
                if (this.getIconBounds().Contains(mousePosition))
                    ThreadingHelper.DoAsync(() => this.changeBrightness(Math.Sign(val) * 10));
            }));
        }

        private bool onKeyBrightnessUpPressed()
        {
            ThreadingHelper.DoAsync(() => this.changeBrightness(10));

            return false;
        }

        private bool onKeyBrightnessDownPressed()
        {
            ThreadingHelper.DoAsync(() => this.changeBrightness(-10));

            return false;
        }

        #endregion
    }
}
