//Copyright (c) 2018 Chi Cheng Hsu
//MIT License
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KittyManga {
    /// <summary>
    /// An encoding that encodes everything as miaows
    /// </summary>
    static class Meow {

        public static string ToMeow(string str, string meow = "miaow") {
            return ToMeowByte(Encoding.UTF8.GetBytes(str), meow);
        }

        public static string ToMeowByte(byte[] data, string meow = "miaow") {
            if (meow.Length < 2 || meow.Length > 7)
                throw new ArgumentException("Meow needs to be 3 to 7 charactors long.");
            StringBuilder sb = new StringBuilder();
            int bytesPerMeow = meow.Length - 1;
            int i = 0;
            while (i < data.Length) {
                sb.Append(meow[0]);
                for (int j = 0; j < bytesPerMeow; j++) {
                    for (int k = 7; k >= 0; k--)
                        sb.Append(((data[i] >> k) & 1) == 0 ? meow[j] : meow[j + 1]);
                    i++;
                    if (i == data.Length && j != bytesPerMeow - 1) {
                        for (; j < bytesPerMeow; j++)
                            sb.Append(meow[j]);
                        break;
                    }
                }
                sb.Append(meow[meow.Length - 1]);
                sb.Append(' ');
            }
            return sb.ToString();
        }

        public static string FromMeow(string data, string meow = "miaow") {
            return Encoding.UTF8.GetString(FromMeowByte(data, meow));
        }

        public static byte[] FromMeowByte(string data, string meow = "miaow") {
            if (meow.Length < 2 || meow.Length > 7)
                throw new ArgumentException("Meow needs to be 3 to 7 charactors long.");
            List<byte> arr = new List<byte>();
            int bytesPerMeow = meow.Length - 1;
            int i = 0;
            while (i < data.Length) {
                i++;//m
                bool delimit = false;
                for (int j = 0; j < bytesPerMeow; j++) {
                    byte b = 0x0;
                    for (int k = 0; k < 8; k++) {
                        if (i >= data.Length)
                            return arr.ToArray();
                        char c = data[i++];
                        if (c == ' ') {
                            delimit = true;
                            break;
                        }
                        b <<= 1;
                        if (c != meow[j])
                            b++;
                    }
                    if (delimit)
                        break;
                    arr.Add(b);
                }
                if (delimit)
                    continue;
                i += 2;
            }

            return arr.ToArray();
        }
    }
}
