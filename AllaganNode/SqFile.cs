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
                jArray.Add(new JObject(
                    new JProperty("FieldKey", fieldKey),
                    new JProperty("FieldValue", new UTF8Encoding(false).GetString(Fields[fieldKey])),
                    new JProperty("FieldRawValue", JsonConvert.SerializeObject(Fields[fieldKey]))));
            }

            return jObject;
        }

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
                Fields.Add((ushort)field["FieldKey"], new UTF8Encoding(false).GetBytes((string)field["FieldValue"]));
            }
        }
    }

    public class SqFile
    {
        public uint Key;
        public uint DirectoryKey;
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

        public string Name;
        public string Dir;
        public ushort Variant;
        public ushort FixedSizeDataLength;
        public ExHColumn[] Columns;
        public ExHRange[] Ranges;
        public ExHLanguage[] Languages;
        public List<SqFile> ExDats = new List<SqFile>();

        public string LanguageCode;

        public Dictionary<int, ExDChunk> Chunks = new Dictionary<int, ExDChunk>();

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

                if (BitConverter.ToInt32(header, 0x4) != 2) return;

                long length = BitConverter.ToInt32(header, 0x10) * 0x80;
                short blockCount = BitConverter.ToInt16(header, 0x14);

                using (MemoryStream ms = new MemoryStream())
                {
                    for (int i = 0; i < blockCount; i++)
                    {
                        int blockOffset = BitConverter.ToInt32(header, 0x18 + i * 0x8);

                        byte[] blockHeader = new byte[0x10];
                        br.BaseStream.Position = Offset + endOfHeader + blockOffset;
                        br.Read(blockHeader, 0, 0x10);

                        int sourceSize = BitConverter.ToInt32(blockHeader, 0x8);
                        int rawSize = BitConverter.ToInt32(blockHeader, 0xc);

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
                    }

                    Data = ms.ToArray();
                }
            }
        }

        public void WriteData(byte[] origDat, ref byte[] newDat, byte[] index)
        {
            int endOfHeader = BitConverter.ToInt32(origDat, Offset);

            byte[] header = new byte[endOfHeader];
            Array.Copy(origDat, Offset, header, 0, endOfHeader);

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

                int sourceSize = compressedBlock.Length;
                int actualSize = compressedBlock.Length;
                int paddingLeftover = (actualSize + 0x10) % 0x80;
                if (paddingLeftover != 0)
                {
                    actualSize += 0x80 - paddingLeftover;
                }
                Array.Resize(ref compressedBlock, actualSize);

                int currentHeaderPosition = 0x18 + i * 0x8;
                int currentDataPosition = newBlocks.Length;
                Array.Copy(BitConverter.GetBytes(currentDataPosition), 0, newHeader, currentHeaderPosition, 0x4);
                Array.Copy(BitConverter.GetBytes((short)(actualSize + 0x10)), 0, newHeader, currentHeaderPosition + 0x4, 0x2);
                Array.Copy(BitConverter.GetBytes((short)blocks[i].Length), 0, newHeader, currentHeaderPosition + 0x6, 0x2);

                Array.Resize(ref newBlocks, newBlocks.Length + actualSize + 0x10);
                Array.Copy(BitConverter.GetBytes(0x10), 0, newBlocks, currentDataPosition, 0x4);
                Array.Copy(BitConverter.GetBytes(sourceSize), 0, newBlocks, currentDataPosition + 0x8, 0x4);
                Array.Copy(BitConverter.GetBytes(blocks[i].Length), 0, newBlocks, currentDataPosition + 0xc, 0x4);
                Array.Copy(compressedBlock, 0, newBlocks, currentDataPosition + 0x10, compressedBlock.Length);
            }

            Offset = newDat.Length;
            DatFile = 1;

            Array.Copy(BitConverter.GetBytes(Data.Length), 0, newHeader, 0x8, 0x4);
            Array.Copy(BitConverter.GetBytes(newBlocks.Length / 0x80), 0, newHeader, 0x10, 0x4);

            Array.Resize(ref newDat, newDat.Length + newHeader.Length + newBlocks.Length);
            Array.Copy(newHeader, 0, newDat, Offset, newHeader.Length);
            Array.Copy(newBlocks, 0, newDat, Offset + newHeader.Length, newBlocks.Length);

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