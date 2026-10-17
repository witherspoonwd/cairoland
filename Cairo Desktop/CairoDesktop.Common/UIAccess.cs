using CairoDesktop.Interop;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CairoDesktop.Common
{
    /// <summary>
    /// UI access (uiAccess="true" in the manifest) exempts Cairo from UIPI, so it can move and focus windows that
    /// run as administrator, and its keyboard hook keeps working while they are active. Windows grants it only to
    /// a signed exe in a secure folder (Program Files), so installed builds ship a separate copy,
    /// <see cref="ExeName"/>, that CairoDesktop.exe hands off to at startup.
    /// </summary>
    public static class UIAccess
    {
        public const string ExeName = "CairoDesktop.UIAccess.exe";

        private static bool? _isActive;

        /// <summary>True when this process has UI access.</summary>
        public static bool IsActive => _isActive ?? (_isActive = Query()).Value;

        private static bool Query()
        {
            if (!AdvApi32.OpenProcessToken(Process.GetCurrentProcess().Handle, AdvApi32.TOKEN_QUERY, out IntPtr token))
            {
                return false;
            }

            try
            {
                return AdvApi32.GetTokenInformation(token, AdvApi32.TOKEN_INFORMATION_CLASS.TokenUIAccess, out uint value, sizeof(uint), out _) && value != 0;
            }
            finally
            {
                CloseHandle(token);
            }
        }

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
