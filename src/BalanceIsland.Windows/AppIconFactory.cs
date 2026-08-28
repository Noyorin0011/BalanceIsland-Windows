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

            // A small floating island and two water lines; no letterform, so it stays
            // recognizable in the title bar and notification area at 16–20 px.
            using var sun = new SolidBrush(Color.FromArgb(235, 137, 224, 255));
            graphics.FillEllipse(sun, size * .66f, size * .18f, size * .14f, size * .14f);

            using var island = new GraphicsPath();
            island.StartFigure();
            island.AddBezier(size * .20f, size * .50f,
                size * .32f, size * .35f, size * .66f, size * .35f, size * .80f, size * .50f);
            island.AddBezier(size * .80f, size * .50f,
                size * .67f, size * .57f, size * .33f, size * .57f, size * .20f, size * .50f);
            island.CloseFigure();
            using var islandFill = new SolidBrush(Color.FromArgb(245, 255, 255, 255));
            graphics.FillPath(islandFill, island);

            using var underside = new GraphicsPath();
            underside.AddPolygon(new[]
            {
                new PointF(size * .29f, size * .53f),
                new PointF(size * .71f, size * .53f),
                new PointF(size * .57f, size * .68f),
                new PointF(size * .43f, size * .68f)
            });
            using var undersideFill = new LinearGradientBrush(
                new RectangleF(size * .29f, size * .52f, size * .42f, size * .17f),
                Color.FromArgb(220, 204, 210, 255), Color.FromArgb(60, 129, 104, 238), 90f);
            graphics.FillPath(undersideFill, underside);

            using var pen = new Pen(Color.FromArgb(225, 191, 239, 255), Math.Max(1.4f, size * .045f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            graphics.DrawBezier(pen, size * .20f, size * .76f,
                size * .34f, size * .70f, size * .43f, size * .82f, size * .56f, size * .76f);
            graphics.DrawBezier(pen, size * .56f, size * .76f,
                size * .67f, size * .71f, size * .72f, size * .77f, size * .80f, size * .74f);
            graphics.DrawBezier(pen, size * .30f, size * .86f,
                size * .42f, size * .82f, size * .56f, size * .90f, size * .70f, size * .85f);
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
