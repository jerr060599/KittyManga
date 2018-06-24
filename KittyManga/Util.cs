using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KittyManga {
    public static class Util {
        public static readonly DateTime UnixStart = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public static double ToUnixTime(this DateTime t) {
            if (t.Kind == DateTimeKind.Local)
                return (t.ToUniversalTime() - UnixStart).TotalSeconds;
            return (t - UnixStart).TotalSeconds;
        }

        public static DateTime ToDatetime(this double t) {
            return UnixStart.AddSeconds(t).ToLocalTime();
        }
    }
}
