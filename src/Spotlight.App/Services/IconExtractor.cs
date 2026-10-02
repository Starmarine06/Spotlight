using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Spotlight.App.Services;

/// <summary>
/// Extracts crisp 64px icons through the shell's IShellItemImageFactory (works for .exe, .lnk, folders and
/// packaged/UWP apps via shell:AppsFolder) and stores them as PNG files in the icon cache. The UI loads them
/// over a virtual host name instead of receiving megabytes of base64 in a single message.
/// </summary>
public static class IconExtractor
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, uint flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private const uint SIIGBF_BIGGERSIZEOK = 0x1;
    private const uint SIIGBF_ICONONLY = 0x4;
    private static readonly Guid FactoryGuid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    /// <summary>Returns the icon file name (inside <see cref="AppPaths.IconDir"/>) or empty if none could be produced.</summary>
    public static string GetIconFile(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return string.Empty;

        var file = Hash(target) + ".png";
        var full = Path.Combine(AppPaths.IconDir, file);
        if (File.Exists(full) && new FileInfo(full).Length > 0) return file;

        try
        {
            var parsingName = target;
            if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return string.Empty;

            var iid = FactoryGuid;
            SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var factory);
            try
            {
                var size = new SIZE { cx = 64, cy = 64 };
                int hr = factory.GetImage(size, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out var hbitmap);
                if (hr != 0 || hbitmap == IntPtr.Zero) return string.Empty;

                try
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(
                        hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(source));
                    using var fs = File.Create(full);
                    encoder.Save(fs);
                    return file;
                }
                finally
                {
                    DeleteObject(hbitmap);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>The cached icon file name for a source, or empty if it has not been extracted yet.</summary>
    public static string CachedFile(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return string.Empty;
        var file = Hash(source) + ".png";
        var full = Path.Combine(AppPaths.IconDir, file);
        return File.Exists(full) && new FileInfo(full).Length > 0 ? file : string.Empty;
    }

    public static string Hash(string value)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant()));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }
}
