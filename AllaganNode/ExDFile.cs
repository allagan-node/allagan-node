using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AllaganNode
{
    public class ExDChunk
    {
        public int Key;
        public int Offset;

        public int Size;
        public short CheckDigit;

        public Dictionary<ushort, byte[]> Fields = new Dictionary<ushort, byte[]>();

        public JObject GetJObject()
        {
            JObject jObject = new JObject(
                new JProperty("Key", Key),
                new JProperty("Offset", Offset),
                new JProperty("Size", Size),
                new JProperty("CheckDigit", CheckDigit),
                new JProperty("Fields", new JArray()));

            JArray jArray = (JArray)jObject["Fields"];

            foreach (ushort fieldKey in Fields.Keys)
            {
                JArray entryArray = new JArray();
                EncodeField(Fields[fieldKey], entryArray);

                jArray.Add(new JObject(
                    new JProperty("FieldKey", fieldKey),
                    new JProperty("FieldValue", entryArray)));
            }

            return jObject;
        }

        // encode bytes into legible json field object.
        private void EncodeField(byte[] field, JArray jArray)
        {
            if (field.Length == 0) return;

            // if no tags, just encode it with UTF8.
            if (!field.Contains((byte)0x2))
            {
                jArray.Add(new JObject(
                    new JProperty("EntryType", "text"),
                    new JProperty("EntryValue", new UTF8Encoding(false).GetString(field))));
            }
            else
            {
                int tagIndex = Array.FindIndex(field, b => b == 0x2);

                // if start byte is opening of tag, treat it as tag.
                if (tagIndex == 0)
                {
                    EncodeTag(field, jArray);
                }
                // divide text part and tag part.
                else
                {
                    byte[] head = new byte[tagIndex];
                    Array.Copy(field, 0, head, 0, tagIndex);

                    byte[] tag = new byte[field.Length - tagIndex];
                    Array.Copy(field, tagIndex, tag, 0, tag.Length);

                    EncodeField(head, jArray);
                    EncodeTag(tag, jArray);
                }
            }
        }

        // encode bytes into legible json tag object.
        private void EncodeTag(byte[] tag, JArray jArray)
        {
            if (tag.Length == 0) return;

            // if start byte is not opening of tag, treat it as field.
            if (tag[0] != 0x2)
            {
                EncodeField(tag, jArray);
            }
            else
            {
                /*
                // [0] -> 0x2 (opening of tag)
                // [1] -> byte (type of tag)
                // [2] -> byte (type of length)
                // [..] -> length data (depending on type of length)
                // [..length..] -> data
                // [last] -> 0x3 (closing of tag)

                byte lengthType = tag[2];
                int totalLength;
                byte[] tagData;

                if (lengthType < 0xf0)
                {
                    // length type itself is a length, including the length type byte itself.
                    // total length -> [0] + [1] + (length type = [2...data...]) + [last]
                    totalLength = lengthType + 3;
                    tagData = new byte[lengthType - 1];
                    Array.Copy(tag, 3, tagData, 0, lengthType - 1);
                }
                else if (lengthType == 0xf0)
                {
                    // trailing byte is the length.
                    // total length -> [0] + [1] + [2] + [length byte] + (length byte = [...data...]) + [last]
                    totalLength = tag[3] + 5;
                    tagData = new byte[tag[3]];
                    Array.Copy(tag, 4, tagData, 0, tag[3]);
                }
                else if (lengthType == 0xf1)
                {
                    // trailing byte * 256 is the length.
                    // total length -> [0] + [1] + [2] + [length byte] + (length byte * 256 = [...data...]) + [last]
                    totalLength = (tag[3] * 256) + 5;
                    tagData = new byte[tag[3] * 256];
                    Array.Copy(tag, 4, tagData, 0, tag[3] * 256);
                }
                else if (lengthType == 0xf2)
                {
                    // (trailing byte << 8) + (next byte) is the length. (int16)
                    // total length -> [0] + [1] + [2] + [l1] + [l2] + ([...data...]) + [last]
                    int dataLength = (tag[3] << 8) + tag[4];
                    totalLength = dataLength + 6;
                    tagData = new byte[dataLength];
                    Array.Copy(tag, 5, tagData, 0, dataLength);
                }
                else if (lengthType == 0xf3)
                {
                    // (trailing byte << 16) + (next byte << 8) + (next byte) is the length. (int24)
                    // total length -> [0] + [1] + [2] + [l1] + [l2] + [l3] + ([...data...]) + [last]
                    int dataLength = (tag[3] << 16) + (tag[4] << 8) + tag[5];
                    totalLength = dataLength + 7;
                    tagData = new byte[dataLength];
                    Array.Copy(tag, 6, tagData, 0, dataLength);
                }
                else if (lengthType == 0xf4)
                {
                    // (trailing byte << 24) + (next byte << 16) + (next byte << 8) + (next byte) is the length. (int32)
                    // total length -> [0] + [1] + [2] + [l1] + [l2] + [l3] + [l4] + ([...data...]) + [last]
                    int dataLength = (tag[3] << 24) + (tag[4] << 16) + (tag[5] << 8) + tag[6];
                    totalLength = dataLength + 8;
                    tagData = new byte[dataLength];
                    Array.Copy(tag, 7, tagData, 0, dataLength);
                }
                else throw new Exception();

                // check tag closing byte.
                if (tag[totalLength - 1] != 0x3) throw new Exception();

                jArray.Add(CreateTagJObject(tag[1], tagData));

                byte[] field = new byte[tag.Length - totalLength];
                Array.Copy(tag, totalLength, field, 0, field.Length);
                EncodeField(field, jArray);*/

                byte tagType = tag[1];

                // length byte + tag
                byte[] tagData = new byte[tag.Length - 2];
                Array.Copy(tag, 2, tagData, 0, tagData.Length);

                byte[] tail = new byte[0];

                SplitByLength(ref tagData, ref tail);

                // check tag closing byte.
                if (tail[0] != 0x3) throw new Exception();

                jArray.Add(CreateTagJObject(tagType, tagData));

                byte[] field = new byte[tail.Length - 1];
                Array.Copy(tail, 1, field, 0, field.Length);
                EncodeField(field, jArray);
            }
        }

        // Parses length byte and splits the given data.
        private void SplitByLength(ref byte[] tag, ref byte[] tail)
        {
            // [0] -> byte (type of length)
            // [..] -> length data (depending on type of length)
            // [..length..] -> data

            byte lengthType = tag[0];
            byte[] tagData;

            if (lengthType < 0xf0)
            {
                // length type itself is a length, including the length type byte itself.
                // total length -> length type - 1
                tagData = new byte[lengthType - 1];
                Array.Copy(tag, 1, tagData, 0, lengthType - 1);

                tail = new byte[tag.Length - lengthType];
                Array.Copy(tag, lengthType, tail, 0, tail.Length);
            }
            else if (lengthType == 0xf0)
            {
                // trailing byte is the length.
                // [0] + [length byte] + (length byte = [...data...])
                tagData = new byte[tag[1]];
                Array.Copy(tag, 2, tagData, 0, tag[1]);

                tail = new byte[tag.Length - tag[1] - 2];
                Array.Copy(tag, tag[1] + 2, tail, 0, tail.Length);
            }
            else if (lengthType == 0xf1)
            {
                // trailing byte * 256 is the length.
                // [0] + [length byte] + (length byte * 256 = [...data...])
                int length = tag[1] * 256;
                tagData = new byte[length];
                Array.Copy(tag, 2, tagData, 0, length);

                tail = new byte[tag.Length - length - 2];
                Array.Copy(tag, length + 2, tail, 0, tail.Length);
            }
            else if (lengthType == 0xf2)
            {
                // (trailing byte << 8) + (next byte) is the length. (int16)
                // [0] + [l1] + [l2] + ([...data...])
                int length = (tag[1] << 8) + tag[2];
                tagData = new byte[length];
                Array.Copy(tag, 3, tagData, 0, length);

                tail = new byte[tag.Length - length - 3];
                Array.Copy(tag, length + 3, tail, 0, tail.Length);
            }
            else if (lengthType == 0xf3)
            {
                // (trailing byte << 16) + (next byte << 8) + (next byte) is the length. (int24)
                // [0] + [l1] + [l2] + [l3] + ([...data...])
                int length = (tag[1] << 16) + (tag[2] << 8) + tag[3];
                tagData = new byte[length];
                Array.Copy(tag, 4, tagData, 0, length);

                tail = new byte[tag.Length - length - 4];
                Array.Copy(tag, length + 4, tail, 0, tail.Length);
            }
            else if (lengthType == 0xf4)
            {
                // (trailing byte << 24) + (next byte << 16) + (next byte << 8) + (next byte) is the length. (int32)
                // [0] + [l1] + [l2] + [l3] + [l4] + ([...data...])
                int length = (tag[1] << 24) + (tag[2] << 16) + (tag[3] << 8) + tag[4];
                tagData = new byte[length];
                Array.Copy(tag, 5, tagData, 0, length);

                tail = new byte[tag.Length - length - 5];
                Array.Copy(tag, length + 5, tail, 0, tail.Length);
            }
            else throw new Exception();

            tag = tagData;
        }

        // Recursively parse tag to encode any fields that may be embedded inside.
        private void ParseTag(byte[] tag, JArray jArray)
        {
            if (tag.Length == 0) return;

            // if tag doesn't contain any field tag, just add it to array and return.
            if (!tag.Contains((byte)0xff))
            {
                jArray.Add(new JObject(
                    new JProperty("TokenType", "data"),
                    new JProperty("TokenValue", tag)));
            }
            else
            {
                int fieldIndex = Array.FindIndex(tag, b => b == 0xff);

                // head would be tag data which we can add directly.
                byte[] head = new byte[fieldIndex];
                Array.Copy(tag, 0, head, 0, fieldIndex);
                ParseTag(head, jArray);

                // field tag has to be split by length byte.
                byte[] field = new byte[tag.Length - fieldIndex - 1];
                Array.Copy(tag, fieldIndex + 1, field, 0, field.Length);
                byte[] tail = new byte[0];

                SplitByLength(ref field, ref tail);

                // now treat it as full-blown field.
                JArray fieldArray = new JArray();
                EncodeField(field, fieldArray);
                jArray.Add(new JObject(
                    new JProperty("TokenType", "field"),
                    new JProperty("TokenValue", fieldArray)));

                // parse the rest of the tag.
                ParseTag(tail, jArray);
            }
        }

        private JObject CreateTagJObject(byte tagType, byte[] tagData)
        {
            switch (tagType)
            {
                case 0x6:
                    // reset time
                    break;

                case 0x7:
                    // time
                    break;

                case 0x8:
                    // if
                    break;

                case 0x9:
                    // switch
                    break;

                case 0xc:
                    // if equals
                    break;

                case 0xa:
                    // unknown
                    break;

                case 0x10:
                    // line break
                    break;

                case 0x12:
                    // gui
                    break;

                case 0x13:
                    // color
                    break;

                case 0x14:
                    // unknown
                    break;
            }
            JArray tags = new JArray();
            ParseTag(tagData, tags);

            /*if (tagType == 0x8)
            {
                File.WriteAllBytes(@"C:\Users\486238\Desktop\test", tagData);
                Console.WriteLine();
                Console.WriteLine("TEST");
                Console.ReadLine();
            }*/

            // TODO: recursive decoding inside tag for 0xff decoding byte.
            return new JObject(
                new JProperty("EntryType", "tag"),
                new JProperty("EntryValue", new JObject(
                    new JProperty("TagType", tagType),
                    new JProperty("TagValue", tags))));
        }

        // load from json
        public void LoadJObject(JObject jObject)
        {
            Key = (int)jObject["Key"];
            Offset = (int)jObject["Offset"];
            Size = (int)jObject["Size"];
            CheckDigit = (short)jObject["CheckDigit"];

            Fields.Clear();
            JObject[] fields = jObject["Fields"].Select(f => (JObject)f).ToArray();
            foreach (JObject field in fields)
            {
                ushort fieldKey = (ushort)field["FieldKey"];
                JObject[] entries = field["FieldValue"].Select(e => (JObject)e).ToArray();
                byte[] bField = new byte[0];

                foreach (JObject entry in entries)
                {
                    byte[] decoded = new byte[0];
                    string entryType = (string)entry["EntryType"];

                    if (entryType == "text")
                    {
                        decoded = new UTF8Encoding(false).GetBytes((string)entry["EntryValue"]);
                    }
                    else if (entryType == "tag")
                    {
                        JObject tagObject = (JObject)entry["EntryValue"];

                        // leading 0s are denoted as . for easier legibility.
                        // 0x00 ~ 0xef -> 0x .. .. .. .0 ~ 0x .. .. .. ef
                        // 0xf0        -> 0x .. .. .. f0 ~ 0x .. .. .. ff
                        // 0xf1        -> 0x .. .. .1 00 ~ 0x .. .. ff 00 <- (last byte = 0)
                        // 0xf2        -> 0x .. .. .1 01 ~ 0x .. .. ff ff <- (last byte != 0)
                        // 0xf3        -> 0x .. .1 00 00 ~ 0x .. ff ff ff
                        // 0xf4        -> 0x .1 00 00 00 ~ 0x ff ff ff ff
                        byte[] tagData = (byte[])tagObject["TagValue"];
                        int dataLength = tagData.Length;

                        // if data length + length type byte is still smaller than 0xf0
                        if (dataLength + 1 < 0xf0)
                        {
                            // length type itself is a length, including the length type byte itself.
                            // total length -> [0] + [1] + (length type = [2...data...]) + [last]
                            byte lengthType = (byte)(dataLength + 1);
                            int totalLength = lengthType + 3;
                            Array.Resize(ref decoded, totalLength);
                            Array.Copy(tagData, 0, decoded, 3, dataLength);
                            decoded[2] = lengthType;
                        }
                        // if data length is 1 byte
                        else if (dataLength < 0x100)
                        {
                            // trailing byte is the length.
                            // total length -> [0] + [1] + [2] + [length byte] + (length byte = [...data...]) + [last]
                            Array.Resize(ref decoded, dataLength + 5);
                            Array.Copy(tagData, 0, decoded, 4, dataLength);
                            // length type
                            decoded[2] = 0xf0;
                            // length byte
                            decoded[3] = (byte)dataLength;
                        }
                        // if data length is 2 byte and last byte is 0
                        else if (dataLength < 0x10000 && dataLength % 256 == 0)
                        {
                            // trailing byte * 256 is the length.
                            // total length -> [0] + [1] + [2] + [length byte] + (length byte * 256 = [...data...]) + [last]
                            Array.Resize(ref decoded, dataLength + 5);
                            Array.Copy(tagData, 0, decoded, 4, dataLength);
                            // length type
                            decoded[2] = 0xf1;
                            // length byte
                            decoded[3] = (byte)(dataLength / 256);
                        }
                        // if data length is 2 byte and last byte is not 0
                        else if (dataLength < 0x10000)
                        {
                            // (trailing byte << 8) + (next byte) is the length. (int16)
                            // total length -> [0] + [1] + [2] + [l1] + [l2] + ([...data...]) + [last]
                            Array.Resize(ref decoded, dataLength + 6);
                            Array.Copy(tagData, 0, decoded, 5, dataLength);
                            // length type
                            decoded[2] = 0xf2;
                            // length bytes
                            decoded[3] = (byte)(dataLength >> 8);
                            decoded[4] = (byte)(dataLength & 0xff);
                        }
                        // if data length is 3 bytes
                        else if (dataLength < 0x1000000)
                        {
                            // (trailing byte << 16) + (next byte << 8) + (next byte) is the length. (int24)
                            // total length -> [0] + [1] + [2] + [l1] + [l2] + [l3] + ([...data...]) + [last]
                            Array.Resize(ref decoded, dataLength + 7);
                            Array.Copy(tagData, 0, decoded, 6, dataLength);
                            // length type
                            decoded[2] = 0xf3;
                            // length bytes
                            decoded[3] = (byte)(dataLength >> 16);
                            decoded[4] = (byte)((dataLength & 0xff00) >> 8);
                            decoded[5] = (byte)(dataLength & 0xff);
                        }
                        // if dat length is 4 bytes
                        else
                        {
                            // (trailing byte << 24) + (next byte << 16) + (next byte << 8) + (next byte) is the length. (int32)
                            // total length -> [0] + [1] + [2] + [l1] + [l2] + [l3] + [l4] + ([...data...]) + [last]
                            Array.Resize(ref decoded, dataLength + 8);
                            Array.Copy(tagData, 0, decoded, 7, dataLength);
                            // length type
                            decoded[2] = 0xf4;
                            // length bytes
                            decoded[3] = (byte)(dataLength >> 24);
                            decoded[4] = (byte)((dataLength & 0xff0000) >> 16);
                            decoded[5] = (byte)((dataLength & 0xff00) >> 8);
                            decoded[6] = (byte)(dataLength & 0xff);
                        }

                        // set opening, closing and tag type byte.
                        decoded[0] = 0x2;
                        decoded[1] = (byte)tagObject["TagType"];
                        decoded[decoded.Length - 1] = 0x3;
                    }

                    // append and continue.
                    int curFieldLength = bField.Length;
                    Array.Resize(ref bField, bField.Length + decoded.Length);
                    Array.Copy(decoded, 0, bField, curFieldLength, decoded.Length);
                }

                Fields.Add(fieldKey, bField);
            }
        }
    }

    public class ExDFile : SqFile
    {
        // name of the header.
        public string HeaderName;

        // physicaly directory for extracting.
        public string PhysicalDir;

        // language table.
        public string LanguageCode;

        // link to the parent header that this file belongs to.
        public ExHFile ExHeader;

        // all chunks under data table.
        public Dictionary<int, ExDChunk> Chunks = new Dictionary<int, ExDChunk>();

        // decode exd from buffered data.
        public void ReadExD()
        {
            byte[] data = ReadData();

            int offsetTableSize = toInt32(data, 0x8, true);
            int chunkTableSize = toInt32(data, 0xc, true);

            byte[] offsetTable = new byte[offsetTableSize];
            Array.Copy(data, 0x20, offsetTable, 0, offsetTableSize);

            byte[] chunkTable = new byte[chunkTableSize];
            Array.Copy(data, 0x20 + offsetTableSize, chunkTable, 0, chunkTableSize);

            for (int i = 0; i < offsetTableSize; i += 0x8)
            {
                ExDChunk chunk = new ExDChunk();

                chunk.Key = toInt32(offsetTable, i, true);
                chunk.Offset = toInt32(offsetTable, i + 0x4, true);

                int chunkTablePosition = chunk.Offset - 0x20 - offsetTableSize;
                chunk.Size = toInt32(chunkTable, chunkTablePosition, true);
                chunk.CheckDigit = toInt16(chunkTable, chunkTablePosition + 0x4, true);

                byte[] columnDefinitions = new byte[ExHeader.FixedSizeDataLength];
                Array.Copy(chunkTable, chunkTablePosition + 0x6, columnDefinitions, 0, ExHeader.FixedSizeDataLength);

                byte[] rawData = new byte[chunk.Size - ExHeader.FixedSizeDataLength];
                Array.Copy(chunkTable, chunkTablePosition + 0x6 + ExHeader.FixedSizeDataLength, rawData, 0, rawData.Length);

                foreach (ExHColumn column in ExHeader.Columns)
                {
                    int fieldStart = toInt32(columnDefinitions, column.Offset, true);
                    int fieldEnd = fieldStart;
                    while (fieldEnd < rawData.Length && rawData[fieldEnd] != 0) fieldEnd++;

                    byte[] field = new byte[fieldEnd - fieldStart];
                    Array.Copy(rawData, fieldStart, field, 0, fieldEnd - fieldStart);

                    chunk.Fields.Add(column.Offset, field);
                }

                Chunks.Add(chunk.Key, chunk);
            }
        }

        // repack updated exd chunks back to buffer.
        public byte[] RepackExD()
        {
            byte[] data = ReadData();

            int offsetTableSize = toInt32(data, 0x8, true);
            int chunkTableSize = toInt32(data, 0xc, true);

            byte[] offsetTable = new byte[offsetTableSize];
            Array.Copy(data, 0x20, offsetTable, 0, offsetTableSize);

            byte[] chunkTable = new byte[chunkTableSize];
            Array.Copy(data, 0x20 + offsetTableSize, chunkTable, 0, chunkTableSize);

            using (MemoryStream newChunkTable = new MemoryStream())
            using (BinaryWriter newChunkTableWriter = new BinaryWriter(newChunkTable))
            {
                for (int i = 0; i < offsetTableSize; i += 0x8)
                {
                    int chunkKey = toInt32(offsetTable, i, true);
                    int chunkOffset = toInt32(offsetTable, i + 0x4, true);

                    ExDChunk chunk = Chunks[chunkKey];

                    int chunkTablePosition = chunkOffset - 0x20 - offsetTableSize;

                    byte[] columnDefinitions = new byte[ExHeader.FixedSizeDataLength];
                    Array.Copy(chunkTable, chunkTablePosition + 0x6, columnDefinitions, 0, ExHeader.FixedSizeDataLength);

                    using (MemoryStream rawData = new MemoryStream())
                    using (BinaryWriter rawDataWriter = new BinaryWriter(rawData))
                    {
                        foreach (ExHColumn column in ExHeader.Columns)
                        {
                            int fieldStart = (int)rawData.Length;
                            Array.Copy(toBytes(fieldStart, true), 0, columnDefinitions, column.Offset, 0x4);

                            rawDataWriter.Write(chunk.Fields[column.Offset]);
                            rawDataWriter.Write((byte)0);
                        }

                        int paddingLeftover = ((int)rawData.Length + ExHeader.FixedSizeDataLength + 0x6 + (int)newChunkTable.Length + offsetTableSize + 0x20) % 0x4;
                        if (paddingLeftover != 0)
                        {
                            rawDataWriter.Write(new byte[0x4 - paddingLeftover]);
                        }

                        byte[] newChunkHeader = new byte[0x6];
                        Array.Copy(toBytes((int)rawData.Length + ExHeader.FixedSizeDataLength, true), 0, newChunkHeader, 0, 0x4);
                        Array.Copy(toBytes(chunk.CheckDigit, true), 0, newChunkHeader, 0x4, 0x2);

                        int newChunkOffset = (int)newChunkTable.Length + 0x20 + offsetTableSize;
                        Array.Copy(toBytes(newChunkOffset, true), 0, offsetTable, i + 0x4, 0x4);

                        newChunkTableWriter.Write(newChunkHeader);
                        newChunkTableWriter.Write(columnDefinitions);
                        newChunkTableWriter.Write(rawData.ToArray());
                    }
                }

                byte[] newBuffer = new byte[0x20 + offsetTableSize + newChunkTable.Length];
                Array.Copy(data, 0, newBuffer, 0, 0x20);
                Array.Copy(toBytes((int)newChunkTable.Length, true), 0, newBuffer, 0xc, 0x4);
                Array.Copy(offsetTable, 0, newBuffer, 0x20, offsetTableSize);
                Array.Copy(newChunkTable.ToArray(), 0, newBuffer, 0x20 + offsetTableSize, (int)newChunkTable.Length);

                return newBuffer;
            }
        }

        // extract exd to external file.
        public void ExtractExD(string outputDir)
        {
            string exDatOutDir = Path.Combine(outputDir, PhysicalDir);
            if (!Directory.Exists(exDatOutDir)) Directory.CreateDirectory(exDatOutDir);

            string exDatOutPath = Path.Combine(exDatOutDir, LanguageCode);
            if (File.Exists(exDatOutPath)) File.Delete(exDatOutPath);

            using (StreamWriter sw = new StreamWriter(exDatOutPath, false))
            {
                JArray jArray = new JArray();

                foreach (ExDChunk chunk in Chunks.Values)
                {
                    jArray.Add(chunk.GetJObject());
                }

                sw.Write(jArray.ToString());
            }
        }
    }
}
