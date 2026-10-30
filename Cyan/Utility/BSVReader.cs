using K4os.Compression.LZ4.Streams;

namespace Cyan.Utility
{
    public class BSVReader
    {
        public enum ReadMode
        {
            Memory,
            Stream
        }

        private enum Phase
        {
            Header,
            Body,
            End
        }

        private enum HeaderPhaseApriori : uint
        {
            Start,
            RowCount,
            MaxRowSize
        }

        public enum Format : byte
        {
            Apriori,
            AnonymousSchema
        }

        public interface IBSVReader : IDisposable
        {
            ulong GetRowCount();

            Format GetFormat();

            bool ReadLine<T>(ILineParser<T> parser, ref T dat);
        }

        public class Schema(Schema.Type type, ulong? fixedSize = null)
        {
            public enum Type : byte
            {
                UShort = 16,
                UInt,
                ULong,
                UNum = 32,
                UNumFixed,
                Blob = 48,
                BlobFixed,
                Text = 64,
                TextFixed
            }

            public readonly Type type = type;

            public readonly ulong? fixedSize = fixedSize;
        }

        public interface ILineParser<T>
        {
            void Parse(byte[] buf, ref int offset, ref T dat);
        }

        internal abstract class BaseBSVReader : IBSVReader, IDisposable
        {
            public ulong GetRowCount()
            {
                return rowCount;
            }

            public Format GetFormat()
            {
                return format;
            }

            public BaseBSVReader(Format format, byte[] buf, int offset)
            {
                this.buf = buf;
                this.offset = offset;
                this.format = format;
            }

            public bool ReadLine<T>(ILineParser<T> parser, ref T dat)
            {
                if (row != rowCount)
                {
                    parser.Parse(buf, ref offset, ref dat);
                    row += 1UL;
                    return true;
                }
                if (offset != buf.Length)
                {
                    throw new Exception(string.Format("Row count mismatch: expecting {0} rows, {1}B left", rowCount, buf.Length - offset));
                }
                return false;
            }

            public abstract void Dispose();

            protected readonly byte[] buf;

            protected int offset;

            protected ulong rowCount;

            protected ulong maxRowSize;

            protected ulong row;

            protected readonly Format format;
        }

        internal class AprioriBSVReader : BaseBSVReader
        {
            public AprioriBSVReader(byte[] buf, int offset)
                : base(Format.Apriori, buf, offset)
            {
                rowCount = ReadVLQ(buf, ref this.offset, 8);
                maxRowSize = ReadVLQ(buf, ref this.offset, 8);
            }

            public override void Dispose()
            {
            }
        }

        internal class AnonymousSchemaBSVReader : BaseBSVReader
        {
            private readonly ushort headerSize;

            private readonly uint schemaCount;

            private readonly uint schemaVersion;

            private readonly Schema[] schemas;

            internal uint GetSchemaCount()
            {
                return schemaCount;
            }

            internal uint GetSchemaVersion()
            {
                return schemaVersion;
            }

            public AnonymousSchemaBSVReader(byte[] buf, int offset)
                : base(Format.AnonymousSchema, buf, offset)
            {
                headerSize = (ushort)ReadUNum(buf, ref this.offset, 2);
                rowCount = ReadVLQ(buf, ref this.offset, 8);
                maxRowSize = ReadVLQ(buf, ref this.offset, 8);
                schemaVersion = (uint)ReadVLQ(buf, ref this.offset, 4);
                schemaCount = (uint)ReadVLQ(buf, ref this.offset, 4);
                schemas = new Schema[schemaCount];
                int num = 0;
                while (num < (long)((ulong)schemaCount))
                {
                    Schema.Type type = (Schema.Type)ReadUNum(buf, ref this.offset, 1);
                    ulong? num2 = null;
                    if (type is Schema.Type.UNumFixed or Schema.Type.BlobFixed or Schema.Type.TextFixed)
                    {
                        num2 = new ulong?(ReadVLQ(buf, ref this.offset, 8));
                    }
                    schemas[num] = new Schema(type, num2);
                    num++;
                }
            }

            public override void Dispose()
            {
            }
        }

