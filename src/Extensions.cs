using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cyotek.FixExif
{
  internal static class Extensions
  {
    public static string ToExifString(this DateTime value) => value.ToString(Exif.ExifDateFormat);
  }
}
