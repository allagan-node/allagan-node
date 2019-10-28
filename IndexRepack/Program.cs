using AllaganNode;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace IndexRepack
{
    class Program
    {
        static void Main(string[] args)
        {
            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string inputDir = Path.Combine(baseDir, "input");

            string indexPath = Path.Combine(inputDir, "000000.win32.index");
            IndexFile index = new IndexFile();
            index.ReadData(indexPath);

            foreach (IndexDirectoryInfo directory in index.DirectoryInfo)
            {
                if (directory.Key != Hash.Compute("common/font")) continue;

                List<IndexFileInfo> files = directory.FileInfo.ToList();
                IndexFileInfo font1 = files.First(f => f.Key == Hash.Compute("font1.tex"));
                IndexFileInfo font8 = new IndexFileInfo();
                font8.Key = Hash.Compute("font8.tex");
                font8.DirectoryInfo = directory;
                font8.WrappedOffset = font1.WrappedOffset;
                files.Add(font8);
                directory.FileInfo = files.ToArray();
            }

            string outputDir = Path.Combine(baseDir, "output");
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            string outputPath = Path.Combine(outputDir, "000000.win32.index");
            File.WriteAllBytes(outputPath, index.RepackData(File.ReadAllBytes(indexPath)));
        }
    }
}
