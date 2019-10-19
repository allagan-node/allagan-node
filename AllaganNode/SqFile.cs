using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace AllaganNode
{
    public class ExHColumn
    {
        public ushort Type;
        public ushort Offset;
    }

    public class ExHRange
    {
        public int Start;
        public int Length;
    }

    public class ExHLanguage
    {
        public byte Value;
        public string Code
        {
            get
            {
                switch (Value)
                {
                    case 1:
                        return "ja";
                    case 2:
                        return "en";
                    case 3:
                        return "de";
                    case 4:
                        return "fr";
                    case 5:
                        return "chs";
                    case 6:
                        return "cht";
                    case 7:
                        return "ko";
                    default:
                        return null;
                }
            }
        }
    }

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
                EncodeField(field, jArray);
            }
        }

        private JObject CreateTagJObject(byte tagType, byte[] tagData)
        {
            // TODO: recursive decoding inside tag for 0xff decoding byte.
            return new JObject(
                new JProperty("EntryType", "tag"),
                new JProperty("EntryValue", new JObject(
                    new JProperty("TagType", tagType),
                    new JProperty("TagValue", tagData))));
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

    public class SqFile
    {
        public uint Key;
        public uint DirectoryKey;

        // [...][2,1,0] -> last three bytes denote the datnum (i.e. dat0, dat1...), rest of the bytes denote offset.
        public int WrappedOffset;
        public byte DatFile
        {
            get
            {
                return (byte)((WrappedOffset & 0x7) >> 1);
            }

            set
            {
                WrappedOffset = (int)(WrappedOffset & 0xfffffff8) | ((value & 0x3) << 1);
            }
        }
        public int Offset
        {
            get
            {
                return (int)(WrappedOffset & 0xfffffff8) << 3;
            }

            set
            {
                WrappedOffset = (WrappedOffset & 0x7) | (int)((value >> 3) & 0xfffffff8);
            }
        }
        public byte[] Data;

        // full file name.
        public string Name;

        // full directory.
        public string Dir;

        // for ExHs.
        public ushort Variant;
        public ushort FixedSizeDataLength;
        public ExHColumn[] Columns;
        public ExHRange[] Ranges;
        public ExHLanguage[] Languages;
        public List<SqFile> ExDats = new List<SqFile>();

        // for ExDs.
        public string LanguageCode;
        public Dictionary<int, ExDChunk> Chunks = new Dictionary<int, ExDChunk>();

        // read data blocks and uncompress them.
        public void ReadData(string datPath)
        {
            using (FileStream fs = File.OpenRead(datPath))
            using (BinaryReader br = new BinaryReader(fs))
            {
                br.BaseStream.Position = Offset;
                int endOfHeader = br.ReadInt32();

                byte[] header = new byte[endOfHeader];
                br.BaseStream.Position = Offset;
                br.Read(header, 0, endOfHeader);

                // 4th byte denotes the type of data, which should be 2 for binary files.
                if (BitConverter.ToInt32(header, 0x4) != 2) return;

                // supposed to be the total stream size... but not validating at the moment.
                long length = BitConverter.ToInt32(header, 0x10) * 0x80;
                short blockCount = BitConverter.ToInt16(header, 0x14);

                using (MemoryStream ms = new MemoryStream())
                {
                    for (int i = 0; i < blockCount; i++)
                    {
                        // read where the block is from the header.
                        int blockOffset = BitConverter.ToInt32(header, 0x18 + i * 0x8);

                        // read the actual header of the block. Always 10 bytes.
                        byte[] blockHeader = new byte[0x10];
                        br.BaseStream.Position = Offset + endOfHeader + blockOffset;
                        br.Read(blockHeader, 0, 0x10);

                        // source size -> size the block is actually taking up in this dat file.
                        // raw size -> size before compression (if compressed)
                        int sourceSize = BitConverter.ToInt32(blockHeader, 0x8);
                        int rawSize = BitConverter.ToInt32(blockHeader, 0xc);

                        // compression threhsold = 0x7d00
                        bool isCompressed = sourceSize < 0x7d00;
                        int actualSize = isCompressed ? sourceSize : rawSize;

                        // block is padded to be divisible by 0x80
                        int paddingLeftover = (actualSize + 0x10) % 0x80;
                        if (isCompressed && paddingLeftover != 0)
                        {
                            actualSize += 0x80 - paddingLeftover;
                        }

                        // copy over the block from dat.
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
                    }

                    Data = ms.ToArray();
                }
            }
        }

        // write current Data buffer to new dat (.dat1) and update the offset in .index
        public void WriteData(byte[] origDat, ref byte[] newDat, byte[] index)
        {
            int endOfHeader = BitConverter.ToInt32(origDat, Offset);

            byte[] header = new byte[endOfHeader];
            Array.Copy(origDat, Offset, header, 0, endOfHeader);

            // divide up data to blocks with max size 0x3e80
            List<byte[]> blocks = new List<byte[]>();
            int position = 0;
            while (position < Data.Length)
            {
                int blockLength = Math.Min(0x3e80, Data.Length - position);
                byte[] tmp = new byte[blockLength];
                Array.Copy(Data, position, tmp, 0, blockLength);
                blocks.Add(tmp);

                position += blockLength;
            }

            // new header ->
            //     first 18 bytes will be existing information (like total length, etc) that will be later updated.
            //     rest will be 8 byte each for offset information for blocks.
            //     pad header to be divisible by 0x80
            int newHeaderLength = 0x18 + blocks.Count * 0x8;
            int newHeaderPaddingLeftover = newHeaderLength % 0x80;
            if (newHeaderPaddingLeftover != 0)
            {
                newHeaderLength += 0x80 - newHeaderPaddingLeftover;
            }
            byte[] newHeader = new byte[newHeaderLength];
            Array.Copy(header, 0, newHeader, 0, 0x18);
            Array.Copy(BitConverter.GetBytes(newHeader.Length), 0, newHeader, 0, 0x4);
            Array.Copy(BitConverter.GetBytes(blocks.Count), 0, newHeader, 0x14, 0x2);

            byte[] newBlocks = new byte[0];
            for (int i = 0; i < blocks.Count; i++)
            {
                byte[] compressedBlock;

                using (MemoryStream ms = new MemoryStream())
                {
                    using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Compress))
                    using (MemoryStream _ms = new MemoryStream(blocks[i]))
                    {
                        _ms.CopyTo(ds);
                    }

                    compressedBlock = ms.ToArray();
                }

                // record compressed size that will be written to the dat file.
                // actual size will be padded so that it's divisible by 0x80
                int sourceSize = compressedBlock.Length;
                int actualSize = compressedBlock.Length;
                int paddingLeftover = (actualSize + 0x10) % 0x80;
                if (paddingLeftover != 0)
                {
                    actualSize += 0x80 - paddingLeftover;
                }
                Array.Resize(ref compressedBlock, actualSize);

                // write the block offset to the new header first, along with size information.
                int currentHeaderPosition = 0x18 + i * 0x8;
                int currentDataPosition = newBlocks.Length;
                Array.Copy(BitConverter.GetBytes(currentDataPosition), 0, newHeader, currentHeaderPosition, 0x4);
                Array.Copy(BitConverter.GetBytes((short)(actualSize + 0x10)), 0, newHeader, currentHeaderPosition + 0x4, 0x2);
                Array.Copy(BitConverter.GetBytes((short)blocks[i].Length), 0, newHeader, currentHeaderPosition + 0x6, 0x2);

                // append the block to the data buffer.
                Array.Resize(ref newBlocks, newBlocks.Length + actualSize + 0x10);
                Array.Copy(BitConverter.GetBytes(0x10), 0, newBlocks, currentDataPosition, 0x4);
                Array.Copy(BitConverter.GetBytes(sourceSize), 0, newBlocks, currentDataPosition + 0x8, 0x4);
                Array.Copy(BitConverter.GetBytes(blocks[i].Length), 0, newBlocks, currentDataPosition + 0xc, 0x4);
                Array.Copy(compressedBlock, 0, newBlocks, currentDataPosition + 0x10, compressedBlock.Length);
            }

            // update the offset for the new data and update dat file number to .dat1
            Offset = newDat.Length;
            DatFile = 1;

            // now update the block count and size to the new header.
            Array.Copy(BitConverter.GetBytes(Data.Length), 0, newHeader, 0x8, 0x4);
            Array.Copy(BitConverter.GetBytes(newBlocks.Length / 0x80), 0, newHeader, 0x10, 0x4);

            // append new header and buffered blocks to the new dat file (.dat1)
            Array.Resize(ref newDat, newDat.Length + newHeader.Length + newBlocks.Length);
            Array.Copy(newHeader, 0, newDat, Offset, newHeader.Length);
            Array.Copy(newBlocks, 0, newDat, Offset + newHeader.Length, newBlocks.Length);

            // update the index so it points to our new header.
            int headerOffset = BitConverter.ToInt32(index, 0xc);
            index[headerOffset + 0x50] = 2;
            int fileOffset = BitConverter.ToInt32(index, headerOffset + 0x8);
            int fileCount = BitConverter.ToInt32(index, headerOffset + 0xc) / 0x10;
            for (int i = 0; i < fileCount; i++)
            {
                int keyOffset = fileOffset + i * 0x10;
                uint key = BitConverter.ToUInt32(index, keyOffset);
                uint directoryKey = BitConverter.ToUInt32(index, keyOffset + 0x4);

                if (key == Key && directoryKey == DirectoryKey)
                {
                    Array.Copy(BitConverter.GetBytes(WrappedOffset), 0, index, keyOffset + 0x8, 0x4);
                }
            }
        }

        // decode exh from buffered data.
        public void ReadExH()
        {
            if (Data == null || Data.Length == 0) return;

            FixedSizeDataLength = (ushort)toInt16(Data, 0x6, true);
            ushort columnCount = (ushort)toInt16(Data, 0x8, true);
            Variant = (ushort)toInt16(Data, 0x10, true);
            ushort rangeCount = (ushort)toInt16(Data, 0xa, true);
            ushort langCount = (ushort)toInt16(Data, 0xc, true);

            if (Variant != 1) return;

            Columns = new ExHColumn[columnCount];
            for (int i = 0; i < columnCount; i++)
            {
                int columnOffset = 0x20 + i * 0x4;

                Columns[i] = new ExHColumn();
                Columns[i].Type = (ushort)toInt16(Data, columnOffset, true);
                Columns[i].Offset = (ushort)toInt16(Data, columnOffset + 0x2, true);
            }
            Columns = Columns.Where(x => x.Type == 0x0).ToArray();

            Ranges = new ExHRange[rangeCount];
            for (int i = 0; i < rangeCount; i++)
            {
                int rangeOffset = (0x20 + columnCount * 0x4) + i * 0x8;

                Ranges[i] = new ExHRange();
                Ranges[i].Start = toInt32(Data, rangeOffset, true);
                Ranges[i].Length = toInt32(Data, rangeOffset + 0x4, true);
            }

            Languages = new ExHLanguage[langCount];
            for (int i = 0; i < langCount; i++)
            {
                int langOffset = ((0x20 + columnCount * 0x4) + rangeCount * 0x8) + i * 0x2;

                Languages[i] = new ExHLanguage();
                Languages[i].Value = Data[langOffset];
            }
        }

        // decode exd from buffered data.
        public void ReadExD()
        {
            int offsetTableSize = toInt32(Data, 0x8, true);
            int chunkTableSize = toInt32(Data, 0xc, true);

            byte[] offsetTable = new byte[offsetTableSize];
            Array.Copy(Data, 0x20, offsetTable, 0, offsetTableSize);

            byte[] chunkTable = new byte[chunkTableSize];
            Array.Copy(Data, 0x20 + offsetTableSize, chunkTable, 0, chunkTableSize);

            for (int i = 0; i < offsetTableSize; i += 0x8)
            {
                ExDChunk chunk = new ExDChunk();

                chunk.Key = toInt32(offsetTable, i, true);
                chunk.Offset = toInt32(offsetTable, i + 0x4, true);

                int chunkTablePosition = chunk.Offset - 0x20 - offsetTableSize;
                chunk.Size = toInt32(chunkTable, chunkTablePosition, true);
                chunk.CheckDigit = toInt16(chunkTable, chunkTablePosition + 0x4, true);

                byte[] columnDefinitions = new byte[FixedSizeDataLength];
                Array.Copy(chunkTable, chunkTablePosition + 0x6, columnDefinitions, 0, FixedSizeDataLength);

                byte[] rawData = new byte[chunk.Size - FixedSizeDataLength];
                Array.Copy(chunkTable, chunkTablePosition + 0x6 + FixedSizeDataLength, rawData, 0, rawData.Length);

                foreach (ExHColumn column in Columns)
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

        // write updated exd chunks back to buffer.
        public void WriteExD()
        {
            int offsetTableSize = toInt32(Data, 0x8, true);
            int chunkTableSize = toInt32(Data, 0xc, true);

            byte[] offsetTable = new byte[offsetTableSize];
            Array.Copy(Data, 0x20, offsetTable, 0, offsetTableSize);

            byte[] chunkTable = new byte[chunkTableSize];
            Array.Copy(Data, 0x20 + offsetTableSize, chunkTable, 0, chunkTableSize);

            byte[] newChunkTable = new byte[0];

            for (int i = 0; i < offsetTableSize; i += 0x8)
            {
                int chunkKey = toInt32(offsetTable, i, true);
                int chunkOffset = toInt32(offsetTable, i + 0x4, true);

                ExDChunk chunk = Chunks[chunkKey];

                int chunkTablePosition = chunkOffset - 0x20 - offsetTableSize;

                byte[] columnDefinitions = new byte[FixedSizeDataLength];
                Array.Copy(chunkTable, chunkTablePosition + 0x6, columnDefinitions, 0, FixedSizeDataLength);

                byte[] rawData = new byte[0];
                
                foreach (ExHColumn column in Columns)
                {
                    int fieldStart = rawData.Length;
                    Array.Copy(toBytes(fieldStart, true), 0, columnDefinitions, column.Offset, 0x4);

                    Array.Resize(ref rawData, rawData.Length + chunk.Fields[column.Offset].Length + 1);
                    Array.Copy(chunk.Fields[column.Offset], 0, rawData, fieldStart, chunk.Fields[column.Offset].Length);
                }

                int paddingLeftover = (rawData.Length + FixedSizeDataLength + 0x6 + newChunkTable.Length + offsetTableSize + 0x20) % 0x4;
                if (paddingLeftover != 0)
                {
                    rawData = rawData.Concat(new byte[0x4 - paddingLeftover]).ToArray();
                }

                byte[] newChunkHeader = new byte[0x6];
                Array.Copy(toBytes(rawData.Length + FixedSizeDataLength, true), 0, newChunkHeader, 0, 0x4);
                Array.Copy(toBytes(chunk.CheckDigit, true), 0, newChunkHeader, 0x4, 0x2);

                int newChunkOffset = newChunkTable.Length + 0x20 + offsetTableSize;
                Array.Copy(toBytes(newChunkOffset, true), 0, offsetTable, i + 0x4, 0x4);

                int curLength = newChunkTable.Length;
                Array.Resize(ref newChunkTable, newChunkTable.Length + newChunkHeader.Length + columnDefinitions.Length + rawData.Length);
                Array.Copy(newChunkHeader, 0, newChunkTable, curLength, newChunkHeader.Length);
                Array.Copy(columnDefinitions, 0, newChunkTable, curLength + newChunkHeader.Length, columnDefinitions.Length);
                Array.Copy(rawData, 0, newChunkTable, curLength + newChunkHeader.Length + columnDefinitions.Length, rawData.Length);
            }

            byte[] newBuffer = new byte[0x20 + offsetTableSize + newChunkTable.Length];
            Array.Copy(Data, 0, newBuffer, 0, 0x20);
            Array.Copy(toBytes(newChunkTable.Length, true), 0, newBuffer, 0xc, 0x4);
            Array.Copy(offsetTable, 0, newBuffer, 0x20, offsetTableSize);
            Array.Copy(newChunkTable, 0, newBuffer, 0x20 + offsetTableSize, newChunkTable.Length);

            Data = newBuffer;
        }

        // extract exd to external file.
        public void ExtractExD(string outputDir)
        {
            string exDatOutDir = Path.Combine(outputDir, Dir);
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

        // utility functions.
        private void checkEndian(ref byte[] data, bool isBigEndian)
        {
            if (isBigEndian == BitConverter.IsLittleEndian)
            {
                Array.Reverse(data);
            }
        }

        private short toInt16(byte[] buffer, int offset, bool isBigEndian)
        {
            byte[] tmp = new byte[2];
            Array.Copy(buffer, offset, tmp, 0, 2);
            checkEndian(ref tmp, isBigEndian);
            return BitConverter.ToInt16(tmp, 0);
        }

        private int toInt32(byte[] buffer, int offset, bool isBigEndian)
        {
            byte[] tmp = new byte[4];
            Array.Copy(buffer, offset, tmp, 0, 4);
            checkEndian(ref tmp, isBigEndian);
            return BitConverter.ToInt32(tmp, 0);
        }

        private byte[] toBytes(short value, bool isBigEndian)
        {
            byte[] tmp = BitConverter.GetBytes(value);
            checkEndian(ref tmp, isBigEndian);
            return tmp;
        }

        private byte[] toBytes(int value, bool isBigEndian)
        {
            byte[] tmp = BitConverter.GetBytes(value);
            checkEndian(ref tmp, isBigEndian);
            return tmp;
        }
    }
}