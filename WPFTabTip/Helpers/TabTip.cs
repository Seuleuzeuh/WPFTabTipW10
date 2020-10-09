﻿using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using WPFTabTipMixedHardware.Models;

namespace WPFTabTipMixedHardware.Helpers
{
    public static class TabTip
    {
        private const string TabTipWindowClassName = "IPTip_Main_Window";
        private const string TabTipExecPath = @"C:\Program Files\Common Files\microsoft shared\ink\TabTip.exe";
        private const string TabTipRegistryKeyName = @"HKEY_CURRENT_USER\Software\Microsoft\TabletTip\1.7";
        internal const string TabTipProcessName = "TabTip";

        [DllImport("user32.dll")]
        private static extern int SendMessage(int hWnd, uint msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(String sClassName, String sAppName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(HandleRef hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public readonly int Left;        // x position of upper-left corner
            public readonly int Top;         // y position of upper-left corner
            public readonly int Right;       // x position of lower-right corner
            public readonly int Bottom;      // y position of lower-right corner
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern UInt32 GetWindowLong(IntPtr hWnd, int nIndex);

        /// <summary>
        /// Signals that TabTip was closed after it was opened 
        /// with a call to StartPoolingForTabTipClosedEvent method
        /// </summary>
        internal static event Action Closed;

        internal static event Action<Exception> ExceptionCatched;

        private static IntPtr GetTabTipWindowHandle() => FindWindow(TabTipWindowClassName, null);

        internal static void OpenUndockedAndStartPoolingForClosedEvent()
        {
            System.Diagnostics.Debug.WriteLine("TabTip.OpenUndockedAndStartPoolingForClosedEvent");
            OpenUndocked();
            StartPoolingForTabTipClosedEvent();
        }

        /// <summary>
        /// Open TabTip
        /// </summary>
        public static void Open()
        {
            if (EnvironmentEx.GetOSVersion() == OSVersion.Win10)
                EnableTabTipOpenInDesctopModeOnWin10();

            try
            {
                Process.Start(TabTipExecPath);
            }
            catch (Exception ex)
            {
                ExceptionCatched?.Invoke(ex);
            }
        }

        private static void EnableTabTipOpenInDesctopModeOnWin10()
        {
            try
            {
                const string TabTipAutoInvokeKey = "EnableDesktopModeAutoInvoke";

                int EnableDesktopModeAutoInvoke = (int)(Registry.GetValue(TabTipRegistryKeyName, TabTipAutoInvokeKey, -1) ?? -1);
                if (EnableDesktopModeAutoInvoke != 1)
                    Registry.SetValue(TabTipRegistryKeyName, TabTipAutoInvokeKey, 1);
            }
            catch (Exception ex)
            {
                ExceptionCatched?.Invoke(ex);
            }
        }

        /// <summary>
        /// Open TabTip in undocked state
        /// </summary>
        public static void OpenUndocked()
        {
            const string TabTipDockedKey = "EdgeTargetDockedState";

            try
            {
                int docked = (int)(Registry.GetValue(TabTipRegistryKeyName, TabTipDockedKey, 1) ?? 1);
                if (docked == 1)
                {
                    Registry.SetValue(TabTipRegistryKeyName, TabTipDockedKey, 0);
                    KillTapTibProcess();
                }
            }
            catch (Exception ex)
            {
                ExceptionCatched?.Invoke(ex);
            }

            Open();
        }

        internal static void KillTapTibProcess()
        {
            try
            {
                foreach (Process tabTipProcess in Process.GetProcessesByName(TabTipProcessName))
                    tabTipProcess.Kill();
            }
            catch (Exception ex)
            {
                ExceptionCatched?.Invoke(ex);
            }
        }

        /// <summary>
        /// Close TabTip
        /// </summary>
        public static void Close()
        {
            System.Diagnostics.Debug.WriteLine("TabTip.Close");
            const int WM_SYSCOMMAND = 274;
            const int SC_CLOSE = 61536;
            try
            {
                SendMessage(GetTabTipWindowHandle().ToInt32(), WM_SYSCOMMAND, SC_CLOSE, 0);
            }
            catch (Exception ex)
            {
                ExceptionCatched?.Invoke(ex);
            }
        }

        private static void StartPoolingForTabTipClosedEvent()
        {
            PoolingTimer.PoolUntilTrue(
                PoolingFunc: TabTipClosed,
                Callback: () => Closed?.Invoke(),
                dueTime: TimeSpan.FromMilliseconds(700),
                period: TimeSpan.FromMilliseconds(50));
        }

        private static bool TabTipClosed()
        {
            try
            {
                const int GWL_STYLE = -16; // Specifies we wish to retrieve window styles.
                const uint KeyboardClosedStyle = 2617245696;
                IntPtr KeyboardWnd = GetTabTipWindowHandle();
                return (KeyboardWnd.ToInt32() == 0 || GetWindowLong(KeyboardWnd, GWL_STYLE) == KeyboardClosedStyle);
            }
            catch (Exception ex)
            {
                ExceptionCatched?.Invoke(ex);
                return false;
            }
        }

        // ReSharper disable once UnusedMember.Local
        public static bool IsTabTipProcessRunning => GetTabTipWindowHandle() != IntPtr.Zero;

        /// <summary>
        /// Gets TabTip Window Rectangle
        /// </summary>
        /// <returns></returns>
        [SuppressMessage("ReSharper", "ConvertIfStatementToReturnStatement")]
        public static Rectangle GetTabTipRectangle()
        {
            if (TabTipClosed())
                return new Rectangle();

            return GetWouldBeTabTipRectangle();
        }

        private static Rectangle previousTabTipRectangle;

        /// <summary>
        /// Gets Window Rectangle which would be occupied by TabTip if TabTip was opened.
        /// </summary>
        /// <returns></returns>
        [SuppressMessage("ReSharper", "ConvertIfStatementToReturnStatement")]
        internal static Rectangle GetWouldBeTabTipRectangle()
        {
            if (!GetWindowRect(new HandleRef(null, GetTabTipWindowHandle()), out RECT rect))
            {
                if (previousTabTipRectangle.Equals(new Rectangle())) //in case TabTip was closed and previousTabTipRectangle was not set
                    Task.Delay(TimeSpan.FromSeconds(1)).ContinueWith(TryGetTabTipRectangleToСache);

                return previousTabTipRectangle;
            }

            Rectangle wouldBeTabTipRectangle = new Rectangle(x: rect.Left, y: rect.Top, width: rect.Right - rect.Left + 1, height: rect.Bottom - rect.Top + 1);
            previousTabTipRectangle = wouldBeTabTipRectangle;

            return wouldBeTabTipRectangle;
        }

        private static void TryGetTabTipRectangleToСache(Task task)
        {
            if (GetWindowRect(new HandleRef(null, GetTabTipWindowHandle()), out RECT rect))
                previousTabTipRectangle = new Rectangle(x: rect.Left, y: rect.Top, width: rect.Right - rect.Left + 1, height: rect.Bottom - rect.Top + 1);
        }
    }
}
