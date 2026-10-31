using System.Windows.Media;
using System.Windows.Media.Imaging;
using PokeHub.Core.Graphics;

namespace PokeHub.App;

public static class RgbaImageExtensions
{
    /// <summary>RGBA do Core para um BitmapSource (WPF usa BGRA).</summary>
    public static BitmapSource ToBitmap(this RgbaImage img)
    {
        var bgra = new byte[img.Pixels.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = img.Pixels[i + 2]; bgra[i + 1] = img.Pixels[i + 1]; bgra[i + 2] = img.Pixels[i]; bgra[i + 3] = img.Pixels[i + 3];
        }
        var bmp = BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Bgra32, null, bgra, img.Width * 4);
        bmp.Freeze();
        return bmp;
    }
}
