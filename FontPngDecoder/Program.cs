using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

namespace FontPngDecoder
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var inputFontPath = Path.Combine(baseDir, "font3.png");

            var fontPng = new Bitmap(inputFontPath);
            var aFont = new Bitmap(fontPng.Width, fontPng.Height);
            var rFont = new Bitmap(fontPng.Width, fontPng.Height);
            var gFont = new Bitmap(fontPng.Width, fontPng.Height);
            var bFont = new Bitmap(fontPng.Width, fontPng.Height);
            var rgbFont = new Bitmap(fontPng.Width, fontPng.Height);

            for (var x = 0; x < fontPng.Width; x++)
            for (var y = 0; y < fontPng.Height; y++)
            {
                var c = fontPng.GetPixel(x, y);

                aFont.SetPixel(x, y, Color.FromArgb(255, c.A, c.A, c.A));
                rFont.SetPixel(x, y, Color.FromArgb(255, c.R, c.R, c.R));
                gFont.SetPixel(x, y, Color.FromArgb(255, c.G, c.G, c.G));
                bFont.SetPixel(x, y, Color.FromArgb(255, c.B, c.B, c.B));
                rgbFont.SetPixel(x, y, Color.FromArgb(255, c.R, c.G, c.B));
            }

            aFont.Save(Path.Combine(baseDir, "font_a.png"), ImageFormat.Png);
            rFont.Save(Path.Combine(baseDir, "font_r.png"), ImageFormat.Png);
            gFont.Save(Path.Combine(baseDir, "font_g.png"), ImageFormat.Png);
            bFont.Save(Path.Combine(baseDir, "font_b.png"), ImageFormat.Png);
            rgbFont.Save(Path.Combine(baseDir, "font_rgb.png"), ImageFormat.Png);
        }
    }
}