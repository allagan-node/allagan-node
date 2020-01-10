using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;
using IndexRepack;

namespace AllaganNode
{
    internal enum ImageFormat
    {
        Unknown = 0,
        A4R4G4B4 = 0x1440
    }

    public class Test
    {
        public Dictionary<string, byte[]> TestDictionary { get; set; }
    }

    internal class Program
    {
        private static string previousLine;

        private static Dictionary<uint, Dictionary<uint, SqFile>> readIndex(string indexPath, string datPath)
        {
            var sqFiles = new Dictionary<uint, Dictionary<uint, SqFile>>();

            using (var fs = File.OpenRead(indexPath))
            using (var br = new BinaryReader(fs))
            {
                br.BaseStream.Position = 0xc;
                var headerOffset = br.ReadInt32();

                br.BaseStream.Position = headerOffset + 0x8;
                var fileOffset = br.ReadInt32();
                var fileCount = br.ReadInt32() / 0x10;

                br.BaseStream.Position = fileOffset;
                for (var i = 0; i < fileCount; i++)
                {
                    var sqFile = new SqFile
                    {
                        Key = br.ReadUInt32(),
                        DirectoryKey = br.ReadUInt32(),
                        WrappedOffset = br.ReadInt32(),
                        DatPath = datPath
                    };

                    br.ReadInt32();

                    if (!sqFiles.ContainsKey(sqFile.DirectoryKey))
                        sqFiles.Add(sqFile.DirectoryKey, new Dictionary<uint, SqFile>());

                    sqFiles[sqFile.DirectoryKey].Add(sqFile.Key, sqFile);

                    Report(string.Format("{0} / {1}: {2}", i, fileCount, sqFile.Key));
                }
            }

            return sqFiles;
        }

