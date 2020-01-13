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
    internal class Program
    {
        private static string previousLine;

        private static void Main(string[] args)
        {
            Console.WriteLine("AllaganNode v{0}", Assembly.GetExecutingAssembly().GetName().Version);

            // TODO: make base path selectable.
            var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var indexPath = Path.Combine(baseDir, "input", "0a0000.win32.index");
            var datPath = Path.Combine(baseDir, "input", "0a0000.win32.dat0");
            var outputDir = Path.Combine(baseDir, "output");
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            var sqFiles = new Dictionary<uint, Dictionary<uint, SqFile>>();

            // Read index and cache all available sqfiles.
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

            // find root file that lists all ExHs in 0a0000.
            // root file encoding is simple ASCII.
            var rootFile = sqFiles[Hash.Compute("exd")][Hash.Compute("root.exl")];
            var headerNames = new List<string>();

            using (var ms = new MemoryStream(rootFile.ReadData()))
            using (var sr = new StreamReader(ms, Encoding.ASCII))
            using (var sw = new StreamWriter(Path.Combine(outputDir, "root.exl")))
            {
                sr.ReadLine();

                while (!sr.EndOfStream)
                {
                    var line = sr.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    Report(line);
                    sw.WriteLine(line);

                    var split = line.Split(',');
                    if (split.Length != 2) continue;

                    headerNames.Add(split[0]);
                }
            }

            var exHeaderList = new List<ExHFile>();

            // for all ExHs, decode the cached data buffer as ExH.
            for (var i = 0; i < headerNames.Count; i++)
            {
                Report(string.Format("{0} / {1}: {2}", i, headerNames.Count, headerNames[i]));

                var headerDir = string.Empty;
                var headerName = headerNames[i];

                if (headerName.Contains("/"))
                {
                    headerDir = string.Format("/{0}", headerName.Substring(0, headerName.LastIndexOf("/")));
                    headerName = headerName.Substring(headerName.LastIndexOf("/") + 1);
                }

                headerDir = string.Format("exd{0}", headerDir);

                var sqFile = sqFiles[Hash.Compute(headerDir)][Hash.Compute(string.Format("{0}.exh", headerName))];

                var exHFile = new ExHFile();
                exHFile.Copy(sqFile);
                exHFile.Name = headerName + ".exh";
                exHFile.Dir = headerDir;
                exHFile.HeaderName = headerName;
                exHFile.ReadExH();

                // only add ExHs with supported variant and string columns.
                if (exHFile.Variant == 1 && exHFile.Columns != null && exHFile.Columns.Length > 0)
                    exHeaderList.Add(exHFile);
            }

            var exHeaders = exHeaderList.ToArray();

            // for all ExHs, decode child ExDs and link them to ExH.
            for (var i = 0; i < exHeaders.Length; i++)
            {
                Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exHeaders[i].Name));

                var exHeader = exHeaders[i];

                foreach (var lang in exHeader.Languages)
                foreach (var range in exHeader.Ranges)
                {
                    var datName = string.Format("{0}_{1}_{2}.exd", exHeader.HeaderName, range.Start, lang.Code);

                    var directoryKey = Hash.Compute(exHeader.Dir);
                    var key = Hash.Compute(datName);

                    if (!sqFiles.ContainsKey(directoryKey)) continue;

                    if (!sqFiles[directoryKey].ContainsKey(key)) continue;

                    var exDat = new ExDFile();
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
                    var languageCode = Console.ReadLine();
                    var keyword = Console.ReadLine();

                    for (var i = 0; i < exHeaders.Length; i++)
                        foreach (var exDat in exHeaders[i].ExDats)
                        {
                            Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                            if (exDat.LanguageCode != languageCode) continue;

                            foreach (var chunk in exDat.Chunks.Values)
                            foreach (var field in chunk.Fields.Values)
                            {
                                var test = new UTF8Encoding(false).GetString(field);
                                if (test.Contains(keyword))
                                {
                                    Console.WriteLine();
                                    Console.WriteLine(exDat.PhysicalDir + "/" + exDat.Name);
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
                    var englishQuest = new Dictionary<int, string>();
                    var koreanQuest = new Dictionary<int, string>();

                    for (var i = 0; i < exHeaders.Length; i++)
                        foreach (var exDat in exHeaders[i].ExDats)
                        {
                            Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                            if (exDat.Dir.ToLower() != "exd" || exDat.HeaderName.ToLower() != "quest") continue;

                            if (exDat.LanguageCode != "en") continue;

                            foreach (var chunk in exDat.Chunks.Values)
                            {
                                if (englishQuest.ContainsKey(chunk.Key)) continue;

                                if (chunk.Fields.Count == 0) continue;

                                if (!chunk.Fields.ContainsKey(0)) continue;

                                var jChunk = chunk.GetJObject();
                                var jField = (JObject) jChunk["Fields"].First(j => (ushort) j["FieldKey"] == 0);
                                var jEntries = (JArray) jField["FieldValue"];

                                if (jEntries.Count == 0) continue;

                                englishQuest.Add(chunk.Key, jEntries.ToString());
                            }

                            var exDatOutDir = Path.Combine(outputDir, exDat.PhysicalDir);
                            if (!Directory.Exists(exDatOutDir)) continue;

                            var exDatKoPath = Path.Combine(exDatOutDir, "ko");
                            if (!File.Exists(exDatKoPath)) continue;

                            JObject[] jChunks;

                            using (var sr = new StreamReader(exDatKoPath))
                            {
                                jChunks = JArray.Parse(sr.ReadToEnd()).Select(j => (JObject) j).ToArray();
                            }

                            foreach (var jChunk in jChunks)
                            {
                                var chunk = new ExDChunk();
                                chunk.LoadJObject(jChunk);

                                if (koreanQuest.ContainsKey(chunk.Key)) continue;

                                if (chunk.Fields.Count == 0) continue;

                                if (!chunk.Fields.ContainsKey(0)) continue;

                                var jField = (JObject) jChunk["Fields"].First(j => (ushort) j["FieldKey"] == 0);
                                var jEntries = (JArray) jField["FieldValue"];

                                if (jEntries.Count == 0) continue;

                                koreanQuest.Add(chunk.Key, jEntries.ToString());
                            }
                        }

                    var englishToKorean = new Dictionary<string, string>();

                    foreach (var key in englishQuest.Keys)
                    {
                        if (!koreanQuest.ContainsKey(key)) continue;

                        if (englishToKorean.ContainsKey(englishQuest[key])) continue;

                        englishToKorean.Add(englishQuest[key], koreanQuest[key]);
                    }

                    // mapping content finder titles...
                    var englishContents = new Dictionary<string, string>();
                    var koreanContents = new Dictionary<string, string>();

                    for (var i = 0; i < exHeaders.Length; i++)
                        foreach (var exDat in exHeaders[i].ExDats)
                        {
                            Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                            if (exDat.Dir.ToLower() != "exd" || exDat.HeaderName.ToLower() != "contentfindercondition")
                                continue;

                            if (exDat.LanguageCode != "en") continue;

                            foreach (var chunk in exDat.Chunks.Values)
                            {
                                if (chunk.Fields.Count != 2) continue;

                                if (!chunk.Fields.ContainsKey(0) || !chunk.Fields.ContainsKey(44)) continue;

                                var jChunk = chunk.GetJObject();
                                var jFields = (JArray) jChunk["Fields"];
                                var jKeyFIeld = (JObject) jFields.First(j => (ushort) j["FieldKey"] == 44);
                                var jKeyEntries = (JArray) jKeyFIeld["FieldValue"];

                                if (jKeyEntries.Count != 1) continue;

                                if ((string) jKeyEntries[0]["EntryType"] != "text") continue;

                                var fieldKey = (string) jKeyEntries[0]["EntryValue"];

                                if (englishContents.ContainsKey(fieldKey)) continue;

                                var jValueField = (JObject) jFields.First(j => (ushort) j["FieldKey"] == 0);
                                var jValueEntries = (JArray) jValueField["FieldValue"];

                                if (jValueEntries.Count == 0) continue;

                                englishContents.Add(fieldKey, jValueEntries.ToString());
                            }

                            var exDatOutDir = Path.Combine(outputDir, exDat.PhysicalDir);
                            if (!Directory.Exists(exDatOutDir)) continue;

                            var exDatKoPath = Path.Combine(exDatOutDir, "ko");
                            if (!File.Exists(exDatKoPath)) continue;

                            JObject[] jChunks;

                            using (var sr = new StreamReader(exDatKoPath))
                            {
                                jChunks = JArray.Parse(sr.ReadToEnd()).Select(j => (JObject) j).ToArray();
                            }

                            foreach (var jChunk in jChunks)
                            {
                                var chunk = new ExDChunk();
                                chunk.LoadJObject(jChunk);

                                if (chunk.Fields.Count != 2) continue;

                                if (!chunk.Fields.ContainsKey(0) || !chunk.Fields.ContainsKey(44)) continue;

                                var jFields = (JArray) jChunk["Fields"];
                                var jKeyField = (JObject) jFields.First(j => (ushort) j["FieldKey"] == 44);
                                var jKeyEntries = (JArray) jKeyField["FieldValue"];

                                if (jKeyEntries.Count != 1) continue;

                                if ((string) jKeyEntries[0]["EntryType"] != "text") continue;

                                var fieldKey = (string) jKeyEntries[0]["EntryValue"];

                                if (koreanContents.ContainsKey(fieldKey)) continue;

                                var jValueField = (JObject) jFields.First(j => (ushort) j["FieldKey"] == 0);
                                var jValueEntries = (JArray) jValueField["FieldValue"];

                                if (jValueEntries.Count == 0) continue;

                                koreanContents.Add(fieldKey, jValueEntries.ToString());
                            }
                        }

                    foreach (var key in englishContents.Keys)
                    {
                        if (!koreanContents.ContainsKey(key)) continue;

                        if (englishToKorean.ContainsKey(englishContents[key])) continue;

                        englishToKorean.Add(englishContents[key], koreanContents[key]);
                    }

                    for (var i = 0; i < exHeaders.Length; i++)
                        foreach (var exDat in exHeaders[i].ExDats)
                        {
                            Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                            if (exDat.Dir.ToLower() != "exd" || exDat.HeaderName.ToLower() != "completejournal")
                                continue;

                            if (exDat.LanguageCode != "en") continue;

                            foreach (var chunk in exDat.Chunks.Values)
                            {
                                if (chunk.Fields.Count != 1) continue;

                                if (!chunk.Fields.ContainsKey(0)) continue;

                                var jChunk = chunk.GetJObject();
                                var jFieldArray = (JArray) jChunk["Fields"];
                                var jField = (JObject) jFieldArray[0];
                                if ((ushort) jField["FieldKey"] != 0) continue;

                                var jEntries = (JArray) jField["FieldValue"];
                                if (jEntries.Count == 0) continue;

                                if (!englishToKorean.ContainsKey(jEntries.ToString())) continue;

                                jField["FieldValue"] = JArray.Parse(englishToKorean[jEntries.ToString()]);

                                chunk.LoadJObject(jChunk);
                            }

                            exDat.LanguageCode = "trans";
                            exDat.ExtractExD(outputDir);
                        }

                    break;
            }
        }

        private static void ExtractExDs(ExHFile[] exHeaders, string outputDir)
        {
            for (var i = 0; i < exHeaders.Length; i++)
                foreach (var exDat in exHeaders[i].ExDats)
                {
                    Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                    exDat.ExtractExD(outputDir);
                }
        }

        private static void ApplyTranslations(ExHFile[] exHeaders, string outputDir, string baseDir)
        {
            var translationsPath = Path.Combine(baseDir, "input", "translations");
            if (!File.Exists(translationsPath)) return;

            Console.Write("Enter lang codes to apply translations (separated by comma): ");
            var targetLangCodes = Console.ReadLine().Split(',');

            var translations = DecompressTranslations(translationsPath);

            for (var i = 0; i < exHeaders.Length; i++)
                foreach (var exDat in exHeaders[i].ExDats)
                {
                    Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                    if (!targetLangCodes.Contains(exDat.LanguageCode)) continue;

                    if (!translations.ContainsKey(exDat.PhysicalDir)) continue;

                    var translationChunks = DecodeTranslations((byte[]) translations[exDat.PhysicalDir])
                        .Select(j => (JObject) j).ToArray();
                    foreach (var translationChunk in translationChunks)
                    {
                        var chunkKey = (int) translationChunk["Key"];
                        if (!exDat.Chunks.ContainsKey(chunkKey)) continue;

                        exDat.Chunks[chunkKey].LoadJObject(translationChunk);
                    }

                    exDat.ExtractExD(outputDir);
                }
        }

        private static void RepackExDs(ExHFile[] exHeaders, string outputDir, string indexPath, string datPath)
        {
            Console.Write("Enter lang codes to repack (separated by comma): ");
            var targetLangCodes = Console.ReadLine().Split(',');

            var outputIndexPath = Path.Combine(outputDir, Path.GetFileName(indexPath));
            File.Copy(indexPath, outputIndexPath, true);
            var index = File.ReadAllBytes(outputIndexPath);

            var origDat = File.ReadAllBytes(datPath);

            var outputNewDatPath = Path.Combine(outputDir, "0a0000.win32.dat1");
            CreateNewDat(datPath, outputNewDatPath);

            for (var i = 0; i < exHeaders.Length; i++)
                foreach (var exDat in exHeaders[i].ExDats)
                {
                    Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                    if (!targetLangCodes.Contains(exDat.LanguageCode)) continue;

                    var exDatOutDir = Path.Combine(outputDir, exDat.PhysicalDir);
                    if (!Directory.Exists(exDatOutDir)) continue;

                    var exDatOutPath = Path.Combine(exDatOutDir, exDat.LanguageCode);
                    if (!File.Exists(exDatOutPath)) continue;

                    JObject[] jChunks = null;

                    using (var sr = new StreamReader(exDatOutPath))
                    {
                        jChunks = JArray.Parse(sr.ReadToEnd()).Select(j => (JObject) j).ToArray();
                    }

                    foreach (var jChunk in jChunks)
                    {
                        var chunkKey = (int) jChunk["Key"];
                        if (!exDat.Chunks.ContainsKey(chunkKey)) continue;

                        exDat.Chunks[chunkKey].LoadJObject(jChunk);
                    }

                    var buffer = exDat.RepackExD();
                    buffer = exDat.RepackData(origDat, buffer);

                    exDat.UpdateOffset((int) new FileInfo(outputNewDatPath).Length, 1, index);

                    using (var fs = new FileStream(outputNewDatPath, FileMode.Append, FileAccess.Write))
                    using (var bw = new BinaryWriter(fs))
                    {
                        bw.Write(buffer);
                    }
                }

            File.WriteAllBytes(outputIndexPath, index);

            UpdateDatHash(outputNewDatPath);
        }

        private static void SwapCodes(ExHFile[] exHeaders, string outputDir)
        {
            // swap two lang table based on string key mapping (if available) or chunk key mapping.
            Console.Write("Enter source lang code: ");
            var sourceLangCode = Console.ReadLine();
            Console.Write("Enter target lang code: ");
            var targetLangCode = Console.ReadLine();

            // placeholder files.
            var emptyPath = Path.Combine(outputDir, "empty");
            if (File.Exists(emptyPath)) File.Delete(emptyPath);

            var emptyArray = new JArray();

            // these files are mapped by string key and is pretty much accurate.
            var stringKeyMappedPath = Path.Combine(outputDir, "string_key_mapped");
            if (File.Exists(stringKeyMappedPath)) File.Delete(stringKeyMappedPath);

            var stringKeyMappedArray = new JArray();

            // these files are mapped by chunk key and could be wrong.
            var chunkKeyMappedPath = Path.Combine(outputDir, "chunk_key_mapped");
            if (File.Exists(chunkKeyMappedPath)) File.Delete(chunkKeyMappedPath);

            var chunkKeyMappedArray = new JArray();

            // these files do not exist in source lang code but exist in target lang code.
            // need custom translations.
            var notTranslatedPath = Path.Combine(outputDir, "not_translated");
            if (File.Exists(notTranslatedPath)) File.Delete(notTranslatedPath);

            var notTranslatedArray = new JArray();

            for (var i = 0; i < exHeaders.Length; i++)
                foreach (var exDat in exHeaders[i].ExDats)
                {
                    Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                    if (exDat.LanguageCode != targetLangCode) continue;

                    var exDatOutDir = Path.Combine(outputDir, exDat.PhysicalDir);
                    if (!Directory.Exists(exDatOutDir)) continue;

                    var exDatSourcePath = Path.Combine(exDatOutDir, sourceLangCode);

                    // source doesn't exist, which means this is probably newer file.
                    // need custom translations.
                    if (!File.Exists(exDatSourcePath))
                    {
                        notTranslatedArray.Add(exDat.PhysicalDir);
                        continue;
                    }

                    JObject[] jChunks = null;

                    using (var sr = new StreamReader(exDatSourcePath))
                    {
                        jChunks = JArray.Parse(sr.ReadToEnd()).Select(j => (JObject) j).ToArray();
                    }

                    if (jChunks.Length == 0) continue;

                    // load string key based mapper.
                    // string key mapper -> field key 0 should be text-only field with all-capital constant (i.e. TEXT_XXX_NNN_SYSTEM_NNN_NN)
                    var mapper = new Dictionary<string, JObject>();
                    var stringKeyRegex = new Regex("^[A-Za-z0-9_]+$");
                    var exDChunks = new List<ExDChunk>();

                    foreach (var jChunk in jChunks)
                    {
                        var chunk = new ExDChunk();
                        chunk.LoadJObject(jChunk);
                        exDChunks.Add(chunk);

                        // string key mapper should have string key on field 0 and text content on field 4.
                        if (!chunk.Fields.ContainsKey(0) || !chunk.Fields.ContainsKey(4) || chunk.Fields.Count != 2)
                            continue;

                        var stringKeyField = jChunk["Fields"].Select(j => (JObject) j)
                            .First(j => (ushort) j["FieldKey"] == 0);

                        // string key should not have any tags and consist of only one text type entry.
                        var jEntries = stringKeyField["FieldValue"].Select(j => (JObject) j).ToArray();
                        if (jEntries.Length != 1) continue;

                        if ((string) jEntries[0]["EntryType"] != "text") continue;

                        // additional validation for string key.
                        var stringKey = (string) jEntries[0]["EntryValue"];
                        if (!stringKeyRegex.IsMatch(stringKey)) continue;

                        if (mapper.ContainsKey(stringKey)) continue;

                        mapper.Add(stringKey, jChunk);
                    }

                    // if all rows in the table are string mapped
                    if (mapper.Count == jChunks.Length)
                    {
                        foreach (var chunkKey in exDat.Chunks.Keys)
                        {
                            // find jobject with the same string key.
                            var stringKey = new UTF8Encoding(false).GetString(exDat.Chunks[chunkKey].Fields[0]);
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
                        var isEmpty = true;

                        foreach (var chunk in exDat.Chunks.Values)
                        foreach (var field in chunk.Fields.Values)
                            if (field.Length > 0)
                                isEmpty = false;

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

                            foreach (var chunk in exDChunks)
                            foreach (var field in chunk.Fields.Values)
                                if (field.Length > 0)
                                    isEmpty = false;

                            // if source is empty, this means no source data is present for this target file.
                            // add in not translated list.
                            if (isEmpty)
                            {
                                notTranslatedArray.Add(exDat.PhysicalDir);
                            }

                            // both are not empty, let's proceed with chunk key mapping...
                            else
                            {
                                foreach (var chunk in exDChunks)
                                {
                                    if (!exDat.Chunks.ContainsKey(chunk.Key)) continue;

                                    foreach (var fieldKey in chunk.Fields.Keys)
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

            // record all arrays.
            using (var sw = new StreamWriter(emptyPath, false))
            {
                sw.Write(emptyArray.ToString());
            }

            using (var sw = new StreamWriter(stringKeyMappedPath, false))
            {
                sw.Write(stringKeyMappedArray.ToString());
            }

            using (var sw = new StreamWriter(chunkKeyMappedPath, false))
            {
                sw.Write(chunkKeyMappedArray.ToString());
            }

            using (var sw = new StreamWriter(notTranslatedPath, false))
            {
                sw.Write(notTranslatedArray.ToString());
            }
        }

        private static void CompressTranslations(ExHFile[] exHeaders, string outputDir)
        {
            var translations = new JObject();

            for (var i = 0; i < exHeaders.Length; i++)
                foreach (var exDat in exHeaders[i].ExDats)
                {
                    Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                    if (translations.ContainsKey(exDat.PhysicalDir)) continue;

                    var exDatOutDir = Path.Combine(outputDir, exDat.PhysicalDir);
                    if (!Directory.Exists(exDatOutDir)) continue;

                    var exDatOutPath = Path.Combine(exDatOutDir, "trans");
                    if (!File.Exists(exDatOutPath)) continue;

                    translations.Add(exDat.PhysicalDir, new JValue(File.ReadAllBytes(exDatOutPath)));

                    Console.WriteLine();
                    Console.WriteLine(exDat.PhysicalDir);
                }

            var bTranslations = new UTF8Encoding().GetBytes(translations.ToString());

            using (var ms = new MemoryStream())
            {
                using (var ds = new DeflateStream(ms, CompressionMode.Compress))
                using (var _ms = new MemoryStream(bTranslations))
                {
                    _ms.CopyTo(ds);
                }

                var outputPath = Path.Combine(outputDir, "translations");
                if (File.Exists(outputPath)) File.Delete(outputPath);

                File.WriteAllBytes(outputPath, ms.ToArray());
            }

            Console.WriteLine();
            Console.WriteLine("DONE");
            Console.ReadLine();
        }

        private static void ExtractTranslations(ExHFile[] exHeaders, string outputDir, string baseDir)
        {
            var translationsPath = Path.Combine(baseDir, "input", "translations");
            if (!File.Exists(translationsPath)) return;

            var translations = DecompressTranslations(translationsPath);

            for (var i = 0; i < exHeaders.Length; i++)
                foreach (var exDat in exHeaders[i].ExDats)
                {
                    Report(string.Format("{0} / {1}: {2}", i, exHeaders.Length, exDat.Name));

                    if (!translations.ContainsKey(exDat.PhysicalDir)) continue;

                    var exDatOutDir = Path.Combine(outputDir, exDat.PhysicalDir);
                    if (!Directory.Exists(exDatOutDir)) Directory.CreateDirectory(exDatOutDir);

                    var transOutPath = Path.Combine(exDatOutDir, "trans");
                    if (File.Exists(transOutPath)) File.Delete(transOutPath);

                    File.WriteAllBytes(transOutPath, (byte[]) translations[exDat.PhysicalDir]);
                }
        }

        private static JObject DecompressTranslations(string translationsPath)
        {
            if (!File.Exists(translationsPath)) return null;

            JObject translations;

            using (var ms = new MemoryStream(File.ReadAllBytes(translationsPath)))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            {
                using (var _ms = new MemoryStream())
                {
                    ds.CopyTo(_ms);
                    translations = JObject.Parse(new UTF8Encoding().GetString(_ms.ToArray()));
                }
            }

            return translations;
        }

        private static JArray DecodeTranslations(byte[] translations)
        {
            var decoded = string.Empty;

            using (var ms = new MemoryStream(translations))
            using (var sr = new StreamReader(ms))
            {
                decoded = sr.ReadToEnd();
            }

            return JArray.Parse(decoded);
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