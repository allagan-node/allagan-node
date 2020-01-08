using System.Collections.Generic;
using System.Linq;

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

    public class ExHFile : SqFile
    {
        // name of the header.
        public string HeaderName;

        // header data.
        public ushort Variant;
        public ushort FixedSizeDataLength;
        public ExHColumn[] Columns;
        public ExHRange[] Ranges;
        public ExHLanguage[] Languages;

        // child exDats.
        public List<ExDFile> ExDats = new List<ExDFile>();

        // decode exh from buffered data.
        public void ReadExH()
        {
            byte[] data = ReadData();

            if (data == null || data.Length == 0) return;

            FixedSizeDataLength = (ushort)toInt16(data, 0x6, true);
            ushort columnCount = (ushort)toInt16(data, 0x8, true);
            Variant = (ushort)toInt16(data, 0x10, true);
            ushort rangeCount = (ushort)toInt16(data, 0xa, true);
            ushort langCount = (ushort)toInt16(data, 0xc, true);

            if (Variant != 1) return;

            Columns = new ExHColumn[columnCount];
            for (int i = 0; i < columnCount; i++)
            {
                int columnOffset = 0x20 + i * 0x4;

                Columns[i] = new ExHColumn();
                Columns[i].Type = (ushort)toInt16(data, columnOffset, true);
                Columns[i].Offset = (ushort)toInt16(data, columnOffset + 0x2, true);
            }
            Columns = Columns.Where(x => x.Type == 0x0).ToArray();

            Ranges = new ExHRange[rangeCount];
            for (int i = 0; i < rangeCount; i++)
            {
                int rangeOffset = (0x20 + columnCount * 0x4) + i * 0x8;

                Ranges[i] = new ExHRange();
                Ranges[i].Start = toInt32(data, rangeOffset, true);
                Ranges[i].Length = toInt32(data, rangeOffset + 0x4, true);
            }

            Languages = new ExHLanguage[langCount];
            for (int i = 0; i < langCount; i++)
            {
                int langOffset = ((0x20 + columnCount * 0x4) + rangeCount * 0x8) + i * 0x2;

                Languages[i] = new ExHLanguage();
                Languages[i].Value = data[langOffset];
            }
        }
    }
}
