namespace BSVManifestReader
{
    public static class Base32Encoding
    {
        private const char Padding = '=';

        public static string ToString(byte[] input, bool padding = true)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (input.Length == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(input));
            }
            char[] array = new char[(int)Math.Ceiling(input.Length / 5.0) * 8];
            int i = 0;
            int num = 0;
            while (i < input.Length)
            {
                long num2 = 0L;
                int num3 = Math.Min(5, input.Length - i);
                for (int j = 0; j < num3; j++)
                {
                    num2 += (long)(ulong)input[i + j] << 8 * (4 - j);
                }
                int num4 = 8;
                switch (num3)
                {
                    case 1:
                        num4 = 2;
                        break;
                    case 2:
                        num4 = 4;
                        break;
                    case 3:
                        num4 = 5;
                        break;
                    case 4:
                        num4 = 7;
                        break;
                }
                for (int k = 0; k < num4; k++)
                {
                    byte b = (byte)((num2 >> 5 * (7 - k)) & 31L);
                    array[num + k] = ByteToChar(b);
                }
                if (padding)
                {
                    for (int l = num4; l < 8; l++)
                    {
                        array[num + l] = Padding;
                    }
                    num4 = 8;
                }
                i += 5;
                num += num4;
            }
            return new string(array, 0, num);
        }

        public static byte[] ToBytes(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                throw new ArgumentNullException(nameof(input));
            }
            input = input.TrimEnd([Padding]);
            int num = input.Length * 5 / 8;
            byte[] array = new byte[num];
            int i = 0;
            int num2 = 0;
            while (i < input.Length)
            {
                long num3 = 0L;
                int num4 = Math.Min(8, input.Length - i);
                for (int j = 0; j < num4; j++)
                {
                    long num5 = (long)Base32Encoding.CharToByte(input[i + j]);
                    num3 += num5 << 5 * (7 - j);
                }
                int num6 = Math.Min(5, num - num2);
                for (int k = 0; k < num6; k++)
                {
                    array[num2 + k] = (byte)((num3 >> 8 * (4 - k)) & 255L);
                }
                i += 8;
                num2 += 5;
            }
            return array;
        }

        private static char ByteToChar(byte b)
        {
            if (b < 26)
            {
                return (char)(b + 65);
            }
            if (b < 32)
            {
                return (char)(b + 24);
            }
            throw new ArgumentException("ここに来ることはない（ロジックが間違っている）: " + b.ToString(), nameof(b));
        }

        private static int CharToByte(char c)
        {
            if ('A' <= c && c <= 'Z')
            {
                return c - 'A';
            }
            if ('2' <= c && c <= '7')
            {
                return c - '\u0018';
            }
            if ('a' <= c && c <= 'z')
            {
                return c - 'a';
            }
            throw new ArgumentException("Base32エンコーディング後の文字列ではありません: " + c.ToString(), nameof(c));
        }
    }
}
