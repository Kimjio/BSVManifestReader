using Cyan.Utility;
using BSVManifestReader;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Cyan.Manifest
{
    public struct ManifestEntry
    {
        public byte[] name;

        public byte[] deps;

        public uint group;

        public uint priority;

        public ulong size;

        public ulong checksum;

        public string hname;

        public int kind;

        public long key;

        public enum Format
        {
            Simplified,
            Full
        }

        public class SimplifiedEntryLineReader : BSVReader.ILineParser<ManifestEntry>
        {
            public void Parse(byte[] buf, ref int offset, ref ManifestEntry dat)
            {
                dat.name = [.. BSVReader.ReadText(buf, ref offset)];
                dat.size = BSVReader.ReadVLQ(buf, ref offset, 8);
                dat.checksum = BSVReader.ReadUNum(buf, ref offset, 8);
            }
        }

        public class FullEntryLineReader : BSVReader.ILineParser<ManifestEntry>
        {
            public void Parse(byte[] buf, ref int offset, ref ManifestEntry dat)
            {
                dat.name = [.. BSVReader.ReadText(buf, ref offset)];
                dat.deps = [.. BSVReader.ReadText(buf, ref offset)];
                dat.group = (uint)BSVReader.ReadVLQ(buf, ref offset, 4);
                dat.priority = (uint)BSVReader.ReadVLQ(buf, ref offset, 4);
                dat.size = BSVReader.ReadVLQ(buf, ref offset, 8);
                dat.checksum = BSVReader.ReadUNum(buf, ref offset, 8);
                dat.key = BinaryPrimitives.ReadInt64BigEndian(buf.AsSpan(offset));
                offset += 8;
            }
        }

        public class LegacyFullEntryLineReader : BSVReader.ILineParser<ManifestEntry>
        {
            public void Parse(byte[] buf, ref int offset, ref ManifestEntry dat)
            {
                dat.name = [.. BSVReader.ReadText(buf, ref offset)];
                dat.deps = [.. BSVReader.ReadText(buf, ref offset)];
                dat.group = (uint)BSVReader.ReadVLQ(buf, ref offset, 4);
                dat.size = BSVReader.ReadVLQ(buf, ref offset, 8);
                dat.checksum = BSVReader.ReadUNum(buf, ref offset, 8);
                dat.priority = 0U;
                dat.key = 0;
            }
        }

        public static byte[] CalcHName(ulong checksum, ulong size, byte[] name)
        {
            byte[] array = new byte[16 + name.Length];
            array[0] = (byte)(checksum >> 56);
            array[1] = (byte)(checksum >> 48);
            array[2] = (byte)(checksum >> 40);
            array[3] = (byte)(checksum >> 32);
            array[4] = (byte)(checksum >> 24);
            array[5] = (byte)(checksum >> 16);
            array[6] = (byte)(checksum >> 8);
            array[7] = (byte)checksum;
            array[8] = (byte)(size >> 56);
            array[9] = (byte)(size >> 48);
            array[10] = (byte)(size >> 40);
            array[11] = (byte)(size >> 32);
            array[12] = (byte)(size >> 24);
            array[13] = (byte)(size >> 16);
            array[14] = (byte)(size >> 8);
            array[15] = (byte)size;
            Buffer.BlockCopy(name, 0, array, 16, name.Length);
            return SHA1.HashData(array);
        }

        public static string CalcHNameString(ulong checksum, ulong size, byte[] name)
        {
            return Base32Encoding.ToString(CalcHName(checksum, size, name), true);
        }

        public static BSVReader.ILineParser<ManifestEntry>? GetBsvParser(Format format, BSVReader.IBSVReader bsvReader)
        {
            BSVReader.ILineParser<ManifestEntry>? lineParser = null;
            if (format != Format.Simplified)
            {
                if (format == Format.Full)
                {
                    if (bsvReader is not BSVReader.AprioriBSVReader)
                    {
                        if (bsvReader is not BSVReader.AnonymousSchemaBSVReader)
                        {
                            throw new Exception($"Unexpceted BSV reader: {bsvReader?.GetType()?.ToString()}");
                        }
                        lineParser = new FullEntryLineReader();
                    }
                    else
                    {
                        lineParser = new LegacyFullEntryLineReader();
                    }
                }
            }
            else
            {
                lineParser = new SimplifiedEntryLineReader();
            }
            return lineParser;
        }
    }
}
