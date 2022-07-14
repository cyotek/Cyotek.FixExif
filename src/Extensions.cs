namespace Cyotek.FixExif
{
  internal static class Extensions
  {
    #region Public Methods

    public static string ToExifString(this DateTime value) => value.ToString(Exif.ExifDateFormat);

    #endregion Public Methods
  }
}