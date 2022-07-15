using Cyotek.FixExif;

string path;
string[] masks;
Exif exif;

path = Environment.CurrentDirectory;
masks = new[] { "*.jpg", "*.tif" };

exif = new Exif()
//  .Verbose()
  ;

foreach (string mask in masks)
{
  foreach (string fileName in Directory.EnumerateFiles(path, mask, SearchOption.AllDirectories))
  {
    exif
      .UseFileName(fileName)
      // add missing date digitized, or fix a malformed one
      .GetTagValue("CreateDate")
      .IfMissingReplaceWith(x => x.DateFileModified.ToExifString())
      .IfInvalidDateReplaceWith(x => x.DateFileModified.ToExifString())
      // add missing original date, or fix a malformed one
      .GetTagValue("DateTimeOriginal")
      .IfMissingReplaceWith(x => x.DateFileModified.ToExifString())
      .IfInvalidDateReplaceWith(x => x.DateFileModified.ToExifString())
      // add missing date time, or fix a malformed one
      .GetTagValue("ModifyDate")
      .IfMissingReplaceWith(x => x.DateFileModified.ToExifString())
      // add missing scanner properties
      .GetTagValue("Make")
      .IfMissingReplaceWith("Canon")
      .GetTagValue("Model")
      .IfMissingReplaceWith("CanoScan LiDE 100")
      // add missing author
      .GetTagValue("Artist")
      .IfMissingReplaceWith("Richard James Moss")
      .GetTagValue("Copyright")
      .ReplaceWith(x => string.Format("Copyright (c) {0} Richard James Moss. All Rights Reserved.", x.DateFileModified.Year))
      // add missing software
      .GetTagValue("Software")
      .IfMissingReplaceWith("Cyotek QuickScan v1.0.0.0")
      .PreserveDateFileModified()
      ;
  }
}

exif
  .Preview()
  .Confirm("Save changes?", x => x.SaveChanges())
  .Dispose()
  ;