        private static void Main(string[] args)
        {
            Console.WriteLine("AllaganTextNode v{0}", Assembly.GetExecutingAssembly().GetName().Version);

            var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var inputDir = Path.Combine(baseDir, "input");
            var globalDir = Path.Combine(inputDir, "global");
            var koreanDir = Path.Combine(inputDir, "korean");

            var glIndexPath = Path.Combine(globalDir, "000000.win32.index");
            var koIndexPath = Path.Combine(koreanDir, "000000.win32.index");

            var glDatPath = Path.Combine(globalDir, "000000.win32.dat0");
            var koDatPath = Path.Combine(koreanDir, "000000.win32.dat0");

            var outputDir = Path.Combine(baseDir, "output");
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            var outputIndexPath = Path.Combine(outputDir, Path.GetFileName(glIndexPath));
            File.Copy(glIndexPath, outputIndexPath, true);
            var index = File.ReadAllBytes(outputIndexPath);

            var indexFile = new IndexFile();
            indexFile.ReadData(index);

            foreach (var directory in indexFile.DirectoryInfo)
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

            index = indexFile.RepackData(index);
            File.WriteAllBytes(outputIndexPath, index);

            var glSqFiles = readIndex(outputIndexPath, glDatPath);
            var koSqFiles = readIndex(koIndexPath, koDatPath);

            var origDat = File.ReadAllBytes(glDatPath);

            var outputNewDatPath = Path.Combine(outputDir, "000000.win32.dat1");
            //CreateNewDat(glDatPath, outputNewDatPath);
            File.Copy(koDatPath, outputNewDatPath, true);

            var glFontTexFile = glSqFiles[Hash.Compute("common/font")][Hash.Compute("font8.tex")];
            var koFontTexFile = koSqFiles[Hash.Compute("common/font")][Hash.Compute("font_krn_1.tex")];

            glFontTexFile.UpdateOffset(koFontTexFile.Offset, 1, index);

            var glMappingFile = glSqFiles[Hash.Compute("common/font")][Hash.Compute("axis_12.fdt")];
            var koMappingFile = koSqFiles[Hash.Compute("common/font")][Hash.Compute("krnaxis_120.fdt")];

            var glMappingBytes = glMappingFile.ReadData();
            var koMappingBytes = koMappingFile.ReadData();

            File.WriteAllBytes(Path.Combine(outputDir, "glMappingBytes"), glMappingBytes);
            File.WriteAllBytes(Path.Combine(outputDir, "koMappingBytes"), koMappingBytes);

            // hangul jamo -> 1100~11ff
            // hangul compatibility jamo -> 3130~318f
            // hangul jamo extended-a -> a960~a97f
            // hangul syllables -> ac00-d7af
            // hangul jamo extended-b -> d7b0~d7ff

            // global range 0x40~0x1d9af
            // korean range 0x40~0x309cf

            var glRows = new Dictionary<string, byte[]>();

            for (long i = 0x60; i <= 0x1d9c0; i += 0x10)
            {
                var row = new byte[0x10];
                Array.Copy(glMappingBytes, i, row, 0, 0x10);

                var j = 0;
                for (j = 0; j < row.Length; j++)
                    if (row[j] == 0)
                        break;

                var utf = new byte[j];
                Array.Copy(row, 0, utf, 0, j);
                Array.Reverse(utf);

                var key = Encoding.UTF8.GetString(utf);
                if (!glRows.ContainsKey(key)) glRows.Add(key, row);
            }

            using (var sw = new StreamWriter(Path.Combine(outputDir, "glRows"), false))
            {
                new XmlSerializer(typeof(string[])).Serialize(sw, glRows.Keys.ToArray());
            }

            var koRows = new Dictionary<string, byte[]>();
            var diffRows = new Dictionary<string, byte[]>();

            for (long i = 0x60; i <= 0x309c0; i += 0x10)
            {
                var row = new byte[0x10];
                Array.Copy(koMappingBytes, i, row, 0, 0x10);

                var j = 0;
                for (j = 0; j < row.Length; j++)
                    if (row[j] == 0)
                        break;

                var utf = new byte[j];
                Array.Copy(row, 0, utf, 0, j);
                Array.Reverse(utf);

                var key = Encoding.UTF8.GetString(utf);
                if (!koRows.ContainsKey(key)) koRows.Add(key, row);

                if (!glRows.ContainsKey(key)) diffRows.Add(key, row);
            }

            using (var sw = new StreamWriter(Path.Combine(outputDir, "koRows"), false))
            {
                new XmlSerializer(typeof(string[])).Serialize(sw, koRows.Keys.ToArray());
            }

            using (var sw = new StreamWriter(Path.Combine(outputDir, "diffRows"), false))
            {
                new XmlSerializer(typeof(string[])).Serialize(sw, diffRows.Keys.ToArray());
            }

            foreach (var key in diffRows.Keys)
            {
                var modRow = new byte[0x10];
                Array.Copy(diffRows[key], 0, modRow, 0, 0x10);
                modRow[0x6] = 0x1;
                modRow[0x7] = 0x0;
                modRow[0x8] = 0x72;
                modRow[0x9] = 0x0;
                modRow[0xa] = 0xda;
                modRow[0xb] = 0x1;
                modRow[0xc] = 0x8;
                modRow[0xd] = 0x10;
                modRow[0xe] = 0x0;
                modRow[0xf] = 0x0;
                glRows.Add(key, modRow);
            }

            var orderedKeys = glRows.Keys.OrderBy(s =>
            {
                var b = Encoding.UTF8.GetBytes(s);
                var p = new byte[4];
                Array.Copy(b, 0, p, 0, b.Length);
                Array.Reverse(p);
                return BitConverter.ToUInt32(p, 0);
            }).ToArray();

            var newMappingPath = Path.Combine(outputDir, "newMapping");

            var mappingHeader = new byte[0x60];
            Array.Copy(glMappingBytes, 0, mappingHeader, 0, 0x60);
            File.WriteAllBytes(newMappingPath, mappingHeader);

            using (var fs = new FileStream(newMappingPath, FileMode.Append, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                foreach (var key in orderedKeys) bw.Write(glRows[key]);
            }

            var mappingTail = new byte[0x430];
            Array.Copy(glMappingBytes, 0x1d9d0, mappingTail, 0, 0x430);

            using (var fs = new FileStream(newMappingPath, FileMode.Append, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(mappingTail);
            }

            var newMapping = File.ReadAllBytes(newMappingPath);
            Array.Copy(BitConverter.GetBytes(newMapping.Length - 0x430), 0, newMapping, 0xc, 0x4);
            Array.Copy(BitConverter.GetBytes((short) ((newMapping.Length - 0x430 - 0x40) / 0x10)), 0, newMapping, 0x24,
                0x2);

            File.WriteAllBytes(newMappingPath, newMapping);

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
            newMapping[0x156] = 0x1b;
            // coordinate
            newMapping[0x158] = 0x0;
            newMapping[0x159] = 0x0;
            newMapping[0x15a] = 0x0;
            newMapping[0x15b] = 0x0;
            //size
            newMapping[0x15c] = 0xff;
            newMapping[0x15d] = 0xff;

            var repackedBuffer = glMappingFile.RepackData(origDat, newMapping);
            glMappingFile.UpdateOffset((int) new FileInfo(outputNewDatPath).Length, 1, index);

            using (var fs = new FileStream(outputNewDatPath, FileMode.Append, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(repackedBuffer);
            }

            File.WriteAllBytes(outputIndexPath, index);

            UpdateDatHash(outputNewDatPath);
            /*
            SqFile fontFile = glSqFiles[Hash.Compute("common/font")][Hash.Compute("font7.tex")];

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

                        image.Save(@"C:\Users\serap\Desktop\test.png", System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                
            }
            */
        }

        private static void Report(string line)
        {
            if (!string.IsNullOrEmpty(previousLine))
            {
                var cleanLine = string.Empty;
                while (cleanLine.Length != previousLine.Length) cleanLine += " ";

                Console.Write(cleanLine + "\r");
            }

            Console.Write(line + "\r");
            previousLine = line;
        }

        // create a new dat file and copy header from existing dat.
        private static void CreateNewDat(string origDatPath, string newDatPath)
        {
            var dat = File.ReadAllBytes(origDatPath);
            var header = new byte[0x800];
            Array.Copy(dat, 0, header, 0, 0x800);
            Array.Copy(BitConverter.GetBytes(0x2), 0, header, 0x400 + 0x10, 0x4);
            File.WriteAllBytes(newDatPath, header);
        }

        // update sha1 hashes with appended data.
        private static void UpdateDatHash(string datPath)
        {
            var dat = File.ReadAllBytes(datPath);

            var data = new byte[dat.Length - 0x800];
            Array.Copy(dat, 0x800, data, 0, data.Length);
            var sha1Data = new SHA1Managed().ComputeHash(data);
            Array.Copy(sha1Data, 0, dat, 0x400 + 0x20, sha1Data.Length);

            var header = new byte[0x3c0];
            Array.Copy(dat, 0x400, header, 0, 0x3c0);
            var sha1Header = new SHA1Managed().ComputeHash(header);
            Array.Copy(sha1Header, 0, dat, 0x400 + 0x3c0, sha1Header.Length);

            File.WriteAllBytes(datPath, dat);
        }
    }
}