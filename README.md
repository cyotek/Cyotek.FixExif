# Cyotek.FixExif.exe

> This tool is not really designed for ease of use and must be
> modified to suit your own needs. I say again, not general
> purpose, do not run without changing the code!

## Overview

I've been creating a catalog of a passel of items, using flatbed
scanners where possible, otherwise a camera. I recently changed
my [QuickScan][quickscan] application to add the ability to add
EXIF data to the scanned images. However, what I failed to
realise was I somehow read the spec wrong and all the timestamps
I saved were in the wrong format, so once I discovered that I
had a small pile of images to fix. In addition, I wanted to
update all existing images to include timestamps and to add
copyright, author and software information to the existing data,
for automatic extraction by the QuickCatalog tool.

However, reading and writing EXIF data in .NET is half-baked at
best. In addition, while I switched to TIFF for scanned output
images, that was a reasonably recent switch and so the vast
majority of images are in JPEG format, and I really didn't want
to be re-saving that image data in a lossy image format.

After some digging around for a .NET library and coming up
wanting, I came across [ExifTool][exiftool], a CLI for working
with meta information in an impressive number of file formats,
and created this tool to build the list of repair actions and
then perform them.

## Running the source

The code enumerates all `.JPG` and `.TIF` files in the current
directory and its sub folders. For each file, it will read in
the existing tags, then perform a number of actions

* Set missing `CreateDate`, `DateTimeOriginal` and `ModifyDate`
  values with the `LastWriteUtcTime` of the file
* Set missing `CreateDate`, `DateTimeOriginal` and `ModifyDate`
  values that don't conform to the `yyyy:MM:dd HH:mm:ss` format,
  again with the `LastWriteUtcTime` of the file
* Set the `Make` to be `Canon` if missing
* Set the `Model` to be `CanoScan LiDE 100` if missing
* Set the `Artist` to be `Richard James Moss` if missing
* Set the `Copyright` to be `Copyright (c) {0} Richard James
  Moss. All Rights Reserved.`, swapping in year based on the
  `LastWriteUtcTime`
* Set the `Software` to be `Cyotek QuickScan v1.0.0.0` if missing

Once it has build a list of changes, it will print a preview to
the console (which admittedly is useless when scanning thousands
of files) and confirm if you want to save.

If saving is confirmed, for each file

* Applies all the tag changes **without backing up the file** (I
  backed up the entire set before starting this exercise and
  didn't want ExitTool to create another copy of 120 GiB of
  files)
* Re-applies the original last modified date

Now you can see why running this on your own images as-is would
be a _very_ bad idea.

However, you can change these "rules" by modifying the code in
`Program.cs` - or add your own given I haven't really put in a
lot of bells and whistles given it is specifically to fit my use
case.

## ExifTool and -stay_open for performance

The original version took over an hour to 120 GiB of assorted
JPGand TIF images. This was actually fairly close to the time it
too to scan the EXIF data in .NET using `ImageFile.Open` which
was a surprise.

The docs mention that ExifTool has a significant overhead in
loading. When I was testing it adhoc that didn't seem any worse
than any other CLI tool but as ExifTool has a batch mode (using
`-stay_open 1` and `-@ <file>` and writing commands to `<file>`)
I gave that a try and the results were insane - that hour plus
scan I mentioned would run in around 4 minutes. I also
experimented with the `-fast2` option as I have no need to look
at "MakerNote" information, which knocked a few more seconds off
the total run time - not as much as most of the images I was
processing were scans, not photo's, and so I assume there was
little of that tag in use.

However, clearly there _is_ a huge start up overhead and so when
using in batch scenarios where you're dynamically building
commands, the `-say_open` is a clear winner, and something to
keep in mind for my own CLI tools.

## Closing thoughts

Not entirely sure why I've chosen to publish this source - as a
rule any internal tools like this never seen the light of day.
But maybe it is a useful starting point for someone else.

[exiftool]: https://exiftool.org/
[quickscan]: https://github.com/cyotek/Cyotek.QuickScan
