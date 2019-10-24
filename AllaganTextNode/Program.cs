using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace AllaganNode
{
    enum ImageFormat
    {
        Unknown = 0,
        A4R4G4B4 = 0x1440
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

            byte[] origDat = File.ReadAllBytes(glDatPath);

            string outputNewDatPath = Path.Combine(outputDir, "000000.win32.dat1");
            File.Copy(koDatPath, outputNewDatPath, true);

            SqFile glFontTexFile = glSqFiles[Hash.Compute("common/font")][Hash.Compute("font1.tex")];
            SqFile koFontTexFile = koSqFiles[Hash.Compute("common/font")][Hash.Compute("font_krn_1.tex")];

            glFontTexFile.UpdateOffset(koFontTexFile.Offset, 1, index);

            SqFile mappingFile = glSqFiles[Hash.Compute("common/font")][Hash.Compute("axis_12.fdt")];
            //SqFile mappingFile = sqFiles[Hash.Compute("exd")][Hash.Compute("Achievement_0_en.exd")];

            byte[] test = mappingFile.ReadData();
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
            
            // code page?
            test[0x156] = 0x2;
            // coordinate
            test[0x158] = 0x61;
            test[0x159] = 0x2;
            test[0x15a] = 0x5f;
            test[0x15b] = 0x2;
            //size
            test[0x15c] = 0x8;
            test[0x15d] = 0x10;

            File.WriteAllBytes(@"C:\Users\serap\Desktop\test", test);

            byte[] buffer = mappingFile.RepackData(origDat, test);
            mappingFile.UpdateOffset((int)new FileInfo(outputNewDatPath).Length, 1, index);

            using (FileStream fs = new FileStream(outputNewDatPath, FileMode.Append, FileAccess.Write))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                bw.Write(buffer);
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