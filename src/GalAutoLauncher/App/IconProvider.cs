using System.Drawing;
using System.Reflection;

namespace GalAutoLauncher;

/// <summary>
/// 提供托盘 / 窗体图标，优先使用内嵌资源，失败则回退到系统图标。
/// </summary>
internal static class IconProvider
{
    private static Icon? _cached;

    public static Icon GetAppIcon()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var name = Array.Find(
                assembly.GetManifestResourceNames(),
                n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));

            if (name is not null)
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream is not null)
                {
                    _cached = new Icon(stream);
                    return _cached;
                }
            }

            // 回退：读取 exe 自身图标
            var exeIcon = Icon.ExtractAssociatedIcon(AppInfo.ExePath);
            if (exeIcon is not null)
            {
                _cached = exeIcon;
                return _cached;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"加载图标失败：{ex.Message}");
        }

        _cached = SystemIcons.Application;
        return _cached;
    }

    public static Bitmap CreateTrayBitmap(int size = 32)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var background = new SolidBrush(Color.FromArgb(0x2B, 0x2F, 0x3E));
        graphics.FillEllipse(background, 0, 0, size - 1, size - 1);

        using var pen = new Pen(Color.FromArgb(0x7C, 0xC8, 0xFF), Math.Max(1.5f, size / 12f));
        graphics.DrawLine(pen, size * 0.32f, size * 0.32f, size * 0.68f, size * 0.68f);
        graphics.DrawLine(pen, size * 0.68f, size * 0.32f, size * 0.32f, size * 0.68f);

        return bitmap;
    }
}
