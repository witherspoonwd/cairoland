using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CairoDesktop.Interop
{
    /// <summary>
    /// Shell property store and shell item image interop, used to identify and draw UWP apps hosted in
    /// ApplicationFrameWindow frames.
    /// </summary>
    public class Shell32
    {
        const string Shell32_DllName = "shell32.dll";

        #region Structs and interfaces

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct PROPERTYKEY
        {
            public Guid fmtid;
            public uint pid;

            public PROPERTYKEY(Guid fmtid, uint pid)
            {
                this.fmtid = fmtid;
                this.pid = pid;
            }
        }

        /// <summary>Only the string case (VT_LPWSTR) is read.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct PROPVARIANT
        {
            public ushort vt;
            public ushort wReserved1;
            public ushort wReserved2;
            public ushort wReserved3;
            public IntPtr p;
            public IntPtr p2;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx;
            public int cy;
        }

        [Flags]
        public enum SIIGBF
        {
            RESIZETOFIT = 0x00,
            BIGGERSIZEOK = 0x01,
            ICONONLY = 0x04,
            ICONBACKGROUND = 0x80
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint cProps);
            [PreserveSig] int GetAt(uint iProp, out PROPERTYKEY pkey);
            [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
            [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
            [PreserveSig] int Commit();
        }

        [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
        }

        public const ushort VT_LPWSTR = 31;

        public static readonly PROPERTYKEY PKEY_AppUserModel_ID = new PROPERTYKEY(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

        private static readonly Guid IID_IPropertyStore = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
        private static readonly Guid IID_IShellItemImageFactory = new Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B");

        #endregion

        #region P/Invoke

        [DllImport(Shell32_DllName)]
        private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

        [DllImport(Shell32_DllName, CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PROPVARIANT pvar);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetApplicationUserModelId(IntPtr hProcess, ref uint applicationUserModelIdLength, StringBuilder applicationUserModelId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAP
        {
            public int bmType;
            public int bmWidth;
            public int bmHeight;
            public int bmWidthBytes;
            public ushort bmPlanes;
            public ushort bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [DllImport("gdi32.dll")]
        private static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out BITMAP lpvObject);

        [DllImport("gdi32.dll")]
        private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, [Out] byte[] lpvBits, ref BITMAPINFOHEADER lpbi, uint uUsage);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        /// <summary>
        /// Copies a bitmap's pixels as top-down 32bpp BGRA (premultiplied, as the shell returns them), keeping
        /// the alpha channel that Imaging.CreateBitmapSourceFromHBitmap drops. Returns null on failure.
        /// </summary>
        public static byte[] GetBitmapPixels(IntPtr hbitmap, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (GetObject(hbitmap, Marshal.SizeOf(typeof(BITMAP)), out BITMAP bitmap) == 0 || bitmap.bmWidth <= 0 || bitmap.bmHeight <= 0)
            {
                return null;
            }

            BITMAPINFOHEADER header = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                biWidth = bitmap.bmWidth,
                biHeight = -bitmap.bmHeight, // top-down
                biPlanes = 1,
                biBitCount = 32
            };

            byte[] pixels = new byte[bitmap.bmWidth * bitmap.bmHeight * 4];
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            try
            {
                if (GetDIBits(dc, hbitmap, 0, (uint)bitmap.bmHeight, pixels, ref header, 0) == 0)
                {
                    return null;
                }
            }
            finally
            {
                DeleteDC(dc);
            }

            width = bitmap.bmWidth;
            height = bitmap.bmHeight;
            return pixels;
        }

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const int S_OK = 0;

        #endregion

        /// <summary>
        /// The AppUserModelID the shell associates with a window (for a UWP frame, the hosted app's).
        /// </summary>
        public static string GetWindowAppUserModelId(IntPtr hwnd)
        {
            Guid iid = IID_IPropertyStore;
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out IPropertyStore store) != S_OK || store == null)
            {
                return null;
            }

            try
            {
                PROPERTYKEY key = PKEY_AppUserModel_ID;
                if (store.GetValue(ref key, out PROPVARIANT value) != S_OK)
                {
                    return null;
                }

                try
                {
                    return value.vt == VT_LPWSTR && value.p != IntPtr.Zero ? Marshal.PtrToStringUni(value.p) : null;
                }
                finally
                {
                    PropVariantClear(ref value);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }

        /// <summary>
        /// The AppUserModelID of a packaged process, or null for an unpackaged one.
        /// </summary>
        public static string GetProcessAppUserModelId(uint pid)
        {
            IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                uint length = 0;
                GetApplicationUserModelId(process, ref length, null);
                if (length == 0)
                {
                    return null;
                }

                StringBuilder id = new StringBuilder((int)length);
                return GetApplicationUserModelId(process, ref length, id) == S_OK ? id.ToString() : null;
            }
            finally
            {
                CloseHandle(process);
            }
        }

        /// <summary>
        /// The icon of shell:AppsFolder\&lt;aumid&gt; as an HBITMAP, which the caller deletes with
        /// <see cref="DeleteObject"/>. Returns IntPtr.Zero on failure.
        /// </summary>
        public static IntPtr GetAppIconBitmap(string appUserModelId, int size)
        {
            Guid iid = IID_IShellItemImageFactory;
            if (SHCreateItemFromParsingName(@"shell:AppsFolder\" + appUserModelId, IntPtr.Zero, ref iid, out IShellItemImageFactory factory) != S_OK || factory == null)
            {
                return IntPtr.Zero;
            }

            try
            {
                SIZE requested = new SIZE { cx = size, cy = size };
                return factory.GetImage(requested, SIIGBF.ICONONLY | SIIGBF.BIGGERSIZEOK, out IntPtr bitmap) == S_OK ? bitmap : IntPtr.Zero;
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
    }
}
