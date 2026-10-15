using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace U2BIO.BT003PROLabelPrinting.Tester
{
    // SDK AW 그림과 같은 조건으로 만든 문구 그림이다. Ink는 반열림 범위이며 잉크가 없으면 null, 만들지 못했으면 Failure에 이유가 있다.
    internal sealed class TextRaster : IDisposable
    {
        internal TextRaster(Bitmap image, Rectangle? ink, long bytes, string failure)
        {
            Image = image;
            Ink = ink;
            Bytes = bytes;
            Failure = failure;
        }

        internal Bitmap Image { get; }
        internal Rectangle? Ink { get; }
        internal long Bytes { get; }
        internal string Failure { get; }

        public void Dispose() => Image?.Dispose();
    }

    // SDK(x64 정적 분석)는 AW를 LOGFONT 높이 +size*340/72 셀, 굵기 400/600/700, DEFAULT_CHARSET, FF_ROMAN|VARIABLE_PITCH로 만든 글꼴과
    // CP949 TextOutA (0,0) 기본 정렬로 1bpp 비트맵에 그려 보낸다. 미리보기도 같은 GDI 호출로 그려 실제 잉크 범위를 얻는다.
    internal static class SdkTextRenderer
    {
        internal const string DefaultFace = "돋움체";
        internal const int MaxSide = 8000;
        internal const long MaxPixels = 16000000;
        internal const long BudgetBytes = 32L * 1024 * 1024;
        internal const string BudgetFailure = "미리보기 메모리 한도";
        private const string SdkSettingsKey = @"Software\BitekTechnology\AXBT003";
        private static readonly Encoding Cp949 = Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        // SDK가 AW 글꼴로 읽는 값이다. 없거나 읽지 못하거나 LOGFONT에 넣을 수 없으면 SDK 기본값을 쓴다.
        internal static string FontFace(out bool configured)
        {
            configured = false;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SdkSettingsKey))
                {
                    string face = key?.GetValue("FontName") as string;
                    if (!string.IsNullOrWhiteSpace(face) && TryEncode(face, out byte[] bytes) && bytes.Length <= 31)
                    {
                        configured = true;
                        return face;
                    }
                }
            }
            catch (Exception error) when (error is SecurityException || error is UnauthorizedAccessException || error is IOException) { }
            return DefaultFace;
        }

        // GDI가 요청한 글꼴 대신 고른 실제 글꼴 이름이다. 만들거나 고르지 못하면 null이다.
        internal static string RealizedFace(string face)
        {
            var font = new LOGFONT { lfHeight = 47, lfWeight = 400, lfCharSet = 1, lfPitchAndFamily = 0x12, lfFaceName = new byte[32] };
            if (!TryEncode(face, out byte[] name)) return null;
            Array.Copy(name, font.lfFaceName, Math.Min(name.Length, 31));
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return null;
            IntPtr hfont = CreateFontIndirectA(font);
            try
            {
                if (hfont == IntPtr.Zero) return null;
                IntPtr old = SelectObject(dc, hfont);
                if (old == IntPtr.Zero) return null;
                try
                {
                    var buffer = new byte[64];
                    int length = GetTextFaceA(dc, buffer.Length, buffer);
                    return length <= 1 ? null : Cp949.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0) is int end && end >= 0 ? end : length);
                }
                finally
                {
                    SelectObject(dc, old);
                }
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
            finally
            {
                if (hfont != IntPtr.Zero) DeleteObject(hfont);
                DeleteDC(dc);
            }
        }

        internal static int Weight(int weight) => weight == 0 || weight == 1 ? 400 : weight == 2 ? 600 : weight == 3 ? 700 : weight;

        // 새 그림 하나는 GDI 비트와 GDI+ 사본을 잠시 함께 쓰므로 두 배를 남은 예산과 비교한다.
        internal static bool Fits(long bytes, long used) => bytes >= 0 && bytes * 2 <= BudgetBytes - used;

        internal static TextRaster Render(string face, int size, int weight, string text, long used)
        {
            if (!TryEncode(text, out byte[] bytes)) return Fail("CP949로 바꿀 수 없음");
            if (bytes.Length == 0) return new TextRaster(null, null, 0, null);
            long cell = (long)size * 340 / 72;
            if (cell <= 0 || cell > MaxSide) return Fail("크기 한도 초과");
            var font = new LOGFONT { lfHeight = (int)cell, lfWeight = Weight(weight), lfCharSet = 1, lfPitchAndFamily = 0x12, lfFaceName = new byte[32] };
            byte[] name = Cp949.GetBytes(face);
            Array.Copy(name, font.lfFaceName, Math.Min(name.Length, 31));

            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return Fail("그리기 장치를 만들지 못함");
            IntPtr hfont = IntPtr.Zero, oldFont = IntPtr.Zero, bitmap = IntPtr.Zero, oldBitmap = IntPtr.Zero;
            try
            {
                hfont = CreateFontIndirectA(font);
                if (hfont == IntPtr.Zero) return Fail("글꼴을 만들지 못함");
                oldFont = SelectObject(dc, hfont);
                // 선택에 실패하면 기본 글꼴로 그려지므로 정상 그림으로 보지 않는다.
                if (oldFont == IntPtr.Zero) return Fail("글꼴을 선택하지 못함");
                if (!GetTextExtentPoint32A(dc, bytes, bytes.Length, out SIZE extent) || extent.cx <= 0 || extent.cy <= 0) return Fail("크기를 재지 못함");
                // SDK와 같이 폭을 32픽셀 배수로 올린다. 행 바이트가 4의 배수라 GDI·GDI+ 정렬이 같다.
                long width = ((long)extent.cx + 31) / 32 * 32;
                long height = extent.cy;
                if (width > MaxSide || height > MaxSide || width * height > MaxPixels) return Fail("크기 한도 초과");
                long stride = width / 8;
                long total = stride * height;
                if (!Fits(total, used)) return Fail(BudgetFailure);
                bitmap = CreateBitmap((int)width, (int)height, 1, 1, IntPtr.Zero);
                if (bitmap == IntPtr.Zero) return Fail("그림을 만들지 못함");
                oldBitmap = SelectObject(dc, bitmap);
                if (oldBitmap == IntPtr.Zero) return Fail("그림을 선택하지 못함");
                // 검은 배경(0)에 흰 글자(1)를 그리므로 1인 비트가 잉크다.
                if (!PatBlt(dc, 0, 0, (int)width, (int)height, Blackness)) return Fail("배경을 칠하지 못함");
                SetTextColor(dc, 0xFFFFFF);
                SetBkColor(dc, 0);
                if (!TextOutA(dc, 0, 0, bytes, bytes.Length)) return Fail("글자를 그리지 못함");
                var bits = new byte[total];
                if (GetBitmapBits(bitmap, (int)total, bits) != total) return Fail("그림을 읽지 못함");
                // 표시용 그림을 만들기 전에 GDI 그림을 놓아 비트 배열과 표시용 그림 두 벌만 함께 있게 한다.
                SelectObject(dc, oldBitmap);
                oldBitmap = IntPtr.Zero;
                DeleteObject(bitmap);
                bitmap = IntPtr.Zero;
                Rectangle? ink = InkBounds(bits, (int)width, (int)height, (int)stride);
                return new TextRaster(ToImage(bits, (int)width, (int)height, (int)stride), ink, total, null);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero) SelectObject(dc, oldBitmap);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (oldFont != IntPtr.Zero) SelectObject(dc, oldFont);
                if (hfont != IntPtr.Zero) DeleteObject(hfont);
                DeleteDC(dc);
            }
        }

        // 그릴 때 팔레트 1번만 바꿔 색을 정한다. 0번은 투명이다.
        internal static void SetInkColor(Bitmap image, Color color)
        {
            ColorPalette palette = image.Palette;
            if (palette.Entries[1] == color) return;
            palette.Entries[1] = color;
            image.Palette = palette;
        }

        private static TextRaster Fail(string reason) => new TextRaster(null, null, 0, reason);

        private static bool TryEncode(string text, out byte[] bytes)
        {
            try
            {
                bytes = Cp949.GetBytes(text ?? "");
                return true;
            }
            catch (EncoderFallbackException)
            {
                bytes = null;
                return false;
            }
        }

        private static Rectangle? InkBounds(byte[] bits, int width, int height, int stride)
        {
            int left = width, top = -1, right = -1, bottom = -1;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if ((bits[y * stride + (x >> 3)] & (0x80 >> (x & 7))) == 0) continue;
                    if (top < 0) top = y;
                    bottom = y;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                }
            }
            return top < 0 ? (Rectangle?)null : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }

        private static Bitmap ToImage(byte[] bits, int width, int height, int stride)
        {
            var image = new Bitmap(width, height, PixelFormat.Format1bppIndexed);
            try
            {
                ColorPalette palette = image.Palette;
                palette.Entries[0] = Color.Transparent;
                palette.Entries[1] = Color.Black;
                image.Palette = palette;
                BitmapData data = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format1bppIndexed);
                try
                {
                    for (int y = 0; y < height; y++) Marshal.Copy(bits, y * stride, data.Scan0 + y * data.Stride, stride);
                }
                finally
                {
                    image.UnlockBits(data);
                }
                return image;
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }

        private const int Blackness = 0x00000042;

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        private sealed class LOGFONT
        {
            public int lfHeight;
            public int lfWidth;
            public int lfEscapement;
            public int lfOrientation;
            public int lfWeight;
            public byte lfItalic;
            public byte lfUnderline;
            public byte lfStrikeOut;
            public byte lfCharSet;
            public byte lfOutPrecision;
            public byte lfClipPrecision;
            public byte lfQuality;
            public byte lfPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] lfFaceName;
        }

        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateFontIndirectA([In] LOGFONT font);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool GetTextExtentPoint32A(IntPtr dc, byte[] text, int length, out SIZE size);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, IntPtr bits);
        [DllImport("gdi32.dll")] private static extern bool PatBlt(IntPtr dc, int x, int y, int width, int height, int rop);
        [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr dc, int color);
        [DllImport("gdi32.dll")] private static extern uint SetBkColor(IntPtr dc, int color);
        [DllImport("gdi32.dll")] private static extern bool TextOutA(IntPtr dc, int x, int y, byte[] text, int length);
        [DllImport("gdi32.dll")] private static extern int GetBitmapBits(IntPtr bitmap, int length, byte[] bits);
        [DllImport("gdi32.dll")] private static extern int GetTextFaceA(IntPtr dc, int length, byte[] face);
    }
}
