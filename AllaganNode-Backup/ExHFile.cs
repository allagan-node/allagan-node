using System.Collections.Generic;
using System.Linq;

namespace AllaganNode
{
    public class ExHColumn
    {
        public ushort Offset;
        public ushort Type;
    }

    public class ExHRange
    {
        public int Length;
        public int Start;
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
        public ExHColumn[] Columns;

        // child exDats.
        public List<ExDFile> ExDats = new List<ExDFile>();

        public ushort FixedSizeDataLength;

        // name of the header.
        public string HeaderName;
        public ExHLanguage[] Languages;
        public ExHRange[] Ranges;

        // header data.
        public ushort Variant;

        // decode exh from buffered data.
        public void ReadExH()
        {
            var data = ReadData();

            if (data == null || data.Length == 0) return;

            FixedSizeDataLength = (ushort) toInt16(data, 0x6, true);
            var columnCount = (ushort) toInt16(data, 0x8, true);
            Variant = (ushort) toInt16(data, 0x10, true);
            var rangeCount = (ushort) toInt16(data, 0xa, true);
            var langCount = (ushort) toInt16(data, 0xc, true);

            if (Variant != 1) return;

            Columns = new ExHColumn[columnCount];
            for (var i = 0; i < columnCount; i++)
            {
                var columnOffset = 0x20 + i * 0x4;

                Columns[i] = new ExHColumn
                {
                    Type = (ushort) toInt16(data, columnOffset, true),
                    Offset = (ushort) toInt16(data, columnOffset + 0x2, true)
                };
            }

            Columns = Columns.Where(x => x.Type == 0x0).ToArray();

            Ranges = new ExHRange[rangeCount];
            for (var i = 0; i < rangeCount; i++)
            {
                var rangeOffset = 0x20 + columnCount * 0x4 + i * 0x8;

                Ranges[i] = new ExHRange
                {
                    Start = toInt32(data, rangeOffset, true),
                    Length = toInt32(data, rangeOffset + 0x4, true)
                };
            }

            Languages = new ExHLanguage[langCount];
            for (var i = 0; i < langCount; i++)
            {
                var langOffset = 0x20 + columnCount * 0x4 + rangeCount * 0x8 + i * 0x2;

                Languages[i] = new ExHLanguage
                {
                    Value = data[langOffset]
                };
            }
        }
    }
}