using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace AllaganNode
{
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

        // full file name.
        public string Name;

        // full directory.
        public string Dir;

        // physical path to dat file.
        public string DatPath;

        public void Copy(SqFile copy)
        {
            Key = copy.Key;
            DirectoryKey = copy.DirectoryKey;
            WrappedOffset = copy.WrappedOffset;

            Name = copy.Name;
            Dir = copy.Dir;
            DatPath = copy.DatPath;
        }

        // read data blocks and uncompress them.
        public byte[] ReadData()
        {
            using (FileStream fs = File.OpenRead(DatPath))
            using (BinaryReader br = new BinaryReader(fs))
            {
                br.BaseStream.Position = Offset;
                int endOfHeader = br.ReadInt32();

                byte[] header = new byte[endOfHeader];
                br.BaseStream.Position = Offset;
                br.Read(header, 0, endOfHeader);

                // 4th byte denotes the type of data, which should be 2 for binary files.
                if (BitConverter.ToInt32(header, 0x4) != 2) return null;

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

                        int magic = BitConverter.ToInt32(blockHeader, 0);
                        if (magic != 0x10) throw new Exception();

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

                    return ms.ToArray();
                }
            }
        }

        // repack given data buffer to dat format.
        public byte[] RepackData(byte[] origDat, byte[] data)
        {
            int endOfHeader = BitConverter.ToInt32(origDat, Offset);

            byte[] header = new byte[endOfHeader];
            Array.Copy(origDat, Offset, header, 0, endOfHeader);

            // divide up data to blocks with max size 0x3e80
            List<byte[]> blocks = new List<byte[]>();
            int position = 0;
            while (position < data.Length)
            {
                int blockLength = Math.Min(0x3e80, data.Length - position);
                byte[] tmp = new byte[blockLength];
                Array.Copy(data, position, tmp, 0, blockLength);
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

            // now update the block count and size to the new header.
            Array.Copy(BitConverter.GetBytes(data.Length), 0, newHeader, 0x8, 0x4);
            Array.Copy(BitConverter.GetBytes(newBlocks.Length / 0x80), 0, newHeader, 0x10, 0x4);

            // construct new dat by adding new header and buffered blocks.
            byte[] newDat = new byte[newHeader.Length + newBlocks.Length];
            Array.Copy(newHeader, 0, newDat, 0, newHeader.Length);
            Array.Copy(newBlocks, 0, newDat, newHeader.Length, newBlocks.Length);

            return newDat;
        }

        /*public byte[] RepackData(string origDatPath, string newDatPath, byte[] index)
        {
            byte[] origDat = File.ReadAllBytes(origDatPath);

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
        }*/

        public void UpdateOffset(int offset, byte datFile, byte[] index)
        {
            Offset = offset;
            DatFile = datFile;

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

        // utility functions.
        protected void checkEndian(ref byte[] data, bool isBigEndian)
        {
            if (isBigEndian == BitConverter.IsLittleEndian)
            {
                Array.Reverse(data);
            }
        }

        protected short toInt16(byte[] buffer, int offset, bool isBigEndian)
        {
            byte[] tmp = new byte[2];
            Array.Copy(buffer, offset, tmp, 0, 2);
            checkEndian(ref tmp, isBigEndian);
            return BitConverter.ToInt16(tmp, 0);
        }

        protected int toInt32(byte[] buffer, int offset, bool isBigEndian)
        {
            byte[] tmp = new byte[4];
            Array.Copy(buffer, offset, tmp, 0, 4);
            checkEndian(ref tmp, isBigEndian);
            return BitConverter.ToInt32(tmp, 0);
        }

        protected byte[] toBytes(short value, bool isBigEndian)
        {
            byte[] tmp = BitConverter.GetBytes(value);
            checkEndian(ref tmp, isBigEndian);
            return tmp;
        }

        protected byte[] toBytes(int value, bool isBigEndian)
        {
            byte[] tmp = BitConverter.GetBytes(value);
            checkEndian(ref tmp, isBigEndian);
            return tmp;
        }
    }
}