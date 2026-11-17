using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SUSModder.Services;

internal static class NotificationBadgeRenderer
{
    internal static string? GetLabel(int count) => count <= 0 ? null : count <= 9 ? count.ToString() : "9+";

    internal static Icon RenderTrayIcon(Icon baseIcon, string label)
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.DrawIcon(baseIcon, new Rectangle(0, 0, 32, 32));
            DrawBadge(graphics, label, new Rectangle(12, 12, 20, 20), 10f);
        }
        return CreateIcon(bitmap);
    }

    internal static Icon RenderOverlayIcon(string label)
    {
        using var bitmap = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            DrawBadge(graphics, label, new Rectangle(0, 0, 16, 16), 8f);
        }
        return CreateIcon(bitmap);
    }

    private static void DrawBadge(Graphics graphics, string label, Rectangle bounds, float fontSize)
    {
        using var brush = new SolidBrush(Color.Red);
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.White);
        graphics.FillEllipse(brush, bounds);
        var size = graphics.MeasureString(label, font);
        graphics.DrawString(label, font, textBrush,
            bounds.X + (bounds.Width - size.Width) / 2,
            bounds.Y + (bounds.Height - size.Height) / 2);
    }

    private static Icon CreateIcon(Bitmap bitmap)
    {
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
