using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace BrightnessControl.Themes
{
    /// <summary>
    /// A Windows theme file
    /// </summary>
    class ThemeInfo
    {
        public string Path;
        public string Name;
    }


    /// <summary>
    /// Lists and applies the Windows themes (.theme files)
    /// </summary>
    static class ThemeManager
    {
        private const string UNSAVED_THEME_FILE = "Custom.theme";
        private const string THEMES_KEY = @"Software\Microsoft\Windows\CurrentVersion\Themes";

        private const string THEMES_PAGE = "ms-settings:themes";

        // the theme is applied by the Settings when the file is opened (tested: the page needs ~2.5 s to show up)
        private const int THEMES_PAGE_TIME = 2500;
        private const int MAX_APPLY_TIME = 10000;
        private const int SETTLE_TIME = 1500;

        private static readonly Dictionary<string, string> nameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);


        #region Listing

        /// <summary>
        /// Returns the themes saved by the user
        /// </summary>
        public static List<ThemeInfo> GetUserThemes()
        {
            var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Themes");
            return getThemes(folder, SearchOption.AllDirectories)
                .Where(t => !IsUnsaved(t.Path))
                .ToList();
        }


        /// <summary>
        /// Returns the themes coming with Windows
        /// </summary>
        public static List<ThemeInfo> GetWindowsThemes()
        {
            var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Resources\Themes");
            return getThemes(folder, SearchOption.TopDirectoryOnly).ToList();
        }


        /// <summary>
        /// Returns the path of the theme in use
        /// </summary>
        public static string GetCurrentThemePath()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(THEMES_KEY, false))
                    return key?.GetValue("CurrentTheme") as string;
            }
            catch { return null; }
        }


        /// <summary>
        /// Returns true if the theme file is the one holding the changes made to the current theme
        /// </summary>
        public static bool IsUnsaved(string path)
        {
            return string.Equals(System.IO.Path.GetFileName(path), UNSAVED_THEME_FILE, StringComparison.OrdinalIgnoreCase);
        }


        /// <summary>
        /// Returns true if both paths point to the same file
        /// </summary>
        public static bool IsSame(string path1, string path2)
        {
            if (string.IsNullOrEmpty(path1) || string.IsNullOrEmpty(path2))
                return false;

            try
            {
                return string.Equals(
                    System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(path1)),
                    System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(path2)),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }


        /// <summary>
        /// Returns the name of the theme to be shown to the user
        /// </summary>
        public static string GetName(string path)
        {
            try
            {
                var key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
                lock (nameCache)
                {
                    string name;
                    if (!nameCache.TryGetValue(key, out name))
                        nameCache[key] = name = readDisplayName(path);

                    return name;
                }
            }
            catch { return System.IO.Path.GetFileNameWithoutExtension(path); }
        }


        private static IEnumerable<ThemeInfo> getThemes(string folder, SearchOption searchOption)
        {
            if (!Directory.Exists(folder))
                return new List<ThemeInfo>();

            return Directory.GetFiles(folder, "*.theme", searchOption)
                .Select(p => new ThemeInfo { Path = p, Name = GetName(p) })
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase);
        }


        /// <summary>
        /// Reads the display name from the [Theme] section. It can be a reference to a resource of Windows.
        /// </summary>
        private static string readDisplayName(string path)
        {
            var fallback = System.IO.Path.GetFileNameWithoutExtension(path);
            var inThemeSection = false;

            foreach (var l in File.ReadLines(path))
            {
                var line = l.Trim();
                if (line.StartsWith("["))
                {
                    if (inThemeSection)
                        break;

                    inThemeSection = line.Equals("[Theme]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inThemeSection || !line.StartsWith("DisplayName=", StringComparison.OrdinalIgnoreCase))
                    continue;

                var name = line.Substring("DisplayName=".Length).Trim();
                if (name.StartsWith("@"))
                {
                    var sb = new StringBuilder(260);
                    name = SHLoadIndirectString(name, sb, sb.Capacity, IntPtr.Zero) == 0 ? sb.ToString() : null;
                }

                return string.IsNullOrWhiteSpace(name) ? fallback : name;
            }

            return fallback;
        }

        #endregion

        #region Applying

        /// <summary>
        /// Applies the theme and returns true when it is the current theme afterwards. Blocks for a few seconds.
        /// Windows has no silent way of applying a theme file: opening the file makes the Settings apply it, but only
        /// when they already show the themes page. So the page is opened first. The Settings window is hidden here
        /// (and closed once the theme is applied, unless the user had it open already).
        /// </summary>
        public static bool Apply(string path)
        {
            if (!File.Exists(path))
                return false;

            var knownWindows = new HashSet<IntPtr>(getSettingsWindows());
            var knownProcesses = getSettingsProcessIds();

            try
            {
                Process.Start(new ProcessStartInfo(THEMES_PAGE) { UseShellExecute = true })?.Dispose();
                waitHidingNewWindows(THEMES_PAGE_TIME, knownWindows, null);

                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
                waitHidingNewWindows(MAX_APPLY_TIME, knownWindows, () => IsSame(GetCurrentThemePath(), path));

                // letting Windows finish the job (wallpaper, colors) before the Settings are closed
                waitHidingNewWindows(SETTLE_TIME, knownWindows, null);
            }
            finally
            {
                closeNewSettingsProcesses(knownProcesses);
            }

            return IsSame(GetCurrentThemePath(), path);
        }


        /// <summary>
        /// Waits for the time (or till the condition is met), hiding the Settings window as soon as it shows up
        /// </summary>
        private static void waitHidingNewWindows(int timeoutMs, HashSet<IntPtr> knownWindows, Func<bool> until)
        {
            var start = Environment.TickCount;
            while (Environment.TickCount - start < timeoutMs)
            {
                foreach (var window in getSettingsWindows())
                    if (!knownWindows.Contains(window))
                        ShowWindow(window, SW_HIDE);

                if (until != null && until())
                    return;

                Thread.Sleep(50);
            }
        }


        /// <summary>
        /// Returns the visible frames of the modern (UWP) windows, which the Settings window is
        /// </summary>
        private static List<IntPtr> getSettingsWindows()
        {
            var windows = new List<IntPtr>();
            var className = new StringBuilder(64);

            EnumWindows((hWnd, lParam) =>
            {
                className.Clear();
                if (GetClassName(hWnd, className, className.Capacity) > 0 && className.ToString() == "ApplicationFrameWindow" && IsWindowVisible(hWnd))
                    windows.Add(hWnd);

                return true;
            }, IntPtr.Zero);

            return windows;
        }


        private static HashSet<int> getSettingsProcessIds()
        {
            var processes = Process.GetProcessesByName("SystemSettings");
            try { return new HashSet<int>(processes.Select(p => p.Id)); }
            finally { foreach (var p in processes) p.Dispose(); }
        }


        /// <summary>
        /// Closes the Settings opened by the theme (leaves the one the user had open)
        /// </summary>
        private static void closeNewSettingsProcesses(HashSet<int> knownProcesses)
        {
            foreach (var p in Process.GetProcessesByName("SystemSettings"))
            {
                try
                {
                    if (!knownProcesses.Contains(p.Id))
                        p.Kill();
                }
                catch { }
                finally { p.Dispose(); }
            }
        }

        #endregion

        #region Interop

        private const int SW_HIDE = 0;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

        #endregion
    }
}
