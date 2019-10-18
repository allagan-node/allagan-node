using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace AllaganNode
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(string.Format("AllaganNode v{0}", Assembly.GetExecutingAssembly().GetName().Version.ToString()));

            // TODO: make base path selectable.
            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            string indexPath = Path.Combine(baseDir, "input", "0a0000.win32.index");
            string datPath = Path.Combine(baseDir, "input", "0a0000.win32.dat0");
            string outputDir = Path.Combine(baseDir, "output");
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            Dictionary<uint, Dictionary<uint, SqFile>> sqFiles = new Dictionary<uint, Dictionary<uint, SqFile>>();
            List<string> headerNames = new List<string>();
            SqFile rootFile;
            SqFile[] exHeaders;

            // Read index and cache all available sqfiles.
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

                    // uncompress and cache data buffer for all sqfiles.
                    // TODO: only read required sqfiles.
                    sqFile.ReadData(datPath);

                    if (!sqFiles.ContainsKey(sqFile.DirectoryKey)) sqFiles.Add(sqFile.DirectoryKey, new Dictionary<uint, SqFile>());
                    sqFiles[sqFile.DirectoryKey].Add(sqFile.Key, sqFile);

                    Report(string.Format("{0} / {1}: {2}", i, fileCount, sqFile.Key));
                }
            }

            // find root file that lists all ExHs in 0a0000.
            // root file encoding is simple ASCII.
            rootFile = sqFiles[Hash.Compute("exd")][Hash.Compute("root.exl")];
            using (MemoryStream ms = new MemoryStream(rootFile.Data))
            using (StreamReader sr = new StreamReader(ms, Encoding.ASCII))
            using (StreamWriter sw = new StreamWriter(Path.Combine(outputDir, "root.exl")))
            {
                sr.ReadLine();

                while (!sr.EndOfStream)
                {
                    string line = sr.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    Report(line);
                    sw.WriteLine(line);

                    string[] split = line.Split(',');
                    if (split.Length != 2) continue;

                    headerNames.Add(split[0]);
                }
            }

            // for all ExHs, decode the cached data buffer as ExH.
            for (int i = 0; i < headerNames.Count; i++)
            {
                Report(string.Format("{0} / {1}: {2}", i, headerNames.Count, headerNames[i]));

                string headerDir = string.Empty;
                string headerName = headerNames[i];

                if (headerName.Contains("/"))
                {
                    headerDir = string.Format("/{0}", headerName.Substring(0, headerName.LastIndexOf("/")));
                    headerName = headerName.Substring(headerName.LastIndexOf("/") + 1);
                }

                headerDir = string.Format("exd{0}", headerDir);

                SqFile sqFile = sqFiles[Hash.Compute(headerDir)][Hash.Compute(string.Format("{0}.exh", headerName))];
                sqFile.Name = headerName;
                sqFile.Dir = headerDir;
                sqFile.ReadExH();
            }

            // only add ExHs with supported variant and string columns.
            List<SqFile> exHeaderList = new List<SqFile>();
            foreach (uint directoryKey in sqFiles.Keys)
            {
                foreach (uint key in sqFiles[directoryKey].Keys)
                {
                    SqFile sqFile = sqFiles[directoryKey][key];

                    if (sqFile.Variant == 1 && sqFile.Columns != null && sqFile.Columns.Length > 0)
                    {
                        exHeaderList.Add(sqFile);
                    }
                }
            }
            exHeaders = exHeaderList.ToArray();

            // for all ExHs, decode child ExDs and link them to ExH.
            for (int i = 0; i < exHeaders.Length; i++)
            {
                Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exHeaders[i].Name));

                SqFile exHeader = exHeaders[i];

                foreach (ExHLanguage lang in exHeader.Languages)
                {
                    foreach (ExHRange range in exHeader.Ranges)
                    {
                        string datName = string.Format("{0}_{1}_{2}.exd", exHeader.Name, range.Start, lang.Code);

                        uint directoryKey = Hash.Compute(exHeader.Dir);
                        uint key = Hash.Compute(datName);
                        if (!sqFiles.ContainsKey(directoryKey)) continue;
                        if (!sqFiles[directoryKey].ContainsKey(key)) continue;
                        SqFile exDat = sqFiles[directoryKey][key];
                        exDat.Columns = exHeader.Columns;
                        exDat.FixedSizeDataLength = exHeader.FixedSizeDataLength;
                        exDat.ReadExD();

                        exDat.Name = datName;
                        exDat.Dir = string.Format("{0}/{1}/{2}", exHeader.Dir, exHeader.Name, range.Start);
                        exDat.LanguageCode = lang.Code;
                        exHeader.ExDats.Add(exDat);
                    }
                }
            }

            Report(string.Empty);
            Console.Write("Enter an option (0 - extract, 1 - repackage): ");
            switch (int.Parse(Console.ReadLine()))
            {
                case 0:
                    // extract all ExDs under cached ExHs.
                    for (int i = 0; i < exHeaders.Length; i++)
                    {
                        SqFile exHeader = exHeaders[i];

                        Report(string.Format("{0} / {1}: {2}", i.ToString(), exHeaders.Length.ToString(), exHeader.Name));

                        foreach (SqFile exDat in exHeader.ExDats)
                        {
                            string exDatOutDir = Path.Combine(outputDir, exDat.Dir);
                            if (!Directory.Exists(exDatOutDir)) Directory.CreateDirectory(exDatOutDir);

                            string exDatOutPath = Path.Combine(exDatOutDir, exDat.LanguageCode);

                            using (StreamWriter sw = new StreamWriter(exDatOutPath, false))
                            {
                                JArray jArray = new JArray();

                                foreach (ExDChunk chunk in exDat.Chunks.Values)
                                {
                                    jArray.Add(chunk.GetJObject());
                                }

                                sw.Write(jArray.ToString());
                            }
                            /*
                            string exDatCsvPath = Path.Combine(exDatOutDir, exDat.LanguageCode + ".csv");
                            using (StreamWriter sw = new StreamWriter(exDatCsvPath, false))
                            {
                                int[] orderedKeys = exDat.Chunks.Keys.OrderBy(x => x).ToArray();
                                for (int j = 0; j < orderedKeys.Length; j++)
                                {
                                    ExDChunk chunk = exDat.Chunks[orderedKeys[j]];
                                    string line = chunk.Key + ",";
                                    ushort[] orderedColumns = chunk.Fields.Keys.OrderBy(x => x).ToArray();
                                    for (int k = 0; k < orderedColumns.Length; k++)
                                    {
                                        byte[] field = chunk.Fields[orderedColumns[k]];
                                        line += orderedColumns[k] + ",\"" + JsonConvert.SerializeObject(new UTF8Encoding(false).GetString(field));
                                    }
                                    sw.WriteLine(line);
                                }
                            }*/
                        }
                    }
                    break;

                case 1:
                    // repack only selected lang table from extracted output dir.
                    Console.Write("Enter lang codes to repack (separated by comma): ");
                    string[] targetLangCodes = Console.ReadLine().Split(',');

                    string outputIndexPath = Path.Combine(outputDir, Path.GetFileName(indexPath));
                    File.Copy(indexPath, outputIndexPath, true);
                    string outputDatPath = Path.Combine(outputDir, Path.GetFileName(datPath));
                    File.Copy(datPath, outputDatPath, true);
                    string outputNewDatPath = Path.Combine(outputDir, "0a0000.win32.dat1");
                    CreateNewDat(outputDatPath, outputNewDatPath);

                    byte[] origDat = File.ReadAllBytes(outputDatPath);
                    byte[] newDat = File.ReadAllBytes(outputNewDatPath);
                    byte[] index = File.ReadAllBytes(outputIndexPath);

                    for (int i = 0; i < exHeaders.Length; i++)
                    {
                        SqFile exHeader = exHeaders[i];

                        foreach (SqFile exDat in exHeader.ExDats)
                        {
                            Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                            if (!targetLangCodes.Contains(exDat.LanguageCode)) continue;

                            string exDatOutDir = Path.Combine(outputDir, exDat.Dir);
                            if (!Directory.Exists(exDatOutDir)) continue;

                            string exDatOutPath = Path.Combine(exDatOutDir, exDat.LanguageCode);
                            if (!File.Exists(exDatOutPath)) continue;

                            JObject[] jChunks = null;

                            using (StreamReader sr = new StreamReader(exDatOutPath))
                            {
                                jChunks = JArray.Parse(sr.ReadToEnd()).Select(j => (JObject)j).ToArray();
                            }

                            foreach (JObject jChunk in jChunks)
                            {
                                int chunkKey = (int)jChunk["Key"];
                                if (!exDat.Chunks.ContainsKey(chunkKey)) continue;

                                exDat.Chunks[chunkKey].LoadJObject(jChunk);
                            }

                            exDat.WriteExD();
                            exDat.WriteData(origDat, ref newDat, index);
                        }
                    }

                    File.WriteAllBytes(outputDatPath, origDat);
                    File.WriteAllBytes(outputNewDatPath, newDat);
                    File.WriteAllBytes(outputIndexPath, index);

                    UpdateDatHash(outputNewDatPath);
                    break;

                case 9876:
                    // swap two lang table based on string key mapping (if available) or chunk key mapping.
                    Console.Write("Enter source lang code: ");
                    string sourceLangCode = Console.ReadLine();
                    Console.Write("Enter target lang code: ");
                    string targetLangCode = Console.ReadLine();

                    for (int i = 0; i < exHeaders.Length; i++)
                    {
                        SqFile exHeader = exHeaders[i];

                        foreach (SqFile exDat in exHeader.ExDats)
                        {
                            Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                            if (exDat.LanguageCode != targetLangCode) continue;

                            string exDatOutDir = Path.Combine(outputDir, exDat.Dir);
                            if (!Directory.Exists(exDatOutDir)) continue;

                            string exDatOutPath = Path.Combine(exDatOutDir, sourceLangCode);
                            if (!File.Exists(exDatOutPath)) continue;

                            JObject[] jChunks = null;

                            using (StreamReader sr = new StreamReader(exDatOutPath))
                            {
                                jChunks = JArray.Parse(sr.ReadToEnd()).Select(j => (JObject)j).ToArray();
                            }

                            if (jChunks.Length == 0) continue;

                            // load string key based mapper.
                            // string key mapper -> field key 0 should be text-only field with all-capital constant (i.e. TEXT_XXX_NNN_SYSTEM_NNN_NN)
                            Dictionary<string, JObject> mapper = new Dictionary<string, JObject>();

                            foreach (JObject jChunk in jChunks)
                            {
                                ExDChunk chunk = new ExDChunk();
                                chunk.LoadJObject(jChunk);
                                // string key mapper should have string key on field 0 and text content on field 4.
                                if (!chunk.Fields.ContainsKey(0) || !chunk.Fields.ContainsKey(4) || chunk.Fields.Count != 2) continue;

                                JObject[] jFields = jChunk["Fields"].Select(j => (JObject)j).ToArray();
                                foreach (JObject jField in jFields)
                                {
                                    if ((ushort)jField["FieldKey"] != 0) continue;

                                    JObject[] jEntries = jField["FieldValue"].Select(j => (JObject)j).ToArray();

                                    if (jEntries.Length != 1) continue;
                                    if ((string)jEntries[0]["EntryType"] != "text") continue;

                                    string stringKey = (string)jEntries[0]["EntryValue"];
                                    if (stringKey.ToUpper() != stringKey) continue;

                                    // add the key and chunk to the mapper if it looks like string key mapped table.
                                    if (mapper.ContainsKey(stringKey)) continue;
                                    mapper.Add(stringKey, jChunk);
                                }
                            }

                            // if all rows in the table are string mapped
                            if (mapper.Count == jChunks.Length)
                            {
                                foreach (int chunkKey in exDat.Chunks.Keys)
                                {
                                    // find jobject with the same string key.
                                    string stringKey = new UTF8Encoding(false).GetString(exDat.Chunks[chunkKey].Fields[0]);
                                    if (!mapper.ContainsKey(stringKey)) continue;
                                    exDat.Chunks[chunkKey].LoadJObject(mapper[stringKey]);

                                    // preserve the original key.
                                    exDat.Chunks[chunkKey].Key = chunkKey;
                                }

                                string newExDatOutPath = Path.Combine(exDatOutDir, exDat.LanguageCode);

                                using (StreamWriter sw = new StreamWriter(newExDatOutPath, false))
                                {
                                    JArray jArray = new JArray();

                                    foreach (ExDChunk chunk in exDat.Chunks.Values)
                                    {
                                        jArray.Add(chunk.GetJObject());
                                    }

                                    sw.Write(jArray.ToString());
                                }
                            }
                            else
                            {
                                string tempPath = Path.Combine(outputDir, "unmapped");
                                using (StreamWriter sw = new StreamWriter(tempPath, true))
                                {
                                    sw.WriteLine(exDat.Dir + "/" + exDat.Name);
                                }
                            }
                        }
                    }
                    break;
            }
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

        static void CreateNewDat(string origDatPath, string newDatPath)
        {
            byte[] dat = File.ReadAllBytes(origDatPath);
            byte[] header = new byte[0x800];
            Array.Copy(dat, 0, header, 0, 0x800);
            Array.Copy(BitConverter.GetBytes(0x2), 0, header, 0x400 + 0x10, 0x4);
            File.WriteAllBytes(newDatPath, header);
        }

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
