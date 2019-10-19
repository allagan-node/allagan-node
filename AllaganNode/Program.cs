using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

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
            Console.Write("Enter an option (0 - extract, 1 - apply custom texts, 2 - repackage): ");
            switch (int.Parse(Console.ReadLine()))
            {
                case 0:
                    ExtractExDs(exHeaders, outputDir);
                    break;

                case 1:
                    break;

                case 2:
                    RepackExDs(exHeaders, outputDir, indexPath, datPath);
                    break;

                    // this hidden option swaps language codes.
                    // it tries to map entries based on string key if available. if not, it maps based on chunk keys.
                case 1234:
                    SwapCodes(exHeaders, outputDir);
                    break;

                    // this hidden option is for translators.
                    // this will compress all available translations that are written in exd.
                case 91:
                    CompressTranslations(exHeaders, outputDir);
                    break;

                    // this hidden option is for translators.
                    // this will extract translations from compressed format and place them in exd directory in editable format.
                case 92:
                    ExtractTranslations(exHeaders, outputDir, baseDir);
                    break;

                    // dev option to find certain string from original input dat...
                case 93:
                    string languageCode = Console.ReadLine();
                    string keyword = Console.ReadLine();

                    for (int i = 0; i < exHeaders.Length; i++)
                    {
                        SqFile exHeader = exHeaders[i];

                        foreach (SqFile exDat in exHeader.ExDats)
                        {
                            Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                            if (exDat.LanguageCode != languageCode) continue;

                            foreach (ExDChunk chunk in exDat.Chunks.Values)
                            {
                                foreach (byte[] field in chunk.Fields.Values)
                                {
                                    string test = new UTF8Encoding(false).GetString(field);
                                    if (test.Contains(keyword))
                                    {
                                        Console.WriteLine(exDat.Dir + "/" + exDat.Name);
                                    }
                                }
                            }
                        }
                    }

                    Console.WriteLine("DONE");
                    Console.ReadLine();
                    break;
            }
        }

        static void ExtractExDs(SqFile[] exHeaders, string outputDir)
        {
            for (int i = 0; i < exHeaders.Length; i++)
            {
                SqFile exHeader = exHeaders[i];

                Report(string.Format("{0} / {1}: {2}", i.ToString(), exHeaders.Length.ToString(), exHeader.Name));

                foreach (SqFile exDat in exHeader.ExDats)
                {
                    exDat.ExtractExD(outputDir);
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
        }

        static void RepackExDs(SqFile[] exHeaders, string outputDir, string indexPath, string datPath)
        {
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
        }

        static void SwapCodes(SqFile[] exHeaders, string outputDir)
        {
            // swap two lang table based on string key mapping (if available) or chunk key mapping.
            Console.Write("Enter source lang code: ");
            string sourceLangCode = Console.ReadLine();
            Console.Write("Enter target lang code: ");
            string targetLangCode = Console.ReadLine();

            // placeholder files.
            string emptyPath = Path.Combine(outputDir, "empty");
            if (File.Exists(emptyPath)) File.Delete(emptyPath);
            JArray emptyArray = new JArray();

            // these files are mapped by string key and is pretty much accurate.
            string stringKeyMappedPath = Path.Combine(outputDir, "string_key_mapped");
            if (File.Exists(stringKeyMappedPath)) File.Delete(stringKeyMappedPath);
            JArray stringKeyMappedArray = new JArray();

            // these files are mapped by chunk key and could be wrong.
            string chunkKeyMappedPath = Path.Combine(outputDir, "chunk_key_mapped");
            if (File.Exists(chunkKeyMappedPath)) File.Delete(chunkKeyMappedPath);
            JArray chunkKeyMappedArray = new JArray();

            // these files do not exist in source lang code but exist in target lang code.
            // need custom translations.
            string notTranslatedPath = Path.Combine(outputDir, "not_translated");
            if (File.Exists(notTranslatedPath)) File.Delete(notTranslatedPath);
            JArray notTranslatedArray = new JArray();

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
                    // source doesn't exist, which means this is probably newer file.
                    // need custom translations.
                    if (!File.Exists(exDatOutPath))
                    {
                        notTranslatedArray.Add(exDat.Dir);
                        continue;
                    }

                    JObject[] jChunks = null;

                    using (StreamReader sr = new StreamReader(exDatOutPath))
                    {
                        jChunks = JArray.Parse(sr.ReadToEnd()).Select(j => (JObject)j).ToArray();
                    }

                    if (jChunks.Length == 0) continue;

                    // load string key based mapper.
                    // string key mapper -> field key 0 should be text-only field with all-capital constant (i.e. TEXT_XXX_NNN_SYSTEM_NNN_NN)
                    Dictionary<string, JObject> mapper = new Dictionary<string, JObject>();
                    Regex stringKeyRegex = new Regex("^[A-Za-z0-9_]+$");
                    List<ExDChunk> exDChunks = new List<ExDChunk>();

                    foreach (JObject jChunk in jChunks)
                    {
                        ExDChunk chunk = new ExDChunk();
                        chunk.LoadJObject(jChunk);
                        exDChunks.Add(chunk);

                        // string key mapper should have string key on field 0 and text content on field 4.
                        if (!chunk.Fields.ContainsKey(0) || !chunk.Fields.ContainsKey(4) || chunk.Fields.Count != 2) continue;

                        JObject stringKeyField = jChunk["Fields"].Select(j => (JObject)j).First(j => (ushort)j["FieldKey"] == 0);

                        // string key should not have any tags and consist of only one text type entry.
                        JObject[] jEntries = stringKeyField["FieldValue"].Select(j => (JObject)j).ToArray();
                        if (jEntries.Length != 1) continue;
                        if ((string)jEntries[0]["EntryType"] != "text") continue;

                        // additional validation for string key.
                        string stringKey = (string)jEntries[0]["EntryValue"];
                        if (!stringKeyRegex.IsMatch(stringKey)) continue;
                        if (mapper.ContainsKey(stringKey)) continue;

                        mapper.Add(stringKey, jChunk);
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

                        exDat.ExtractExD(outputDir);

                        // indicate that this file was mapped by string keys.
                        stringKeyMappedArray.Add(exDat.Dir);
                    }
                    else
                    {
                        // let's see if target is empty...
                        bool isEmpty = true;

                        foreach (ExDChunk chunk in exDat.Chunks.Values)
                        {
                            foreach (byte[] field in chunk.Fields.Values)
                            {
                                if (field.Length > 0) isEmpty = false;
                            }
                        }

                        // if target is empty, we don't care about the source.
                        // record it as empty.
                        if (isEmpty)
                        {
                            emptyArray.Add(exDat.Dir);
                        }
                        else
                        {
                            // check whether source is also empty.
                            isEmpty = true;

                            foreach (ExDChunk chunk in exDChunks)
                            {
                                foreach (byte[] field in chunk.Fields.Values)
                                {
                                    if (field.Length > 0) isEmpty = false;
                                }
                            }

                            // if source is empty, this means no source data is present for this target file.
                            // add in not translated list.
                            if (isEmpty)
                            {
                                notTranslatedArray.Add(exDat.Dir);
                            }
                            // both are not empty, let's proceed with chunk key mapping...
                            else
                            {
                                foreach (ExDChunk chunk in exDChunks)
                                {
                                    if (!exDat.Chunks.ContainsKey(chunk.Key)) continue;

                                    foreach (ushort fieldKey in chunk.Fields.Keys)
                                    {
                                        // if source field is empty, don't bother changing.
                                        if (chunk.Fields[fieldKey].Length == 0) continue;
                                        if (!exDat.Chunks[chunk.Key].Fields.ContainsKey(fieldKey)) continue;
                                        exDat.Chunks[chunk.Key].Fields[fieldKey] = chunk.Fields[fieldKey];
                                    }
                                }

                                exDat.ExtractExD(outputDir);

                                // indicate that this file was mapped by chunk keys.
                                chunkKeyMappedArray.Add(exDat.Dir);
                            }
                        }
                    }
                }
            }

            // record all arrays.
            using (StreamWriter sw = new StreamWriter(emptyPath, false))
            {
                sw.Write(emptyArray.ToString());
            }

            using (StreamWriter sw = new StreamWriter(stringKeyMappedPath, false))
            {
                sw.Write(stringKeyMappedArray.ToString());
            }

            using (StreamWriter sw = new StreamWriter(chunkKeyMappedPath, false))
            {
                sw.Write(chunkKeyMappedArray.ToString());
            }

            using (StreamWriter sw = new StreamWriter(notTranslatedPath, false))
            {
                sw.Write(notTranslatedArray.ToString());
            }
        }

        static void CompressTranslations(SqFile[] exHeaders, string outputDir)
        {
            JObject translations = new JObject();

            for (int i = 0; i < exHeaders.Length; i++)
            {
                SqFile exHeader = exHeaders[i];

                foreach (SqFile exDat in exHeader.ExDats)
                {
                    Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                    if (translations.ContainsKey(exDat.Dir)) continue;

                    string exDatOutDir = Path.Combine(outputDir, exDat.Dir);
                    if (!Directory.Exists(exDatOutDir)) continue;

                    string exDatOutPath = Path.Combine(exDatOutDir, "trans");
                    if (!File.Exists(exDatOutPath)) continue;

                    translations.Add(exDat.Dir, new JValue(File.ReadAllBytes(exDatOutPath)));

                    Console.WriteLine(exDat.Dir);
                }
            }

            byte[] bTranslations = new UTF8Encoding().GetBytes(translations.ToString());

            using (MemoryStream ms = new MemoryStream())
            {
                using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Compress))
                using (MemoryStream _ms = new MemoryStream(bTranslations))
                {
                    _ms.CopyTo(ds);
                }

                string outputPath = Path.Combine(outputDir, "translations");
                if (File.Exists(outputPath)) File.Delete(outputPath);
                File.WriteAllBytes(outputPath, ms.ToArray());
            }

            Console.WriteLine("DONE");
            Console.ReadLine();
        }

        static void ExtractTranslations(SqFile[] exHeaders, string outputDir, string baseDir)
        {
            string translationsPath = Path.Combine(baseDir, "input", "translations");
            if (!File.Exists(translationsPath)) return;

            JObject translations;

            using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(translationsPath)))
            using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
            {
                using (MemoryStream _ms = new MemoryStream())
                {
                    ds.CopyTo(_ms);
                    translations = JObject.Parse(new UTF8Encoding().GetString(_ms.ToArray()));
                }
            }

            for (int i = 0; i < exHeaders.Length; i++)
            {
                SqFile exHeader = exHeaders[i];

                foreach (SqFile exDat in exHeader.ExDats)
                {
                    Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                    if (!translations.ContainsKey(exDat.Dir)) continue;

                    string exDatOutDir = Path.Combine(outputDir, exDat.Dir);
                    if (!Directory.Exists(exDatOutDir)) Directory.CreateDirectory(exDatOutDir);

                    string transOutPath = Path.Combine(exDatOutDir, "trans");
                    if (File.Exists(transOutPath)) File.Delete(transOutPath);

                    File.WriteAllBytes(transOutPath, (byte[])translations[exDat.Dir]);
                }
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
