using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

namespace FontPngDecoder
{
    class Program
    {
        static void Main(string[] args)
        {
            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string inputFontPath = Path.Combine(baseDir, "font3.png");

            Bitmap fontPng = new Bitmap(inputFontPath);
            Bitmap aFont = new Bitmap(fontPng.Width, fontPng.Height);
            Bitmap rFont = new Bitmap(fontPng.Width, fontPng.Height);
            Bitmap gFont = new Bitmap(fontPng.Width, fontPng.Height);
            Bitmap bFont = new Bitmap(fontPng.Width, fontPng.Height);
            Bitmap rgbFont = new Bitmap(fontPng.Width, fontPng.Height);

            for (int x = 0; x < fontPng.Width; x++)
            {
                for (int y = 0; y < fontPng.Height; y++)
                {
                    Color c = fontPng.GetPixel(x, y);

                    aFont.SetPixel(x, y, Color.FromArgb(255, c.A, c.A, c.A));
                    rFont.SetPixel(x, y, Color.FromArgb(255, c.R, c.R, c.R));
                    gFont.SetPixel(x, y, Color.FromArgb(255, c.G, c.G, c.G));
                    bFont.SetPixel(x, y, Color.FromArgb(255, c.B, c.B, c.B));
                    rgbFont.SetPixel(x, y, Color.FromArgb(255, c.R, c.G, c.B));
                }
            }

            aFont.Save(Path.Combine(baseDir, "font_a.png"), ImageFormat.Png);
            rFont.Save(Path.Combine(baseDir, "font_r.png"), ImageFormat.Png);
            gFont.Save(Path.Combine(baseDir, "font_g.png"), ImageFormat.Png);
            bFont.Save(Path.Combine(baseDir, "font_b.png"), ImageFormat.Png);
            rgbFont.Save(Path.Combine(baseDir, "font_rgb.png"), ImageFormat.Png);
        }
    }
}
