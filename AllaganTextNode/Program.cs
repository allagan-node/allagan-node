using IndexRepack;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace AllaganNode
{
    enum ImageFormat
    {
        Unknown = 0,
        A4R4G4B4 = 0x1440
    }

    public class Test
    {
        public Dictionary<string, byte[]> TestDictionary { get; set; }
    }

    class Program
    {
        static Dictionary<uint, Dictionary<uint, SqFile>> readIndex(string indexPath, string datPath)
        {
            Dictionary<uint, Dictionary<uint, SqFile>> sqFiles = new Dictionary<uint, Dictionary<uint, SqFile>>();

            using (FileStream fs = File.OpenRead(indexPath))
            using (BinaryReader br = new BinaryReader(fs))
            {
                br.BaseStream.Position = 0xc;
                int headerOffset = br.ReadInt32();

                br.BaseStream.Position = headerOffset + 0x8;
                int fileOffset = br.ReadInt32();
                int fileCount = br.ReadInt32() / 0x10;

                br.BaseStream.Position = fileOffset;
                for (int i = 0; i < fileCount; i++)
                {
                    SqFile sqFile = new SqFile();
                    sqFile.Key = br.ReadUInt32();
                    sqFile.DirectoryKey = br.ReadUInt32();
                    sqFile.WrappedOffset = br.ReadInt32();
                    sqFile.DatPath = datPath;

                    br.ReadInt32();

                    if (!sqFiles.ContainsKey(sqFile.DirectoryKey)) sqFiles.Add(sqFile.DirectoryKey, new Dictionary<uint, SqFile>());
                    sqFiles[sqFile.DirectoryKey].Add(sqFile.Key, sqFile);

                    Report(string.Format("{0} / {1}: {2}", i, fileCount, sqFile.Key));
                }
            }

            return sqFiles;
        }

        static unsafe void Main(string[] args)
        {
            Console.WriteLine(string.Format("AllaganTextNode v{0}", Assembly.GetExecutingAssembly().GetName().Version.ToString()));

            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string inputDir = Path.Combine(baseDir, "input");
            string globalDir = Path.Combine(inputDir, "global");
            string koreanDir = Path.Combine(inputDir, "korean");

            string glIndexPath = Path.Combine(globalDir, "000000.win32.index");
            string koIndexPath = Path.Combine(koreanDir, "000000.win32.index");

            string glDatPath = Path.Combine(globalDir, "000000.win32.dat0");
            string koDatPath = Path.Combine(koreanDir, "000000.win32.dat0");

            Dictionary<uint, Dictionary<uint, SqFile>> glSqFiles = readIndex(glIndexPath, glDatPath);
            Dictionary<uint, Dictionary<uint, SqFile>> koSqFiles = readIndex(koIndexPath, koDatPath);

            string outputDir = Path.Combine(baseDir, "output");
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            string outputIndexPath = Path.Combine(outputDir, Path.GetFileName(glIndexPath));
            File.Copy(glIndexPath, outputIndexPath, true);
            byte[] index = File.ReadAllBytes(outputIndexPath);
            /*
            IndexFile indexFile = new IndexFile();
            indexFile.ReadData(index);

            foreach (IndexDirectoryInfo directory in indexFile.DirectoryInfo)
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

            index = indexFile.RepackData(index);
            */
            byte[] origDat = File.ReadAllBytes(glDatPath);

            string outputNewDatPath = Path.Combine(outputDir, "000000.win32.dat1");
            //CreateNewDat(glDatPath, outputNewDatPath);
            File.Copy(koDatPath, outputNewDatPath, true);

            SqFile glFontTexFile = glSqFiles[Hash.Compute("common/font")][Hash.Compute("font7.tex")];
            SqFile koFontTexFile = koSqFiles[Hash.Compute("common/font")][Hash.Compute("font_krn_1.tex")];

            glFontTexFile.UpdateOffset(koFontTexFile.Offset, 1, index);

            SqFile glMappingFile = glSqFiles[Hash.Compute("common/font")][Hash.Compute("axis_12.fdt")];
            SqFile koMappingFile = koSqFiles[Hash.Compute("common/font")][Hash.Compute("krnaxis_120.fdt")];

            byte[] glMappingBytes = glMappingFile.ReadData();
            byte[] koMappingBytes = koMappingFile.ReadData();

            File.WriteAllBytes(Path.Combine(outputDir, "glMappingBytes"), glMappingBytes);
            File.WriteAllBytes(Path.Combine(outputDir, "koMappingBytes"), koMappingBytes);

            // hangul jamo -> 1100~11ff
            // hangul compatibility jamo -> 3130~318f
            // hangul jamo extended-a -> a960~a97f
            // hangul syllables -> ac00-d7af
            // hangul jamo extended-b -> d7b0~d7ff

            // global range 0x40~0x1d9af
            // korean range 0x40~0x309cf

            Dictionary<string, byte[]> glRows = new Dictionary<string, byte[]>();

            for (long i = 0x40; i <= 0x1d9af; i += 0x10)
            {
                byte[] row = new byte[0x10];
                Array.Copy(glMappingBytes, i, row, 0, 0x10);

                int j = 0;
                for (j = 0; j < row.Length; j++)
                {
                    if (row[j] == 0) break;
                }

                byte[] utf = new byte[j];
                Array.Copy(row, 0, utf, 0, j);
                Array.Reverse(utf);

                string key = Encoding.UTF8.GetString(utf);
                if (!glRows.ContainsKey(key)) glRows.Add(key, row);
            }

            using (StreamWriter sw = new StreamWriter(Path.Combine(outputDir, "glRows"), false))
            {
                new XmlSerializer(typeof(string[])).Serialize(sw, glRows.Keys.ToArray());
            }

            Dictionary<string, byte[]> koRows = new Dictionary<string, byte[]>();
            Dictionary<string, byte[]> diffRows = new Dictionary<string, byte[]>();

            for (long i = 0x40; i <= 0x309cf; i += 0x10)
            {
                byte[] row = new byte[0x10];
                Array.Copy(koMappingBytes, i, row, 0, 0x10);

                int j = 0;
                for (j = 0; j < row.Length; j++)
                {
                    if (row[j] == 0) break;
                }

                byte[] utf = new byte[j];
                Array.Copy(row, 0, utf, 0, j);
                Array.Reverse(utf);

                string key = Encoding.UTF8.GetString(utf);
                if (!koRows.ContainsKey(key)) koRows.Add(key, row);
                if (!glRows.ContainsKey(key)) diffRows.Add(key, row);
            }

            using (StreamWriter sw = new StreamWriter(Path.Combine(outputDir, "koRows"), false))
            {
                new XmlSerializer(typeof(string[])).Serialize(sw, koRows.Keys.ToArray());
            }

            using (StreamWriter sw = new StreamWriter(Path.Combine(outputDir, "diffRows"), false))
            {
                new XmlSerializer(typeof(string[])).Serialize(sw, diffRows.Keys.ToArray());
            }

            /*foreach (string key in diffRows.Keys)
            {
                glRows.Add(key, diffRows[key]);
            }*/

            string[] orderedKeys = glRows.Keys.OrderBy(s => {
                byte[] b = Encoding.UTF8.GetBytes(s);
                byte[] p = new byte[4];
                Array.Copy(b, 0, p, 0, b.Length);
                Array.Reverse(p);
                return BitConverter.ToUInt32(p, 0);
            }).ToArray();

            string newMappingPath = Path.Combine(outputDir, "newMapping");

            byte[] mappingHeader = new byte[0x40];
            Array.Copy(glMappingBytes, 0, mappingHeader, 0, 0x40);
            File.WriteAllBytes(newMappingPath, mappingHeader);

            using (FileStream fs = new FileStream(newMappingPath, FileMode.Append, FileAccess.Write))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                foreach (string key in orderedKeys)
                {
                    bw.Write(glRows[key]);

                    if (key == " ") bw.Write(new byte[] { 0x20, 0x0, 0x0, 0x0, 0x20, 0x0, 0x1, 0x0, 0xa1, 0x2, 0xc9, 0x1, 0x5, 0x10, 0xfe, 0x0 });
                }
            }

            /*
            byte[] test = new byte[0x10];
            Array.Copy(koMappingBytes, 0x3b40, test, 0, 0x10);

            byte[] test2 = new byte[3];
            Array.Copy(test, 0, test2, 0, 3);
            Array.Reverse(test2);
            Console.WriteLine();
            foreach (byte b in test2)
            {
                Console.WriteLine(b.ToString());
            }
            Console.WriteLine();
            Console.WriteLine(Encoding.UTF8.GetString(test2));

            byte[] test3 = Encoding.UTF8.GetBytes("가");

            foreach (byte b in test3)
            {
                Console.WriteLine(b.ToString());
            }

            Console.WriteLine(Encoding.UTF8.GetString(test3));
            Console.ReadLine();*/

            //test[0x154] = 0x31;
            //test[0x158] = 0x60; <--- this seems to control coordinate? goes from 0x0~0xff
            //test[0x159] <--- seems to increment with 158. when 0x158 goes over 0xff this gets incremented by 0x1
            //test[0x15a] <-- when 0x159 goes over 0x3 this changes
            //test[0x15b] <-- this also goes upto 0x3
            //test[0x15c] = 0x6; -> width (on texture)
            //test[0x15d] = 0x2; -> height (on texture)
            // c~f looks like size-related thing.

            // in the fdt header area there seems to be something that controls how the texture is loaded...
            // compare axis_12.fdt with krnaxis_120.fdt

            // 0x0~0x3 -> unicode, big endian (flipped)

            // code page?
            // -> 0x0~0x3 points to tex1
            // -> 0x4~0x7 points to tex2 (coordinate is the same)
            // -> 0x8 points to tex3 if tex3 is present. Otherwise went back to tex1.
            // -> 0x9 is empty (maybe original tex3)
            // seems 100 increase = 0x4 increase -> tex increment
            // 0     100   1000  1100
            // 0x0   0x4   0x8   0xc   0x10  0x14  0x18
            // font1 font2 font3 font4 font5 font6 font7
            /*test[0x156] = 0x1c;
            // coordinate
            test[0x158] = 0x0;
            test[0x159] = 0x0;
            test[0x15a] = 0x0;
            test[0x15b] = 0x0;
            //size
            test[0x15c] = 0xff;
            test[0x15d] = 0xff;*/

            byte[] repackedBuffer = glMappingFile.RepackData(origDat, glMappingBytes);
            glMappingFile.UpdateOffset((int)new FileInfo(outputNewDatPath).Length, 1, index);

            using (FileStream fs = new FileStream(outputNewDatPath, FileMode.Append, FileAccess.Write))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                bw.Write(repackedBuffer);
            }

            File.WriteAllBytes(outputIndexPath, index);

            UpdateDatHash(outputNewDatPath);

            /*
            SqFile fontFile = sqFiles[Hash.Compute("common/font")][Hash.Compute("font8.tex")];

            using (FileStream fs = File.OpenRead(fontFile.DatPath))
            using (BinaryReader br = new BinaryReader(fs))
            {
                br.BaseStream.Position = fontFile.Offset;
                int endOfHeader = br.ReadInt32();

                byte[] header = new byte[endOfHeader];
                br.BaseStream.Position = fontFile.Offset;
                br.Read(header, 0, endOfHeader);

                Console.WriteLine();
                Console.WriteLine(BitConverter.ToInt32(header, 0x4));

                byte[] imageHeader = new byte[0x50];
                br.Read(imageHeader, 0, 0x50);

                short imageFormat = BitConverter.ToInt16(imageHeader, 0x4);
                short width = BitConverter.ToInt16(imageHeader, 0x8);
                short height = BitConverter.ToInt16(imageHeader, 0xa);

                Console.WriteLine(imageFormat);

                if (!Enum.IsDefined(typeof(ImageFormat), (int)imageFormat)) throw new Exception();

                short blockCount = BitConverter.ToInt16(header, 0x14);
                int lengthsStartOffset = 0x18 + blockCount * 0x14;

                List<ushort> lengths = new List<ushort>();

                for (int i = lengthsStartOffset; i + 1 < header.Length; i += 2)
                {
                    ushort length = BitConverter.ToUInt16(header, i);
                    if (length == 0) break;

                    lengths.Add(length);
                }

                ushort[] lengthArray = lengths.ToArray();

                using (MemoryStream ms = new MemoryStream())
                {
                    int blockOffset = 0;

                    for (int i = 0; i < lengthArray.Length; i++)
                    {
                        byte[] blockHeader = new byte[0x10];
                        br.BaseStream.Position = fontFile.Offset + endOfHeader + 0x50 + blockOffset;
                        br.Read(blockHeader, 0, 0x10);

                        int magic = BitConverter.ToInt32(blockHeader, 0);
                        if (magic != 0x10) throw new Exception();

                        int sourceSize = BitConverter.ToInt32(blockHeader, 0x8);
                        int rawSize = BitConverter.ToInt32(blockHeader, 0xc);

                        Console.WriteLine(sourceSize.ToString() + ", " + rawSize.ToString());

                        bool isCompressed = sourceSize < 0x7d00;
                        int actualSize = isCompressed ? sourceSize : rawSize;

                        int paddingLeftover = (actualSize + 0x10) % 0x80;
                        if (isCompressed && paddingLeftover != 0)
                        {
                            actualSize += 0x80 - paddingLeftover;
                        }

                        byte[] blockBuffer = new byte[actualSize];
                        br.Read(blockBuffer, 0, actualSize);

                        if (isCompressed)
                        {
                            using (MemoryStream _ms = new MemoryStream(blockBuffer))
                            using (DeflateStream ds = new DeflateStream(_ms, CompressionMode.Decompress))
                            {
                                ds.CopyTo(ms);
                            }
                        }
                        else
                        {
                            ms.Write(blockBuffer, 0, blockBuffer.Length);
                        }

                        blockOffset += lengthArray[i];
                    }

                    byte[] data = ms.ToArray();

                    // A4R4G4B4
                    if (imageFormat == 0x1440)
                    {
                        byte[] argb = new byte[width * height * 4];

                        for (int i = 0; (i + 2) <= 2 * width * height; i += 2)
                        {
                            ushort v = BitConverter.ToUInt16(data, i);

                            for (int j = 0; j < 4; j++)
                            {
                                argb[i * 2 + j] = (byte)(((v >> (4 * j)) & 0xf) << 4);
                            }
                        }
                        
                        Image image;
                        fixed (byte* p = argb)
                        {
                            IntPtr ptr = (IntPtr)p;
                            using (Bitmap tempImage = new Bitmap(width, height, width * 4, PixelFormat.Format32bppArgb, ptr))
                            {
                                image = new Bitmap(tempImage);
                            }
                        }

                        image.Save(@"C:\Users\serap\Desktop\test2.png", System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                
            }*/
        }

        private static string previousLine;
        static void Report(string line)
        {
            if (!string.IsNullOrEmpty(previousLine))
            {
                string cleanLine = string.Empty;
                while (cleanLine.Length != previousLine.Length) cleanLine += " ";
                Console.Write(cleanLine + "\r");
            }

            Console.Write(line + "\r");
            previousLine = line;
        }

        // create a new dat file and copy header from existing dat.
        static void CreateNewDat(string origDatPath, string newDatPath)
        {
            byte[] dat = File.ReadAllBytes(origDatPath);
            byte[] header = new byte[0x800];
            Array.Copy(dat, 0, header, 0, 0x800);
            Array.Copy(BitConverter.GetBytes(0x2), 0, header, 0x400 + 0x10, 0x4);
            File.WriteAllBytes(newDatPath, header);
        }

        // update sha1 hashes with appended data.
        static void UpdateDatHash(string datPath)
        {
            byte[] dat = File.ReadAllBytes(datPath);

            byte[] data = new byte[dat.Length - 0x800];
            Array.Copy(dat, 0x800, data, 0, data.Length);
            byte[] sha1Data = new SHA1Managed().ComputeHash(data);
            Array.Copy(sha1Data, 0, dat, 0x400 + 0x20, sha1Data.Length);

            byte[] header = new byte[0x3c0];
            Array.Copy(dat, 0x400, header, 0, 0x3c0);
            byte[] sha1Header = new SHA1Managed().ComputeHash(header);
            Array.Copy(sha1Header, 0, dat, 0x400 + 0x3c0, sha1Header.Length);

            File.WriteAllBytes(datPath, dat);
        }
    }
}