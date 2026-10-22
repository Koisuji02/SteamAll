// =====================================================================================
//  ExeIcon.cs
//  L'icona di un programma di Windows (un .exe), alla dimensione più grande che ha (di solito
//  256x256): per le schede degli store installati, nitida anche in grande e senza internet.
//  Funzioni di Windows, chiamate con P/Invoke:
//    PrivateExtractIcons  prende l'icona dell'exe alla dimensione chiesta (un "HICON");
//    GetIconInfo          dà la bitmap a colori dell'icona;
//    GetDIBits            copia i pixel della bitmap (blu, verde, rosso, trasparenza: 4 byte a pixel).
//  Con i pixel si crea una Bitmap di Avalonia.
// =====================================================================================

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;   // PixelFormat, AlphaFormat

namespace SteamAll.Services;

[SupportedOSPlatform("windows")]
public static class ExeIcon
{
    /// <summary>L'icona dell'exe a "size" pixel, o null se non c'è.</summary>
    public static Bitmap? Load(string exe, int size = 256)
    {
        var icons = new IntPtr[1];
        var ids = new uint[1];
        if (PrivateExtractIcons(exe, 0, size, size, icons, ids, 1, 0) == 0 || icons[0] == IntPtr.Zero) return null;
        IntPtr icon = icons[0];
        try
        {
            if (!GetIconInfo(icon, out IconInfo info)) return null;
            try
            {
                GetObject(info.Color, Marshal.SizeOf<BitmapHeader>(), out BitmapHeader bmp);
                int w = bmp.Width, h = bmp.Height;
                var header = new BitmapInfoHeader
                {
                    Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = w, Height = -h,   // altezza negativa = dall'alto in basso
                    Planes = 1, BitCount = 32, Compression = 0,
                };
                var pixels = new byte[w * h * 4];
                IntPtr dc = GetDC(IntPtr.Zero);
                try { GetDIBits(dc, info.Color, 0, (uint)h, pixels, ref header, 0); }
                finally { ReleaseDC(IntPtr.Zero, dc); }
                // GCHandle "blocca" l'array in memoria, così Avalonia può leggerlo da un indirizzo fisso.
                GCHandle pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try
                {
                    return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, pin.AddrOfPinnedObject(),
                                      new PixelSize(w, h), new Vector(96, 96), w * 4);
                }
                finally { pin.Free(); }
            }
            finally
            {
                DeleteObject(info.Color);
                DeleteObject(info.Mask);
            }
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo { public bool IsIcon; public int HotspotX, HotspotY; public IntPtr Mask, Color; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public IntPtr Bits; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public ushort Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint count, uint flags);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr obj, int size, out BitmapHeader bitmap);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapInfoHeader info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
}
