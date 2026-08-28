using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace BalanceIsland.Windows;

public static class AppIconFactory
{
    public static Icon CreateIcon(int size = 64)
    {
        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            var inset = Math.Max(2, size / 16f);
            var rect = new RectangleF(inset, inset, size - inset * 2, size - inset * 2);
            using var path = RoundedRect(rect, size * .23f);
            using var fill = new LinearGradientBrush(rect,
                Color.FromArgb(255, 77, 98, 255), Color.FromArgb(255, 111, 56, 220), 35f);
            graphics.FillPath(fill, path);

            using var pen = new Pen(Color.FromArgb(235, 255, 255, 255), Math.Max(2f, size * .07f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            var left = size * .30f;
            var top = size * .23f;
            var mid = size * .50f;
            var right = size * .70f;
            var bottom = size * .76f;
            graphics.DrawLine(pen, left, top, left, bottom);
            graphics.DrawBezier(pen, left, top, right, top, right, mid, left, mid);
            graphics.DrawBezier(pen, left, mid, right, mid, right, bottom, left, bottom);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public static BitmapSource CreateImageSource(int size = 64)
    {
        using var icon = CreateIcon(size);
        var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty,
            BitmapSizeOptions.FromWidthAndHeight(size, size));
        source.Freeze();
        return source;
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
