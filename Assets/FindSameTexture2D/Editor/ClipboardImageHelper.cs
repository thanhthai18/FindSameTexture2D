using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Zitga.FindSameTexture2D.Editor
{
    /// <summary>
    /// Đọc ảnh bitmap từ Windows Clipboard bằng Win32 API.
    /// Hỗ trợ CF_DIB / CF_DIBV5 — định dạng chuẩn khi screenshot hoặc "Copy Image" từ browser.
    /// </summary>
    internal static class ClipboardImageHelper
    {
        // ── Clipboard format constants ────────────────────────────────────────────
        private const int CF_DIB   = 8;   // Device-Independent Bitmap
        private const int CF_DIBV5 = 17;  // DIB V5 (hỗ trợ alpha tốt hơn)

        // ── Win32 P/Invoke ────────────────────────────────────────────────────────
        [DllImport("user32.dll")] private static extern bool   OpenClipboard(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool   CloseClipboard();
        [DllImport("user32.dll")] private static extern bool   IsClipboardFormatAvailable(int fmt);
        [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(int fmt);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr h);
        [DllImport("kernel32.dll")] private static extern bool  GlobalUnlock(IntPtr h);

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>Kiểm tra clipboard hiện tại có chứa ảnh bitmap không.</summary>
        public static bool HasImage()
            => IsClipboardFormatAvailable(CF_DIBV5) || IsClipboardFormatAvailable(CF_DIB);

        /// <summary>
        /// Đọc ảnh từ clipboard và trả về Texture2D mới.
        /// Trả về null nếu không có ảnh hoặc xảy ra lỗi.
        /// </summary>
        public static Texture2D GetTexture()
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                Debug.LogWarning("[ClipboardImageHelper] Không mở được clipboard.");
                return null;
            }

            try
            {
                // Ưu tiên DIBV5 vì hỗ trợ alpha channel tốt hơn
                int fmt = IsClipboardFormatAvailable(CF_DIBV5) ? CF_DIBV5 : CF_DIB;
                if (!IsClipboardFormatAvailable(fmt))
                {
                    Debug.LogWarning("[ClipboardImageHelper] Clipboard không chứa ảnh bitmap.");
                    return null;
                }

                IntPtr hData = GetClipboardData(fmt);
                if (hData == IntPtr.Zero) return null;

                IntPtr ptr = GlobalLock(hData);
                if (ptr == IntPtr.Zero) return null;

                try   { return ParseDIB(ptr); }
                finally { GlobalUnlock(hData); }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ClipboardImageHelper] Lỗi đọc clipboard: {e.Message}");
                return null;
            }
            finally { CloseClipboard(); }
        }

        // ── DIB parser ────────────────────────────────────────────────────────────

        private static Texture2D ParseDIB(IntPtr ptr)
        {
            // Đọc BITMAPINFOHEADER (40 bytes chuẩn) hoặc BITMAPV5HEADER (124 bytes)
            int  headerSize  = Marshal.ReadInt32(ptr);        // biSize
            int  width       = Marshal.ReadInt32(ptr + 4);    // biWidth
            int  height      = Marshal.ReadInt32(ptr + 8);    // biHeight (âm = top-down)
            short bitCount   = Marshal.ReadInt16(ptr + 14);   // biBitCount
            int  compression = Marshal.ReadInt32(ptr + 16);   // biCompression

            const int BI_RGB       = 0;
            const int BI_BITFIELDS = 3;

            if (bitCount != 24 && bitCount != 32)
            {
                Debug.LogWarning($"[ClipboardImageHelper] Không hỗ trợ {bitCount}bpp. Chỉ hỗ trợ 24/32bpp.");
                return null;
            }

            if (compression != BI_RGB && compression != BI_BITFIELDS)
            {
                Debug.LogWarning($"[ClipboardImageHelper] Compression mode {compression} chưa hỗ trợ.");
                return null;
            }

            bool topDown    = height < 0;
            int  absH       = Math.Abs(height);
            int  bytesPerPx = bitCount / 8;
            // Row stride phải chia hết cho 4 bytes
            int  stride     = ((width * bitCount + 31) / 32) * 4;

            // Offset tới pixel data:
            // headerSize + color masks (12 bytes nếu BI_BITFIELDS, 0 nếu BI_RGB)
            int pixelStart = headerSize + (compression == BI_BITFIELDS ? 12 : 0);

            int  totalBytes = stride * absH;
            byte[] raw = new byte[totalBytes];
            Marshal.Copy(ptr + pixelStart, raw, 0, totalBytes);

            var tex    = new Texture2D(width, absH, TextureFormat.RGBA32, false);
            var colors = new Color32[width * absH];

            for (int y = 0; y < absH; y++)
            {
                // DIB lưu row từ dưới lên (bottom-up) trừ khi topDown
                int srcRow = topDown ? y : (absH - 1 - y);

                for (int x = 0; x < width; x++)
                {
                    int i = srcRow * stride + x * bytesPerPx;
                    // DIB lưu BGR(A) — đảo lại thành RGBA
                    byte b = raw[i];
                    byte g = raw[i + 1];
                    byte r = raw[i + 2];
                    byte a = bytesPerPx == 4 ? raw[i + 3] : (byte)255;
                    // Nếu DIBV5 24bpp alpha thường = 0 dù ảnh đục → force 255
                    if (bytesPerPx == 4 && a == 0) a = 255;
                    colors[y * width + x] = new Color32(r, g, b, a);
                }
            }

            tex.SetPixels32(colors);
            tex.Apply();
            return tex;
        }
    }
}
