using AllaganNode;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace FontTest
{
    public class Exchange
    {
        public uint GlobalKey;
        public uint GlobalNewKey;
        public uint KoreanKey;
    }

    class Program
    {
        static void Main(string[] args)
        {
            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            string globalIndexPath = Path.Combine(baseDir, "global", "000000.win32.index");
            string globalDatPath = Path.Combine(baseDir, "global", "000000.win32.dat0");

            string koIndexPath = Path.Combine(baseDir, "korean", "000000.win32.index");
            string koDatPath = Path.Combine(baseDir, "korean", "000000.win32.dat0");

            string outputDir = Path.Combine(baseDir, "output");
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            Dictionary<uint, Dictionary<uint, SqFile>> globalSqFiles = readIndex(globalIndexPath);
            Dictionary<uint, Dictionary<uint, SqFile>> koSqFiles = readIndex(koIndexPath);

            string outputIndexPath = Path.Combine(outputDir, Path.GetFileName(globalIndexPath));
            File.Copy(globalIndexPath, outputIndexPath, true);
            //File.Copy(koIndexPath, outputIndexPath, true);
            string outputDatPath = Path.Combine(outputDir, Path.GetFileName(globalDatPath));
            File.Copy(globalDatPath, outputDatPath, true);
            string outputNewDatPath = Path.Combine(outputDir, "000000.win32.dat1");
            File.Copy(koDatPath, outputNewDatPath, true);

            byte[] index = File.ReadAllBytes(outputIndexPath);

            /*Console.WriteLine(koSqFiles[Hash.Compute("common/font")].ContainsKey(Hash.Compute("KrnAxis_96.fdt")));
            Console.ReadLine();*/

            /*
            using (StreamWriter sw = new StreamWriter(Path.Combine(outputDir, "global_keys"), false))
            {
                foreach (uint globalKey in globalSqFiles[Hash.Compute("common/font")].Keys)
                {
                    sw.WriteLine(globalKey.ToString());
                }
            }

            using (StreamWriter sw = new StreamWriter(Path.Combine(outputDir, "korean_keys"), false))
            {
                foreach (uint koreanKey in koSqFiles[Hash.Compute("common/font")].Keys)
                {
                    sw.WriteLine(koreanKey.ToString());
                }
            }
            */

            List<Exchange> exchanges = new List<Exchange>();
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("font1.tex"),
                GlobalNewKey = Hash.Compute("font1.tex"),
                KoreanKey = Hash.Compute("font_krn_1.tex")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("font2.tex"),
                GlobalNewKey = Hash.Compute("font2.tex"),
                KoreanKey = Hash.Compute("font_krn_2.tex")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("font3.tex"),
                GlobalNewKey = Hash.Compute("font3.tex"),
                KoreanKey = Hash.Compute("font_krn_3.tex")
            });
            
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("font_lobby1.tex"),
                GlobalNewKey = Hash.Compute("font_lobby1.tex"),
                KoreanKey = Hash.Compute("font_krn_1.tex")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("font_lobby2.tex"),
                GlobalNewKey = Hash.Compute("font_lobby2.tex"),
                KoreanKey = Hash.Compute("font_krn_2.tex")
            });
            /*exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("font_krn_3.tex"),
                GlobalNewKey = Hash.Compute("font_lobby3.tex"),
                KoreanKey = Hash.Compute("font_krn_3.tex")
            });*/

            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("axis_12.fdt"),
                GlobalNewKey = Hash.Compute("axis_12.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_120.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("axis_14.fdt"),
                GlobalNewKey = Hash.Compute("axis_14.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_140.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("axis_18.fdt"),
                GlobalNewKey = Hash.Compute("axis_18.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_180.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("axis_36.fdt"),
                GlobalNewKey = Hash.Compute("axis_36.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_180.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("axis_96.fdt"),
                GlobalNewKey = Hash.Compute("axis_96.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_180.fdt")
            });

            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("miedingermid_10.fdt"),
                GlobalNewKey = Hash.Compute("miedingermid_10.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_180.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("miedingermid_12.fdt"),
                GlobalNewKey = Hash.Compute("miedingermid_12.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_180.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("miedingermid_14.fdt"),
                GlobalNewKey = Hash.Compute("miedingermid_14.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_180.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("miedingermid_18.fdt"),
                GlobalNewKey = Hash.Compute("miedingermid_18.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_180.fdt")
            });

            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("meidinger_16.fdt"),
                GlobalNewKey = Hash.Compute("meidinger_16.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_180.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("meidinger_20.fdt"),
                GlobalNewKey = Hash.Compute("meidinger_20.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_180.fdt")
            });

            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("trumpgothic_23.fdt"),
                GlobalNewKey = Hash.Compute("trumpgothic_23.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_180.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("trumpgothic_34.fdt"),
                GlobalNewKey = Hash.Compute("trumpgothic_34.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_180.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("trumpgothic_184.fdt"),
                GlobalNewKey = Hash.Compute("trumpgothic_184.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_180.fdt")
            });

            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("jupiter_16.fdt"),
                GlobalNewKey = Hash.Compute("jupiter_16.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_180.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("jupiter_20.fdt"),
                GlobalNewKey = Hash.Compute("jupiter_20.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_180.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("jupiter_23.fdt"),
                GlobalNewKey = Hash.Compute("jupiter_23.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_180.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("jupiter_45.fdt"),
                GlobalNewKey = Hash.Compute("jupiter_45.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_180.fdt")
            });
            
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("axis_12_lobby.fdt"),
                GlobalNewKey = Hash.Compute("axis_12_lobby.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_120.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("axis_14_lobby.fdt"),
                GlobalNewKey = Hash.Compute("axis_14_lobby.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_140.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("axis_18_lobby.fdt"),
                GlobalNewKey = Hash.Compute("axis_18_lobby.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_140.fdt")
            });

            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("miedingermid_10_lobby.fdt"),
                GlobalNewKey = Hash.Compute("miedingermid_10_lobby.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_140.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("miedingermid_12_lobby.fdt"),
                GlobalNewKey = Hash.Compute("miedingermid_12_lobby.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_140.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("miedingermid_14_lobby.fdt"),
                GlobalNewKey = Hash.Compute("miedingermid_14_lobby.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_140.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("miedingermid_18_lobby.fdt"),
                GlobalNewKey = Hash.Compute("miedingermid_18_lobby.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_140.fdt")
            });

            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("meidinger_16_lobby.fdt"),
                GlobalNewKey = Hash.Compute("meidinger_16_lobby.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_140.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("meidinger_20_lobby.fdt"),
                GlobalNewKey = Hash.Compute("meidinger_20_lobby.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_140.fdt")
            });

            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("trumpgothic_23_lobby.fdt"),
                GlobalNewKey = Hash.Compute("trumpgothic_23_lobby.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_140.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("trumpgothic_34_lobby.fdt"),
                GlobalNewKey = Hash.Compute("trumpgothic_34_lobby.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_140.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("trumpgothic_184_lobby.fdt"),
                GlobalNewKey = Hash.Compute("trumpgothic_184_lobby.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_140.fdt")
            });

            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("jupiter_16_lobby.fdt"),
                GlobalNewKey = Hash.Compute("jupiter_16_lobby.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_140.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("jupiter_20_lobby.fdt"),
                GlobalNewKey = Hash.Compute("jupiter_20_lobby.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_140.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("jupiter_23_lobby.fdt"),
                GlobalNewKey = Hash.Compute("jupiter_23_lobby.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_140.fdt")
            });
            exchanges.Add(new Exchange()
            {
                GlobalKey = Hash.Compute("jupiter_45_lobby.fdt"),
                GlobalNewKey = Hash.Compute("jupiter_45_lobby.fdt"),
                KoreanKey = Hash.Compute("KrnAxis_140.fdt")
            });

            foreach (Exchange exchange in exchanges)
            {
                SqFile globalSqFile = globalSqFiles[Hash.Compute("common/font")][exchange.GlobalKey];
                SqFile koSqFile = koSqFiles[Hash.Compute("common/font")][exchange.KoreanKey];
                koSqFile.DatFile = 1;

                int headerOffset = BitConverter.ToInt32(index, 0xc);
                index[headerOffset + 0x50] = 2;
                int fileOffset = BitConverter.ToInt32(index, headerOffset + 0x8);
                int fileCount = BitConverter.ToInt32(index, headerOffset + 0xc) / 0x10;
                for (int i = 0; i < fileCount; i++)
                {
                    int keyOffset = fileOffset + i * 0x10;
                    uint key = BitConverter.ToUInt32(index, keyOffset);
                    uint directoryKey = BitConverter.ToUInt32(index, keyOffset + 0x4);

                    if (key == globalSqFile.Key && directoryKey == globalSqFile.DirectoryKey)
                    {
                        Array.Copy(BitConverter.GetBytes(exchange.GlobalNewKey), 0, index, keyOffset, 0x4);
                        Array.Copy(BitConverter.GetBytes(koSqFile.WrappedOffset), 0, index, keyOffset + 0x8, 0x4);
                    }
                }
            }

            /*
            List<KeyValuePair<string, string>> exchange = new List<KeyValuePair<string, string>>();
            exchange.Add(new KeyValuePair<string, string>("font_krn_1.tex", "font1.tex"));
            exchange.Add(new KeyValuePair<string, string>("font_krn_2.tex", "font2.tex"));
            exchange.Add(new KeyValuePair<string, string>("font_krn_3.tex", "font3.tex"));
            exchange.Add(new KeyValuePair<string, string>("font_krn_1.tex", "font_lobby1.tex"));
            exchange.Add(new KeyValuePair<string, string>("KrnAxis_120.fdt", "axis_12.fdt"));
            exchange.Add(new KeyValuePair<string, string>("KrnAxis_120.fdt", "axis_12_lobby.fdt"));
            exchange.Add(new KeyValuePair<string, string>("KrnAxis_140.fdt", "axis_14.fdt"));
            exchange.Add(new KeyValuePair<string, string>("KrnAxis_140.fdt", "axis_14_lobby.fdt"));
            exchange.Add(new KeyValuePair<string, string>("KrnAxis_180.fdt", "axis_18.fdt"));
            exchange.Add(new KeyValuePair<string, string>("KrnAxis_180.fdt", "axis_18_lobby.fdt"));

            foreach (KeyValuePair<string, string> pair in exchange)
            {
                string koKey = pair.Key;
                string globalKey = pair.Value;

                SqFile globalSqFile = globalSqFiles[Hash.Compute("common/font")][Hash.Compute(globalKey)];
                SqFile koSqFile = koSqFiles[Hash.Compute("common/font")][Hash.Compute(koKey)];
                koSqFile.DatFile = 1;

                int headerOffset = BitConverter.ToInt32(index, 0xc);
                index[headerOffset + 0x50] = 2;
                int fileOffset = BitConverter.ToInt32(index, headerOffset + 0x8);
                int fileCount = BitConverter.ToInt32(index, headerOffset + 0xc) / 0x10;
                for (int i = 0; i < fileCount; i++)
                {
                    int keyOffset = fileOffset + i * 0x10;
                    uint key = BitConverter.ToUInt32(index, keyOffset);
                    uint directoryKey = BitConverter.ToUInt32(index, keyOffset + 0x4);

                    if (key == globalSqFile.Key && directoryKey == globalSqFile.DirectoryKey)
                    {
                        Array.Copy(BitConverter.GetBytes(koSqFile.WrappedOffset), 0, index, keyOffset + 0x8, 0x4);
                    }
                }
            }*/

            File.WriteAllBytes(outputIndexPath, index);
        }

        static Dictionary<uint, Dictionary<uint, SqFile>> readIndex(string indexPath)
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
                    br.ReadInt32();

                    if (!sqFiles.ContainsKey(sqFile.DirectoryKey)) sqFiles.Add(sqFile.DirectoryKey, new Dictionary<uint, SqFile>());
                    sqFiles[sqFile.DirectoryKey].Add(sqFile.Key, sqFile);
                }
            }

            return sqFiles;
        }
    }
}