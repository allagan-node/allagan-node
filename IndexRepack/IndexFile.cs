using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace IndexRepack
{
    public class IndexFileInfo
    {
        public IndexDirectoryInfo DirectoryInfo;
        public uint Key;
        public string Name;
        public uint WrappedOffset;

        public byte DatFile
        {
            get => (byte) ((WrappedOffset & 0x7) >> 1);

            set => WrappedOffset = (WrappedOffset & 0xfffffff8) | (uint) ((value & 0x3) << 1);
        }

        public uint Offset
        {
            get => (WrappedOffset & 0xfffffff8) << 3;

            set => WrappedOffset = (WrappedOffset & 0x7) | ((value >> 3) & 0xfffffff8);
        }
    }

    public class IndexDirectoryInfo
    {
        public IndexFileInfo[] FileInfo;
        public uint Key;
        public string Name;
    }

    public class IndexFile
    {
        public IndexDirectoryInfo[] DirectoryInfo;
        public byte[] IndexHeader;
        public byte[] SqPackHeader;

        public void ReadData(string indexPath)
        {
            var index = File.ReadAllBytes(indexPath);

            var sqPackHeaderLength = BitConverter.ToInt32(index, 0xc);
            SqPackHeader = new byte[sqPackHeaderLength];
            Array.Copy(index, 0, SqPackHeader, 0, sqPackHeaderLength);

            var indexHeaderLength = BitConverter.ToInt32(index, sqPackHeaderLength);
            IndexHeader = new byte[indexHeaderLength];
            Array.Copy(index, sqPackHeaderLength, IndexHeader, 0, indexHeaderLength);

            var directoryOffset = BitConverter.ToInt32(IndexHeader, 0xe4);
            var directorySize = BitConverter.ToInt32(IndexHeader, 0xe8);

            var directorySegment = new byte[directorySize];
            Array.Copy(index, directoryOffset, directorySegment, 0, directorySize);

            var directories = new List<IndexDirectoryInfo>();

            for (var i = 0; i + 0xf < directorySize; i += 0x10)
            {
                var directory = new IndexDirectoryInfo
                {
                    Key = BitConverter.ToUInt32(directorySegment, i)
                };

                var fileOffset = BitConverter.ToInt32(directorySegment, i + 0x4);
                var fileSize = BitConverter.ToInt32(directorySegment, i + 0x8);

                var fileSegment = new byte[fileSize];
                Array.Copy(index, fileOffset, fileSegment, 0, fileSize);

                var files = new List<IndexFileInfo>();

                for (var j = 0; j + 0xf < fileSize; j += 0x10)
                {
                    var file = new IndexFileInfo
                    {
                        Key = BitConverter.ToUInt32(fileSegment, j)
                    };

                    if (BitConverter.ToUInt32(fileSegment, j + 0x4) != directory.Key) throw new Exception();

                    file.WrappedOffset = BitConverter.ToUInt32(fileSegment, j + 0x8);

                    files.Add(file);
                }

                directory.FileInfo = files.ToArray();

                directories.Add(directory);
            }

            DirectoryInfo = directories.ToArray();
        }

        public byte[] RepackData(byte[] origIndex)
        {
            var sqPackHeaderLength = BitConverter.ToInt32(origIndex, 0xc);
            var indexHeaderLength = BitConverter.ToInt32(origIndex, sqPackHeaderLength);

            var sqPackHeader = new byte[sqPackHeaderLength];
            Array.Copy(origIndex, 0, sqPackHeader, 0, sqPackHeaderLength);

            var indexHeader = new byte[indexHeaderLength];
            Array.Copy(origIndex, sqPackHeaderLength, indexHeader, 0, indexHeaderLength);

            var secondSegmentOffset = BitConverter.ToInt32(indexHeader, 0x54);
            var secondSegmentSize = BitConverter.ToInt32(indexHeader, 0x58);
            var secondSegment = new byte[secondSegmentSize];
            Array.Copy(origIndex, secondSegmentOffset, secondSegment, 0, secondSegmentSize);

            var thirdSegmentOffset = BitConverter.ToInt32(indexHeader, 0x9c);
            var thirdSegmentSize = BitConverter.ToInt32(indexHeader, 0xa0);
            var thirdSegment = new byte[thirdSegmentSize];
            Array.Copy(origIndex, thirdSegmentOffset, thirdSegment, 0, thirdSegmentSize);

            using (var fileSegments = new MemoryStream())
            using (var fileWriter = new BinaryWriter(fileSegments))
            using (var directorySegments = new MemoryStream())
            using (var directoryWriter = new BinaryWriter(directorySegments))
            {
                foreach (var directory in DirectoryInfo)
                {
                    directoryWriter.Write(BitConverter.GetBytes(directory.Key));
                    directoryWriter.Write(
                        BitConverter.GetBytes(sqPackHeaderLength + indexHeaderLength + (int) fileSegments.Length));
                    directoryWriter.Write(BitConverter.GetBytes(directory.FileInfo.Length * 0x10));
                    directoryWriter.Write(new byte[4]);

                    foreach (var file in directory.FileInfo)
                    {
                        fileWriter.Write(BitConverter.GetBytes(file.Key));
                        fileWriter.Write(BitConverter.GetBytes(directory.Key));
                        fileWriter.Write(BitConverter.GetBytes(file.WrappedOffset));
                        fileWriter.Write(new byte[4]);
                    }
                }

                using (var indexStream = new MemoryStream())
                using (var indexWriter = new BinaryWriter(indexStream))
                {
                    indexWriter.Write(sqPackHeader);
                    indexWriter.Write(indexHeader);

                    var fileSegmentOffset = (int) indexStream.Length;
                    var fileSegmentSize = (int) fileSegments.Length;
                    var fileSegmentHash = new SHA1Managed().ComputeHash(fileSegments.ToArray());
                    indexWriter.Write(fileSegments.ToArray());

                    secondSegmentOffset = (int) indexStream.Length;
                    indexWriter.Write(secondSegment);

                    thirdSegmentOffset = (int) indexStream.Length;
                    indexWriter.Write(thirdSegment);

                    var directorySegmentOffset = (int) indexStream.Length;
                    var directorySegmentSize = (int) directorySegments.Length;
                    var directorySegmentHash = new SHA1Managed().ComputeHash(directorySegments.ToArray());
                    indexWriter.Write(directorySegments.ToArray());

                    var index = indexStream.ToArray();

                    Array.Copy(BitConverter.GetBytes(fileSegmentOffset), 0, index, sqPackHeaderLength + 0x8, 0x4);
                    Array.Copy(BitConverter.GetBytes(fileSegmentSize), 0, index, sqPackHeaderLength + 0xc, 0x4);
                    Array.Copy(fileSegmentHash, 0, index, sqPackHeaderLength + 0x10, fileSegmentHash.Length);

                    Array.Copy(BitConverter.GetBytes(secondSegmentOffset), 0, index, sqPackHeaderLength + 0x54, 0x4);

                    Array.Copy(BitConverter.GetBytes(thirdSegmentOffset), 0, index, sqPackHeaderLength + 0x9c, 0x4);

                    Array.Copy(BitConverter.GetBytes(directorySegmentOffset), 0, index, sqPackHeaderLength + 0xe4, 0x4);
                    Array.Copy(BitConverter.GetBytes(directorySegmentSize), 0, index, sqPackHeaderLength + 0xe8, 0x4);
                    Array.Copy(directorySegmentHash, 0, index, sqPackHeaderLength + 0xec, directorySegmentHash.Length);

                    return index;
                }
            }
        }
    }
}