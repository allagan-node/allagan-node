using System.IO;
using System.Linq;
using System.Reflection;
using AllaganNode;

namespace IndexRepack
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var inputDir = Path.Combine(baseDir, "input");

            var indexPath = Path.Combine(inputDir, "000000.win32.index");
            var index = new IndexFile();
            index.ReadData(indexPath);

            foreach (var directory in index.DirectoryInfo)
            {
                if (directory.Key != Hash.Compute("common/font")) continue;

                var files = directory.FileInfo.ToList();
                var font1 = files.First(f => f.Key == Hash.Compute("font1.tex"));
                var font8 = new IndexFileInfo
                {
                    Key = Hash.Compute("font8.tex"),
                    DirectoryInfo = directory,
                    WrappedOffset = font1.WrappedOffset
                };
                files.Add(font8);
                directory.FileInfo = files.ToArray();
            }

            var outputDir = Path.Combine(baseDir, "output");
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            var outputPath = Path.Combine(outputDir, "000000.win32.index");
            File.WriteAllBytes(outputPath, index.RepackData(File.ReadAllBytes(indexPath)));
        }
    }
}