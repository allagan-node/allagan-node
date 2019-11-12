using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AllaganNode
{
    public enum TagType
    {
        DayAndHour = 0x6,
        Time = 0x7,
        If = 0x8,
        Switch = 0x9,
        IfEquals = 0xc,
        UnknownA = 0xa,
        LineBreak = 0x10,
        Gui = 0x12,
        Color = 0x13,
        Unknown14 = 0x14,
        SoftHyphen = 0x16,
        Unknown17 = 0x17,
        Emphasis19 = 0x19,
        Emphasis1A = 0x1a,
        Indent = 0x1d,
        CommandIcon = 0x1e,
        Dash = 0x1f,
        Value = 0x20,
        Format = 0x22,
        PadDigits = 0x24,
        Unknown26 = 0x26,
        Sheet = 0x28,
        Highlight = 0x29,
        Clickable = 0x2b,
        Split = 0x2c,
        Unknown2D = 0x2d,
        Fixed = 0x2e,
        Unknown2F = 0x2f,
        SheetJa = 0x30,
        SheetEn = 0x31,
        SheetDe = 0x32,
        SheetFr = 0x33,
        InstanceContent = 0x40,
        UiForeground = 0x48,
        UiGlow = 0x49,
        Padded = 0x50,
        Unknown51 = 0x51,
        Unknown60 = 0x60
    }

    public enum ExpressionType
    {
        // >=
        GTE = 0xe0,
        // >
        GT = 0xe1,
        // >=
        LTE = 0xe2,
        // <
        LT = 0xe3,
        // =
        Equal = 0xe4,
        // !=
        NotEqual = 0xe5,

        // keys
        IntegerKey = 0xe8,
        PlayerKey = 0xe9,
        InstanceKey = 0xeb,

        // data types
        Byte = 0xf0,
        Int16F2 = 0xf2,
        Int16F4 = 0xf4,
        Int24 = 0xf6,
        Int32 = 0xfe,
        Entry = 0xff
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
                EncodeEntry(Fields[fieldKey], entryArray);

                jArray.Add(new JObject(
                    new JProperty("FieldKey", fieldKey),
                    new JProperty("FieldValue", entryArray)));
            }

            return jObject;
        }

        // encode bytes into legible json entry object.
        private void EncodeEntry(byte[] entry, JArray jArray)
        {
            if (entry.Length == 0) return;

            // if no tags, just encode it with UTF8.
            if (!entry.Contains((byte)0x2))
            {
                jArray.Add(new JObject(
                    new JProperty("EntryType", "Text"),
                    new JProperty("EntryValue", new UTF8Encoding(false).GetString(entry))));
            }
            else
            {
                int tagIndex = Array.FindIndex(entry, b => b == 0x2);

                // if start byte is opening of tag, treat it as tag.
                if (tagIndex == 0)
                {
                    EncodeTag(entry, jArray);
                }
                // divide text part and tag part.
                else
                {
                    byte[] head = new byte[tagIndex];
                    Array.Copy(entry, 0, head, 0, tagIndex);

                    byte[] tag = new byte[entry.Length - tagIndex];
                    Array.Copy(entry, tagIndex, tag, 0, tag.Length);

                    EncodeEntry(head, jArray);
                    EncodeTag(tag, jArray);
                }
            }
        }

        // encode bytes into legible json tag object.
        private void EncodeTag(byte[] tag, JArray jArray)
        {
            if (tag.Length == 0) return;

            // if start byte is not opening of tag, treat it as entry.
            if (tag[0] != 0x2)
            {
                EncodeEntry(tag, jArray);
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

                byte[] tail = SplitByLength(ref tagData);

                // check tag closing byte.
                if (tail[0] != 0x3) throw new Exception();

                jArray.Add(new JObject(
                    new JProperty("EntryType", "Tag"),
                    new JProperty("EntryValue", CreateTagJObject(tagType, tagData))));

                // remaining entry after tag closing byte
                byte[] entry = new byte[tail.Length - 1];
                Array.Copy(tail, 1, entry, 0, entry.Length);
                EncodeEntry(entry, jArray);
            }
        }

        // Parses length byte and splits the given data.
        private byte[] SplitByLength(ref byte[] tag)
        {
            // [0] -> byte (type of length)
            // [..] -> length data (depending on type of length)
            // [..length..] -> data

            byte lengthType = tag[0];
            byte[] tagData;
            byte[] tail;

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

            return tail;
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

                byte[] tail = SplitByLength(ref field);

                // now treat it as full-blown field.
                JArray fieldArray = new JArray();
                EncodeEntry(field, fieldArray);
                jArray.Add(new JObject(
                    new JProperty("TokenType", "field"),
                    new JProperty("TokenValue", fieldArray)));

                // parse the rest of the tag.
                ParseTag(tail, jArray);
            }
        }

        private byte[] ParseExpression(byte[] payload, JObject expressionObject)
        {
            byte[] tail;

            // payload just contains normal byte value
            if (payload[0] < 0xe0)
            {
                expressionObject.Add(new JProperty("ExpressionType", "Raw"));
                expressionObject.Add(new JProperty("ExpressionValue", payload[0]));

                tail = payload.Skip(1).ToArray();
            }
            else
            {
                expressionObject.Add(new JProperty("ExpressionType", ((ExpressionType)payload[0]).ToString()));

                JProperty expressionValue = new JProperty("ExpressionValue", null);

                switch ((ExpressionType)payload[0])
                {
                    // followed by two expressions (left and right).
                    case ExpressionType.GTE:
                    case ExpressionType.GT:
                    case ExpressionType.LTE:
                    case ExpressionType.LT:
                    case ExpressionType.Equal:
                    case ExpressionType.NotEqual:
                        // left side could be an expression or raw value
                        JObject leftExpression = new JObject();
                        tail = ParseExpression(payload.Skip(1).ToArray(), leftExpression);

                        // right side could be an expression or raw value
                        JObject rightExpression = new JObject();
                        tail = ParseExpression(tail, rightExpression);

                        expressionValue.Value = new JObject(
                            new JProperty("Left", leftExpression),
                            new JProperty("Right", rightExpression));
                        break;

                    // followed by one expression.
                    case ExpressionType.IntegerKey:
                    case ExpressionType.PlayerKey:
                    case ExpressionType.InstanceKey:
                        JObject parameterValue = new JObject();
                        tail = ParseExpression(payload.Skip(1).ToArray(), parameterValue);

                        expressionValue.Value = parameterValue;
                        break;

                    // followed by one byte.
                    case ExpressionType.Byte:
                        expressionValue.Value = payload[1];
                        tail = payload.Skip(2).ToArray();
                        break;

                    // followed by int16 (2 bytes).
                    case ExpressionType.Int16F2:
                    case ExpressionType.Int16F4:
                        byte[] int16 = new byte[2];
                        Array.Copy(payload, 1, int16, 0, 2);
                        // reverse it if bitconverter is little endian.
                        if (BitConverter.IsLittleEndian) Array.Reverse(int16);
                        expressionValue.Value = BitConverter.ToInt16(int16, 0);
                        tail = payload.Skip(3).ToArray();
                        break;

                    // followed by int24 (3 bytes).
                    case ExpressionType.Int24:
                        byte[] int24 = new byte[4];
                        // pad 0 in front so that it can be converted to int32.
                        int24[0] = 0;
                        Array.Copy(payload, 1, int24, 1, 3);
                        if (BitConverter.IsLittleEndian) Array.Reverse(int24);

                        expressionValue.Value = BitConverter.ToInt32(int24, 0);
                        tail = payload.Skip(4).ToArray();
                        break;

                    // followed by int32 (4 bytes).
                    case ExpressionType.Int32:
                        byte[] int32 = new byte[4];
                        Array.Copy(payload, 1, int32, 0, 4);
                        if (BitConverter.IsLittleEndian) Array.Reverse(int32);
                        expressionValue.Value = BitConverter.ToInt32(int32, 0);
                        tail = payload.Skip(5).ToArray();
                        break;

                    // followed by a whole entry.
                    case ExpressionType.Entry:
                        byte[] entry = payload.Skip(1).ToArray();
                        tail = SplitByLength(ref entry);

                        JArray entryArray = new JArray();
                        EncodeEntry(entry, entryArray);
                        expressionValue.Value = entryArray;
                        break;

                    // unknowns...
                    default:
                        Console.WriteLine();
                        Console.WriteLine(new JObject(new JProperty("UNKNOWN", payload)));
                        throw new Exception();
                }

                expressionObject.Add(expressionValue);
            }

            return tail;
        }

        private JObject CreateTagJObject(byte tagType, byte[] tagData)
        {
            byte[] tail;

            JObject jObject = new JObject(
                new JProperty("TagType", ((TagType)tagType).ToString()));
            
            // parse tags
            switch ((TagType)tagType)
            {
                // this tag contains day and hour info
                case TagType.DayAndHour:
                    if (tagData.Length == 0 || tagData.Length > 2) throw new Exception();

                    JObject dayAndHour = new JObject();

                    // first bit is hour
                    int hour = tagData[0] - 1;
                    dayAndHour.Add(new JProperty("Hour", hour));

                    // second bit is day
                    if (tagData.Length == 2)
                    {
                        string day = string.Empty;

                        switch (tagData[1] % 7)
                        {
                            case 0:
                                day = "Saturday";
                                break;
                            case 1:
                                day = "Sunday";
                                break;
                            case 2:
                                day = "Monday";
                                break;
                            case 3:
                                day = "Tuesday";
                                break;
                            case 4:
                                day = "Wednesday";
                                break;
                            case 5:
                                day = "Thursday";
                                break;
                            case 6:
                                day = "Friday";
                                break;
                        }

                        dayAndHour.Add(new JProperty("Day", day));
                    }

                    jObject.Add(new JProperty("TagValue", dayAndHour));
                    return jObject;

                case TagType.Time:
                    break;

                // if statement
                case TagType.If:
                    // starts with expression that denotes condition for the if statement.
                    JObject conditionExpression = new JObject();
                    tail = ParseExpression(tagData, conditionExpression);

                    // followed by expression that will show up when condition is true.
                    JObject trueExpression = new JObject();
                    tail = ParseExpression(tail, trueExpression);

                    // followed by expression that will show up when condition is false.
                    JObject falseExpression = new JObject();
                    tail = ParseExpression(tail, falseExpression);
                    
                    // there should not be any tag data left...
                    if (tail.Length != 0) throw new Exception();

                    jObject.Add(new JProperty("TagValue", new JObject(
                        new JProperty("Condition", conditionExpression),
                        new JProperty("True", trueExpression),
                        new JProperty("False", falseExpression))));
                    return jObject;

                case TagType.Switch:
                    break;

                case TagType.IfEquals:
                    break;

                case TagType.UnknownA:
                    break;

                case TagType.LineBreak:
                    break;

                case TagType.Gui:
                    break;

                case TagType.Color:
                    break;

                case TagType.Unknown14:
                    break;

                case TagType.SoftHyphen:
                    break;

                case TagType.Unknown17:
                    break;

                case TagType.Emphasis19:
                    break;

                case TagType.Emphasis1A:
                    break;

                case TagType.Indent:
                    break;

                case TagType.CommandIcon:
                    break;

                case TagType.Dash:
                    break;

                case TagType.Value:
                    break;

                case TagType.Format:
                    break;

                case TagType.PadDigits:
                    break;

                case TagType.Unknown26:
                    break;

                case TagType.Sheet:
                    break;

                case TagType.Highlight:
                    break;

                case TagType.Clickable:
                    break;

                case TagType.Split:
                    break;

                case TagType.Unknown2D:
                    break;

                case TagType.Fixed:
                    break;

                case TagType.Unknown2F:
                    break;

                case TagType.SheetJa:
                    break;

                case TagType.SheetEn:
                    break;

                case TagType.SheetDe:
                    break;

                case TagType.SheetFr:
                    break;

                case TagType.InstanceContent:
                    break;

                case TagType.UiForeground:
                    break;

                case TagType.UiGlow:
                    break;

                case TagType.Padded:
                    break;

                case TagType.Unknown51:
                    break;

                case TagType.Unknown60:
                    break;
            }

            // I'm skipping unknown tag types for now, but will have to work on this at some point...
            jObject.Add(new JProperty("TagValue", tagData));
            return jObject;

            throw new Exception();
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
