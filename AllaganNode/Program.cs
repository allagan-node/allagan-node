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
                    sqFile.DatPath = datPath;

                    br.ReadInt32();

                    if (!sqFiles.ContainsKey(sqFile.DirectoryKey)) sqFiles.Add(sqFile.DirectoryKey, new Dictionary<uint, SqFile>());
                    sqFiles[sqFile.DirectoryKey].Add(sqFile.Key, sqFile);

                    Report(string.Format("{0} / {1}: {2}", i, fileCount, sqFile.Key));
                }
            }
            
            // find root file that lists all ExHs in 0a0000.
            // root file encoding is simple ASCII.
            SqFile rootFile = sqFiles[Hash.Compute("exd")][Hash.Compute("root.exl")];
            List<string> headerNames = new List<string>();

            using (MemoryStream ms = new MemoryStream(rootFile.ReadData()))
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

            List<ExHFile> exHeaderList = new List<ExHFile>();

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

                ExHFile exHFile = new ExHFile();
                exHFile.Copy(sqFile);
                exHFile.Name = headerName + ".exh";
                exHFile.Dir = headerDir;
                exHFile.HeaderName = headerName;
                exHFile.ReadExH();

                // only add ExHs with supported variant and string columns.
                if (exHFile.Variant == 1 && exHFile.Columns != null && exHFile.Columns.Length > 0)
                {
                    exHeaderList.Add(exHFile);
                }
            }

            ExHFile[] exHeaders = exHeaderList.ToArray();

            // for all ExHs, decode child ExDs and link them to ExH.
            for (int i = 0; i < exHeaders.Length; i++)
            {
                Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exHeaders[i].Name));

                ExHFile exHeader = exHeaders[i];

                foreach (ExHLanguage lang in exHeader.Languages)
                {
                    foreach (ExHRange range in exHeader.Ranges)
                    {
                        string datName = string.Format("{0}_{1}_{2}.exd", exHeader.HeaderName, range.Start, lang.Code);

                        uint directoryKey = Hash.Compute(exHeader.Dir);
                        uint key = Hash.Compute(datName);

                        if (!sqFiles.ContainsKey(directoryKey)) continue;
                        if (!sqFiles[directoryKey].ContainsKey(key)) continue;

                        ExDFile exDat = new ExDFile();
                        exDat.Copy(sqFiles[directoryKey][key]);
                        
                        exDat.Name = datName;
                        exDat.Dir = exHeader.Dir;

                        exDat.HeaderName = exHeader.HeaderName;
                        exDat.PhysicalDir = string.Format("{0}/{1}/{2}", exHeader.Dir, exHeader.HeaderName, range.Start);
                        exDat.LanguageCode = lang.Code;

                        exDat.ExHeader = exHeader;
                        
                        exDat.ReadExD();
                        exHeader.ExDats.Add(exDat);
                    }
                }
            }

            Report(string.Empty);
            Console.Write("Enter an option (0 - extract, 1 - apply translations, 2 - repackage): ");
            switch (Console.ReadLine().ToLower())
            {
                case "0":
                    ExtractExDs(exHeaders, outputDir);
                    break;

                case "1":
                    ApplyTranslations(exHeaders, outputDir, baseDir);
                    break;

                case "2":
                    RepackExDs(exHeaders, outputDir, indexPath, datPath);
                    break;

                    // this hidden option swaps language codes.
                    // it tries to map entries based on string key if available. if not, it maps based on chunk keys.
                case "swap":
                    SwapCodes(exHeaders, outputDir);
                    break;

                    // this hidden option is for translators.
                    // this will compress all available translations that are written in exd.
                case "compress":
                    CompressTranslations(exHeaders, outputDir);
                    break;

                    // this hidden option is for translators.
                    // this will extract translations from compressed format and place them in exd directory in editable format.
                case "decompress":
                    ExtractTranslations(exHeaders, outputDir, baseDir);
                    break;

                    // dev option to find certain string from original input dat...
                case "search":
                    string languageCode = Console.ReadLine();
                    string keyword = Console.ReadLine();

                    for (int i = 0; i < exHeaders.Length; i++)
                    {
                        foreach (ExDFile exDat in exHeaders[i].ExDats)
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
                                        Console.WriteLine();
                                        Console.WriteLine(exDat.PhysicalDir + "/" + exDat.Name);
                                    }
                                }
                            }
                        }
                    }

                    Console.WriteLine();
                    Console.WriteLine("DONE");
                    Console.ReadLine();
                    break;

                    // dev option for mapping CompleteJournal...
                case "map_journal":
                    // mapping quest titles...
                    Dictionary<int, string> englishQuest = new Dictionary<int, string>();
                    Dictionary<int, string> koreanQuest = new Dictionary<int, string>();

                    for (int i = 0; i < exHeaders.Length; i++)
                    {
                        foreach (ExDFile exDat in exHeaders[i].ExDats)
                        {
                            Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                            if (exDat.Dir.ToLower() != "exd" || exDat.HeaderName.ToLower() != "quest") continue;
                            if (exDat.LanguageCode != "en") continue;

                            foreach (ExDChunk chunk in exDat.Chunks.Values)
                            {
                                if (englishQuest.ContainsKey(chunk.Key)) continue;
                                if (chunk.Fields.Count == 0) continue;
                                if (!chunk.Fields.ContainsKey(0)) continue;

                                JObject jChunk = chunk.GetJObject();
                                JObject jField = (JObject)jChunk["Fields"].First(j => (ushort)j["FieldKey"] == 0);
                                JArray jEntries = (JArray)jField["FieldValue"];

                                if (jEntries.Count == 0) continue;

                                englishQuest.Add(chunk.Key, jEntries.ToString());
                            }

                            string exDatOutDir = Path.Combine(outputDir, exDat.PhysicalDir);
                            if (!Directory.Exists(exDatOutDir)) continue;

                            string exDatKoPath = Path.Combine(exDatOutDir, "ko");
                            if (!File.Exists(exDatKoPath)) continue;

                            JObject[] jChunks;

                            using (StreamReader sr = new StreamReader(exDatKoPath))
                            {
                                jChunks = JArray.Parse(sr.ReadToEnd()).Select(j => (JObject)j).ToArray();
                            }

                            foreach (JObject jChunk in jChunks)
                            {
                                ExDChunk chunk = new ExDChunk();
                                chunk.LoadJObject(jChunk);

                                if (koreanQuest.ContainsKey(chunk.Key)) continue;
                                if (chunk.Fields.Count == 0) continue;
                                if (!chunk.Fields.ContainsKey(0)) continue;

                                JObject jField = (JObject)jChunk["Fields"].First(j => (ushort)j["FieldKey"] == 0);
                                JArray jEntries = (JArray)jField["FieldValue"];

                                if (jEntries.Count == 0) continue;

                                koreanQuest.Add(chunk.Key, jEntries.ToString());
                            }
                        }
                    }

                    Dictionary<string, string> englishToKorean = new Dictionary<string, string>();

                    foreach (int key in englishQuest.Keys)
                    {
                        if (!koreanQuest.ContainsKey(key)) continue;
                        if (englishToKorean.ContainsKey(englishQuest[key])) continue;

                        englishToKorean.Add(englishQuest[key], koreanQuest[key]);
                    }

                    // mapping content finder titles...
                    Dictionary<string, string> englishContents = new Dictionary<string, string>();
                    Dictionary<string, string> koreanContents = new Dictionary<string, string>();

                    for (int i = 0; i < exHeaders.Length; i++)
                    {
                        foreach (ExDFile exDat in exHeaders[i].ExDats)
                        {
                            Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                            if (exDat.Dir.ToLower() != "exd" || exDat.HeaderName.ToLower() != "contentfindercondition") continue;
                            if (exDat.LanguageCode != "en") continue;

                            foreach (ExDChunk chunk in exDat.Chunks.Values)
                            {
                                if (chunk.Fields.Count != 2) continue;
                                if (!chunk.Fields.ContainsKey(0) || !chunk.Fields.ContainsKey(44)) continue;

                                JObject jChunk = chunk.GetJObject();
                                JArray jFields = (JArray)jChunk["Fields"];
                                JObject jKeyFIeld = (JObject)jFields.First(j => (ushort)j["FieldKey"] == 44);
                                JArray jKeyEntries = (JArray)jKeyFIeld["FieldValue"];

                                if (jKeyEntries.Count != 1) continue;
                                if ((string)jKeyEntries[0]["EntryType"] != "text") continue;

                                string fieldKey = (string)jKeyEntries[0]["EntryValue"];

                                if (englishContents.ContainsKey(fieldKey)) continue;

                                JObject jValueField = (JObject)jFields.First(j => (ushort)j["FieldKey"] == 0);
                                JArray jValueEntries = (JArray)jValueField["FieldValue"];

                                if (jValueEntries.Count == 0) continue;

                                englishContents.Add(fieldKey, jValueEntries.ToString());
                            }

                            string exDatOutDir = Path.Combine(outputDir, exDat.PhysicalDir);
                            if (!Directory.Exists(exDatOutDir)) continue;

                            string exDatKoPath = Path.Combine(exDatOutDir, "ko");
                            if (!File.Exists(exDatKoPath)) continue;

                            JObject[] jChunks;

                            using (StreamReader sr = new StreamReader(exDatKoPath))
                            {
                                jChunks = JArray.Parse(sr.ReadToEnd()).Select(j => (JObject)j).ToArray();
                            }

                            foreach (JObject jChunk in jChunks)
                            {
                                ExDChunk chunk = new ExDChunk();
                                chunk.LoadJObject(jChunk);

                                if (chunk.Fields.Count != 2) continue;
                                if (!chunk.Fields.ContainsKey(0) || !chunk.Fields.ContainsKey(44)) continue;

                                JArray jFields = (JArray)jChunk["Fields"];
                                JObject jKeyField = (JObject)jFields.First(j => (ushort)j["FieldKey"] == 44);
                                JArray jKeyEntries = (JArray)jKeyField["FieldValue"];

                                if (jKeyEntries.Count != 1) continue;
                                if ((string)jKeyEntries[0]["EntryType"] != "text") continue;

                                string fieldKey = (string)jKeyEntries[0]["EntryValue"];

                                if (koreanContents.ContainsKey(fieldKey)) continue;

                                JObject jValueField = (JObject)jFields.First(j => (ushort)j["FieldKey"] == 0);
                                JArray jValueEntries = (JArray)jValueField["FieldValue"];

                                if (jValueEntries.Count == 0) continue;

                                koreanContents.Add(fieldKey, jValueEntries.ToString());
                            }
                        }
                    }

                    foreach (string key in englishContents.Keys)
                    {
                        if (!koreanContents.ContainsKey(key)) continue;
                        if (englishToKorean.ContainsKey(englishContents[key])) continue;

                        englishToKorean.Add(englishContents[key], koreanContents[key]);
                    }

                    for (int i = 0; i < exHeaders.Length; i++)
                    {
                        foreach (ExDFile exDat in exHeaders[i].ExDats)
                        {
                            Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                            if (exDat.Dir.ToLower() != "exd" || exDat.HeaderName.ToLower() != "completejournal") continue;
                            if (exDat.LanguageCode != "en") continue;

                            foreach (ExDChunk chunk in exDat.Chunks.Values)
                            {
                                if (chunk.Fields.Count != 1) continue;
                                if (!chunk.Fields.ContainsKey(0)) continue;

                                JObject jChunk = chunk.GetJObject();
                                JArray jFieldArray = (JArray)jChunk["Fields"];
                                JObject jField = (JObject)jFieldArray[0];
                                if ((ushort)jField["FieldKey"] != 0) continue;

                                JArray jEntries = (JArray)jField["FieldValue"];
                                if (jEntries.Count == 0) continue;

                                if (!englishToKorean.ContainsKey(jEntries.ToString())) continue;
                                jField["FieldValue"] = JArray.Parse(englishToKorean[jEntries.ToString()]);

                                chunk.LoadJObject(jChunk);
                            }

                            exDat.LanguageCode = "trans";
                            exDat.ExtractExD(outputDir);
                        }
                    }

                    break;

                    // testing
                case "test":
                    for (int i = 0; i < exHeaders.Length; i++)
                    {
                        foreach (ExDFile exDat in exHeaders[i].ExDats)
                        {
                            Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                            string exDatOutDir = Path.Combine(outputDir, exDat.PhysicalDir);
                            if (!Directory.Exists(exDatOutDir)) continue;

                            string exDatOutPath = Path.Combine(exDatOutDir, exDat.LanguageCode);
                            string exDatTestPath = exDatOutPath + ".test";

                            using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(exDatOutPath)))
                            using (StreamReader sr = new StreamReader(ms))
                            using (MemoryStream ms2 = new MemoryStream(File.ReadAllBytes(exDatTestPath)))
                            using (StreamReader sr2 = new StreamReader(ms2))
                            {
                                while (sr.Peek() != -1)
                                {
                                    if (sr2.Peek() == -1) throw new Exception();

                                    if (sr.ReadLine() != sr2.ReadLine()) throw new Exception();
                                }

                                if (sr2.Peek() != -1) throw new Exception();
                            }
                        }
                    }

                    Console.WriteLine();
                    Console.WriteLine("DONE");
                    Console.ReadLine();
                    break;
            }
        }

        static void ExtractExDs(ExHFile[] exHeaders, string outputDir)
        {
            for (int i = 0; i < exHeaders.Length; i++)
            {
                foreach (ExDFile exDat in exHeaders[i].ExDats)
                {
                    Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                    exDat.ExtractExD(outputDir);
                }
            }
        }

        static void ApplyTranslations(ExHFile[] exHeaders, string outputDir, string baseDir)
        {
            string translationsPath = Path.Combine(baseDir, "input", "translations");
            if (!File.Exists(translationsPath)) return;

            Console.Write("Enter lang codes to apply translations (separated by comma): ");
            string[] targetLangCodes = Console.ReadLine().Split(',');

            JObject translations = DecompressTranslations(translationsPath);

            for (int i = 0; i < exHeaders.Length; i++)
            {
                foreach (ExDFile exDat in exHeaders[i].ExDats)
                {
                    Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                    if (!targetLangCodes.Contains(exDat.LanguageCode)) continue;
                    if (!translations.ContainsKey(exDat.PhysicalDir)) continue;

                    JObject[] translationChunks = DecodeTranslations((byte[])translations[exDat.PhysicalDir]).Select(j => (JObject)j).ToArray();
                    foreach (JObject translationChunk in translationChunks)
                    {
                        int chunkKey = (int)translationChunk["Key"];
                        if (!exDat.Chunks.ContainsKey(chunkKey)) continue;

                        exDat.Chunks[chunkKey].LoadJObject(translationChunk);
                    }

                    exDat.ExtractExD(outputDir);
                }
            }
        }

        static void RepackExDs(ExHFile[] exHeaders, string outputDir, string indexPath, string datPath)
        {
            Console.Write("Enter lang codes to repack (separated by comma): ");
            string[] targetLangCodes = Console.ReadLine().Split(',');

            string outputIndexPath = Path.Combine(outputDir, Path.GetFileName(indexPath));
            File.Copy(indexPath, outputIndexPath, true);
            byte[] index = File.ReadAllBytes(outputIndexPath);

            byte[] origDat = File.ReadAllBytes(datPath);

            string outputNewDatPath = Path.Combine(outputDir, "0a0000.win32.dat1");
            CreateNewDat(datPath, outputNewDatPath);
            
            for (int i = 0; i < exHeaders.Length; i++)
            {
                foreach (ExDFile exDat in exHeaders[i].ExDats)
                {
                    Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                    if (!targetLangCodes.Contains(exDat.LanguageCode)) continue;

                    string exDatOutDir = Path.Combine(outputDir, exDat.PhysicalDir);
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

                    byte[] buffer = exDat.RepackExD();
                    buffer = exDat.RepackData(origDat, buffer);

                    exDat.UpdateOffset((int)new FileInfo(outputNewDatPath).Length, 1, index);

                    using (FileStream fs = new FileStream(outputNewDatPath, FileMode.Append, FileAccess.Write))
                    using (BinaryWriter bw = new BinaryWriter(fs))
                    {
                        bw.Write(buffer);
                    }
                }
            }

            File.WriteAllBytes(outputIndexPath, index);

            UpdateDatHash(outputNewDatPath);
        }

        static void SwapCodes(ExHFile[] exHeaders, string outputDir)
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
                foreach (ExDFile exDat in exHeaders[i].ExDats)
                {
                    Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                    if (exDat.LanguageCode != targetLangCode) continue;

                    string exDatOutDir = Path.Combine(outputDir, exDat.PhysicalDir);
                    if (!Directory.Exists(exDatOutDir)) continue;

                    string exDatSourcePath = Path.Combine(exDatOutDir, sourceLangCode);

                    // source doesn't exist, which means this is probably newer file.
                    // need custom translations.
                    if (!File.Exists(exDatSourcePath))
                    {
                        notTranslatedArray.Add(exDat.PhysicalDir);
                        continue;
                    }

                    JObject[] jChunks = null;

                    using (StreamReader sr = new StreamReader(exDatSourcePath))
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
                        stringKeyMappedArray.Add(exDat.PhysicalDir);
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
                            emptyArray.Add(exDat.PhysicalDir);
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
                                notTranslatedArray.Add(exDat.PhysicalDir);
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
                                chunkKeyMappedArray.Add(exDat.PhysicalDir);
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

        static void CompressTranslations(ExHFile[] exHeaders, string outputDir)
        {
            JObject translations = new JObject();

            for (int i = 0; i < exHeaders.Length; i++)
            {
                foreach (ExDFile exDat in exHeaders[i].ExDats)
                {
                    Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                    if (translations.ContainsKey(exDat.PhysicalDir)) continue;

                    string exDatOutDir = Path.Combine(outputDir, exDat.PhysicalDir);
                    if (!Directory.Exists(exDatOutDir)) continue;

                    string exDatOutPath = Path.Combine(exDatOutDir, "trans");
                    if (!File.Exists(exDatOutPath)) continue;

                    translations.Add(exDat.PhysicalDir, new JValue(File.ReadAllBytes(exDatOutPath)));

                    Console.WriteLine();
                    Console.WriteLine(exDat.PhysicalDir);
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

            Console.WriteLine();
            Console.WriteLine("DONE");
            Console.ReadLine();
        }

        static void ExtractTranslations(ExHFile[] exHeaders, string outputDir, string baseDir)
        {
            string translationsPath = Path.Combine(baseDir, "input", "translations");
            if (!File.Exists(translationsPath)) return;

            JObject translations = DecompressTranslations(translationsPath);

            for (int i = 0; i < exHeaders.Length; i++)
            {
                foreach (ExDFile exDat in exHeaders[i].ExDats)
                {
                    Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                    if (!translations.ContainsKey(exDat.PhysicalDir)) continue;

                    string exDatOutDir = Path.Combine(outputDir, exDat.PhysicalDir);
                    if (!Directory.Exists(exDatOutDir)) Directory.CreateDirectory(exDatOutDir);

                    string transOutPath = Path.Combine(exDatOutDir, "trans");
                    if (File.Exists(transOutPath)) File.Delete(transOutPath);

                    File.WriteAllBytes(transOutPath, (byte[])translations[exDat.PhysicalDir]);
                }
            }
        }

        static JObject DecompressTranslations(string translationsPath)
        {
            if (!File.Exists(translationsPath)) return null;

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
            
            return translations;
        }

        static JArray DecodeTranslations(byte[] translations)
        {
            string decoded = string.Empty;

            using (MemoryStream ms = new MemoryStream(translations))
            using (StreamReader sr = new StreamReader(ms))
            {
                decoded = sr.ReadToEnd();
            }

            return JArray.Parse(decoded);
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