        public static ulong ReadVLQ(byte[] buf, ref int offset, int max_bytes = 8)
        {
            ulong num = 0UL;
            int i = offset;
            while (i < buf.Length)
            {
                num = (num << 7) | (buf[i] & 127UL);
                if ((buf[i] & 128) == 0)
                {
                    offset = i + 1;
                    if ((i - offset) * 7 >= max_bytes * 8)
                    {
                        throw new Exception(string.Format("VLQ byte width overflow at {0}: {1} > {2}", offset, i - offset, max_bytes));
                    }
                    return num;
                }
                else
                {
                    i++;
                }
            }
            throw new Exception(string.Format("Could not decode VLQ at {0}", offset));
        }

        public static ulong ReadUNum(byte[] buf, ref int offset, int bytes)
        {
            ulong num = 0UL;
            if (offset + bytes > buf.Length)
            {
                throw new Exception(string.Format("Could not decode UNum[{0}] at {1}", bytes, offset));
            }
            for (int i = 0; i < bytes; i++)
            {
                num = (num << 8) | buf[offset + i];
            }
            offset += bytes;
            return num;
        }

        public static ArraySegment<byte> ReadText(byte[] buf, ref int offset)
        {
            for (int i = offset; i < buf.Length; i++)
            {
                if (buf[i] == 0)
                {
                    ArraySegment<byte> arraySegment = new(buf, offset, i - offset);
                    offset = i + 1;
                    return arraySegment;
                }
            }
            throw new Exception(string.Format("Non-null terminated string detected at {0}", offset));
        }

        public static ArraySegment<byte> ReadFixedBlob(byte[] buf, ref int offset, int bytes)
        {
            ArraySegment<byte> arraySegment = new(buf, offset, bytes);
            offset += bytes;
            return arraySegment;
        }

        public static ArraySegment<byte> ReadFixedText(byte[] buf, ref int offset, int bytes)
        {
            return ReadFixedBlob(buf, ref offset, bytes);
        }

        public static ArraySegment<byte> ReadBlob(byte[] buf, ref int offset)
        {
            ulong num = ReadVLQ(buf, ref offset, 8);
            return ReadFixedBlob(buf, ref offset, (int)num);
        }

        internal static IBSVReader Init(string path, ReadMode mode, bool isCompressed)
        {
            if (mode == ReadMode.Memory)
            {
                if (isCompressed)
                {
                    using (var source = LZ4Stream.Decode(File.OpenRead(path)))
                    {
                        using (var target = new MemoryStream())
                        {
                            source.CopyTo(target);
                            return CreateReaderFromHeader(target.ToArray());
                        }
                    }
                }
                return CreateReaderFromHeader(File.ReadAllBytes(path));
            }
            throw new Exception(string.Format("Unsupported mode: {0}", mode));
        }

        internal static IBSVReader Init(Stream stream, ReadMode mode, bool isCompressed)
        {
            if (mode == ReadMode.Memory)
            {
                using var target = new MemoryStream();
                if (isCompressed)
                {
                    using var source = LZ4Stream.Decode(stream);
                    source.CopyTo(target);
                    return CreateReaderFromHeader(target.ToArray());
                }
                stream.CopyTo(target);
                return CreateReaderFromHeader(target.ToArray());
            }
            throw new Exception(string.Format("Unsupported mode: {0}", mode));
        }

        private static IBSVReader CreateReaderFromHeader(byte[] buf)
        {
            ArgumentNullException.ThrowIfNull(buf, nameof(buf));
            if (buf.Length < 2)
            {
                throw new Exception("Could not read BSV header: buffer too short");
            }
            if (buf[0] != 191)
            {
                throw new Exception(string.Format("BSV magic mismatch: 0x{0:X2}", buf[0]));
            }
            int num = buf[1] >> 4;
            if (num != 1)
            {
                throw new Exception(string.Format("BSV version mismatch: {0}", num));
            }
            byte b = (byte)(buf[1] & 15);
            int num2 = 2;
            if (b == 0)
            {
                return new AprioriBSVReader(buf, num2);
            }
            if (b != 1)
            {
                throw new Exception(string.Format("BSV unsupported format: {0}", b));
            }
            return new AnonymousSchemaBSVReader(buf, num2);
        }
    }
}
