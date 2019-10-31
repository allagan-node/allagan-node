using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace IndexRepack
{
    public class IndexFileInfo
    {
        public uint Key;
        public string Name;
        public IndexDirectoryInfo DirectoryInfo;
        public uint WrappedOffset;
        public byte DatFile
        {
            get
            {
                return (byte)((WrappedOffset & 0x7) >> 1);
            }

            set
            {
                WrappedOffset = (WrappedOffset & 0xfffffff8) | (uint)((value & 0x3) << 1);
            }
        }
        public uint Offset
        {
            get
            {
                return (WrappedOffset & 0xfffffff8) << 3;
            }

            set
            {
                WrappedOffset = (WrappedOffset & 0x7) | ((value >> 3) & 0xfffffff8);
            }
        }
    }

    public class IndexDirectoryInfo
    {
        public uint Key;
        public string Name;
        public IndexFileInfo[] FileInfo;
    }

    public class IndexFile
    {
        public byte[] SqPackHeader;
        public byte[] IndexHeader;
        public IndexDirectoryInfo[] DirectoryInfo;

        public void ReadData(byte[] index)
        {
            int sqPackHeaderLength = BitConverter.ToInt32(index, 0xc);
            SqPackHeader = new byte[sqPackHeaderLength];
            Array.Copy(index, 0, SqPackHeader, 0, sqPackHeaderLength);

            int indexHeaderLength = BitConverter.ToInt32(index, sqPackHeaderLength);
            IndexHeader = new byte[indexHeaderLength];
            Array.Copy(index, sqPackHeaderLength, IndexHeader, 0, indexHeaderLength);

            int directoryOffset = BitConverter.ToInt32(IndexHeader, 0xe4);
            int directorySize = BitConverter.ToInt32(IndexHeader, 0xe8);

            byte[] directorySegment = new byte[directorySize];
            Array.Copy(index, directoryOffset, directorySegment, 0, directorySize);

            List<IndexDirectoryInfo> directories = new List<IndexDirectoryInfo>();
            
            for (int i = 0; i + 0xf < directorySize; i += 0x10)
            {
                IndexDirectoryInfo directory = new IndexDirectoryInfo();
                directory.Key = BitConverter.ToUInt32(directorySegment, i);

                int fileOffset = BitConverter.ToInt32(directorySegment, i + 0x4);
                int fileSize = BitConverter.ToInt32(directorySegment, i + 0x8);

                byte[] fileSegment = new byte[fileSize];
                Array.Copy(index, fileOffset, fileSegment, 0, fileSize);

                List<IndexFileInfo> files = new List<IndexFileInfo>();
                
                for (int j = 0; j + 0xf < fileSize; j += 0x10)
                {
                    IndexFileInfo file = new IndexFileInfo();
                    file.Key = BitConverter.ToUInt32(fileSegment, j);

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
            int sqPackHeaderLength = BitConverter.ToInt32(origIndex, 0xc);
            int indexHeaderLength = BitConverter.ToInt32(origIndex, sqPackHeaderLength);

            byte[] sqPackHeader = new byte[sqPackHeaderLength];
            Array.Copy(origIndex, 0, sqPackHeader, 0, sqPackHeaderLength);

            byte[] indexHeader = new byte[indexHeaderLength];
            Array.Copy(origIndex, sqPackHeaderLength, indexHeader, 0, indexHeaderLength);

            int secondSegmentOffset = BitConverter.ToInt32(indexHeader, 0x54);
            int secondSegmentSize = BitConverter.ToInt32(indexHeader, 0x58);
            byte[] secondSegment = new byte[secondSegmentSize];
            Array.Copy(origIndex, secondSegmentOffset, secondSegment, 0, secondSegmentSize);

            int thirdSegmentOffset = BitConverter.ToInt32(indexHeader, 0x9c);
            int thirdSegmentSize = BitConverter.ToInt32(indexHeader, 0xa0);
            byte[] thirdSegment = new byte[thirdSegmentSize];
            Array.Copy(origIndex, thirdSegmentOffset, thirdSegment, 0, thirdSegmentSize);

            using (MemoryStream fileSegments = new MemoryStream())
            using (BinaryWriter fileWriter = new BinaryWriter(fileSegments))
            using (MemoryStream directorySegments = new MemoryStream())
            using (BinaryWriter directoryWriter = new BinaryWriter(directorySegments))
            {
                foreach (IndexDirectoryInfo directory in DirectoryInfo)
                {
                    directoryWriter.Write(BitConverter.GetBytes(directory.Key));
                    directoryWriter.Write(BitConverter.GetBytes(sqPackHeaderLength + indexHeaderLength + (int)fileSegments.Length));
                    directoryWriter.Write(BitConverter.GetBytes(directory.FileInfo.Length * 0x10));
                    directoryWriter.Write(new byte[4]);

                    foreach (IndexFileInfo file in directory.FileInfo)
                    {
                        fileWriter.Write(BitConverter.GetBytes(file.Key));
                        fileWriter.Write(BitConverter.GetBytes(directory.Key));
                        fileWriter.Write(BitConverter.GetBytes(file.WrappedOffset));
                        fileWriter.Write(new byte[4]);
                    }
                }

                using (MemoryStream indexStream = new MemoryStream())
                using (BinaryWriter indexWriter = new BinaryWriter(indexStream))
                {
                    indexWriter.Write(sqPackHeader);
                    indexWriter.Write(indexHeader);

                    int fileSegmentOffset = (int)indexStream.Length;
                    int fileSegmentSize = (int)fileSegments.Length;
                    byte[] fileSegmentHash = new SHA1Managed().ComputeHash(fileSegments.ToArray());
                    indexWriter.Write(fileSegments.ToArray());

                    secondSegmentOffset = (int)indexStream.Length;
                    indexWriter.Write(secondSegment);

                    thirdSegmentOffset = (int)indexStream.Length;
                    indexWriter.Write(thirdSegment);

                    int directorySegmentOffset = (int)indexStream.Length;
                    int directorySegmentSize = (int)directorySegments.Length;
                    byte[] directorySegmentHash = new SHA1Managed().ComputeHash(directorySegments.ToArray());
                    indexWriter.Write(directorySegments.ToArray());

                    byte[] index = indexStream.ToArray();

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