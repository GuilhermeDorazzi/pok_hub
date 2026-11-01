using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PokeHub.App.ViewModels;

/// <summary>Ícones das telas: o do próprio programa vinculado, lido do .exe instalado, e os desenhados pelo hub (Views/CardIcons.xaml).</summary>
public static class AppIcons
{
    private const int IconSize = 48;
    private static readonly Dictionary<string, BitmapSource?> Programs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<ResourceDictionary> Drawn = new(() => new ResourceDictionary { Source = new Uri("pack://application:,,,/Views/CardIcons.xaml") });

    public static ImageSource? Drawing(string key) => Drawn.Value["icon." + key] as ImageSource;

    /// <summary>null se o arquivo não for um .exe ou não trouxer ícone, como programas de console e .jar.</summary>
    public static BitmapSource? Program(string? path)
    {
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
        if (Programs.TryGetValue(path, out var known)) return known;
        return Programs[path] = Extract(path);
    }

    private static BitmapSource? Extract(string path)
    {
        var handles = new IntPtr[1];
        var ids = new uint[1];
        uint read = PrivateExtractIcons(path, 0, IconSize, IconSize, handles, ids, 1, 0);
        if (read == 0 || read == uint.MaxValue || handles[0] == IntPtr.Zero) return null;
        try
        {
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(handles[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException) { return null; }
        finally { DestroyIcon(handles[0]); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(string file, int index, int width, int height, IntPtr[] icons, uint[] ids, uint count, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
