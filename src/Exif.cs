﻿using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Cyotek.FixExif
{
  internal class Exif : IDisposable
  {
    #region Internal Fields

    internal const string ExifDateFormat = "yyyy:MM:dd HH:mm:ss";

    #endregion Internal Fields

    #region Private Fields

    private static readonly string _exifToolFileName;

    private static readonly string[] _exitArguments = new[] { "-stay_open", "0" };

    private static char[] _separators = { '\n' };

    private readonly Dictionary<string, List<Tuple<CommandType, string[]>>> _commands;

    private string _argumentsFile;

    private Process _exiftoolProcess;

    private string? _fileName;

    private bool _hasFileChanged;

    private bool _isExiftoolRunning;

    private DateTime _lastWriteTimeUtc;

    private bool _overwriteWithoutBackup;

    private string? _tagName;

    private Dictionary<string, string> _tags;

    private string? _tagValue;

    private bool _verbose;

    #endregion Private Fields

    #region Public Constructors

    static Exif()
    {
      _exifToolFileName = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "exiftool.exe");
    }

    public Exif()
    {
      _commands = new Dictionary<string, List<Tuple<CommandType, string[]>>>(StringComparer.OrdinalIgnoreCase);
      _tags = new Dictionary<string, string>();
    }

    #endregion Public Constructors

    #region Public Properties

    public DateTime DateFileModified => File.GetLastWriteTimeUtc(_fileName);

    public string? FileName => _fileName;

    public string? TagName => _tagName;

    public string? TagValue => _tagValue;

    #endregion Public Properties

    #region Public Methods

    public Exif CloseExifTool()
    {
      if (_isExiftoolRunning)
      {
        this.Run(_exitArguments);

        _isExiftoolRunning = false;
      }

      if (!string.IsNullOrWhiteSpace(_argumentsFile) && File.Exists(_argumentsFile))
      {
        File.Delete(_argumentsFile);
      }

      return this;
    }

    public Exif Confirm(string prompt, Action<Exif> action)
    {
      ConsoleKeyInfo info;

      Console.WriteLine(prompt);

      do
      {
        info = Console.ReadKey(true);
      } while (info.Key != ConsoleKey.Y && info.Key != ConsoleKey.N);

      if (info.Key == ConsoleKey.Y)
      {
        action(this);
      }

      return this;
    }

    public void Dispose()
    {
      this.CloseExifTool();
    }

    public Exif GetTagValue(string tagName)
    {
      _tagName = tagName;
      _tagValue = null;

      if (_tags.Count == 0)
      {
        this.ReadTags();
      }

      _tags.TryGetValue(tagName, out _tagValue);

      this.WriteOutput(string.Format("Current value of tag {0} is {1}", _tagName, _tagValue ?? "<Not set>"));

      return this;
    }

    public Exif IfInvalidDateReplaceWith(string value) => this.IfInvalidDateReplaceWith(_ => value);

    public Exif IfInvalidDateReplaceWith(Func<Exif, string> getValue)
    {
      if (!DateTime.TryParseExact(_tagValue, ExifDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime _))
      {
        string? oldValue;

        oldValue = _tagValue;
        _tagValue = getValue(this);

        this.AddSetCommand(_fileName!, _tagName!, _tagValue);

        this.WriteOutput(string.Format("Replacing invalid date {0} with {1}", oldValue, _tagValue));
      }

      return this;
    }

    public Exif IfMissingReplaceWith(string value) => this.IfMissingReplaceWith(_ => value);

    public Exif IfMissingReplaceWith(Func<Exif, string> getValue)
    {
      if (string.IsNullOrWhiteSpace(_tagValue))
      {
        _tagValue = getValue(this);

        this.AddSetCommand(_fileName!, _tagName!, _tagValue);

        this.WriteOutput(string.Format("Applying missing value {0}", _tagValue));
      }

      return this;
    }

    public Exif OverwriteWithoutBackup()
    {
      _overwriteWithoutBackup = true;

      return this;
    }

    public Exif PreserveDateFileModified()
    {
      if (_hasFileChanged)
      {
        this.AddCommand(_fileName!, Tuple.Create(CommandType.SetLastWriteTimeUtc, new[] { _lastWriteTimeUtc.Ticks.ToString() }));
      }

      return this;
    }

    public Exif Preview()
    {
      foreach (KeyValuePair<string, List<Tuple<CommandType, string[]>>> pair in _commands)
      {
        Console.Write(pair.Key);
        Console.WriteLine(':');

        this.MergeCommands(pair.Key, pair.Value);

        foreach (Tuple<CommandType, string[]> command in pair.Value)
        {
          Console.WriteLine(command.Item1);

          foreach (string line in command.Item2)
          {
            Console.WriteLine("\t{0}", line);
          }
        }
      }

      return this;
    }

    public Exif ReadTags()
    {
      string output;

      this.WriteOutput("Reading tags");

      output = this.Run(new[] { "-S", "-fast2", _fileName! });

      if (!string.IsNullOrWhiteSpace(output))
      {
        string[] lines;

        lines = output.Split(_separators, StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < lines.Length; i++)
        {
          string line;
          int index;

          line = lines[i];
          index = line.IndexOf(':');

          if (index != -1)
          {
            _tags.Add(line.Substring(0, index).Trim(), line.Substring(index + 1).Trim());
          }
        }
      }

      return this;
    }

    public Exif ReplaceWith(string value) => this.ReplaceWith(_ => value);

    public Exif ReplaceWith(Func<Exif, string> getValue)
    {
      string newValue;

      newValue = getValue(this);

      if (!string.Equals(_tagValue, newValue))
      {
        _tagValue = newValue;

        this.AddSetCommand(_fileName!, _tagName!, _tagValue);

        this.WriteOutput(string.Format("Applying tag value {0}", _tagValue));
      }

      return this;
    }

    public Exif SaveChanges()
    {
      if (_commands.Count > 0)
      {
        Dictionary<string, DateTime> fileUpdates;

        this.WriteOutput("Saving changes");

        this.CloseExifTool();

        fileUpdates = new Dictionary<string, DateTime>();

        using (TemporaryFile workFile = new TemporaryFile())
        {
          using (TextWriter writer = new StreamWriter(workFile.FileName))
          {
            foreach (KeyValuePair<string, List<Tuple<CommandType, string[]>>> pair in _commands)
            {
              Console.Write(pair.Key);

              this.MergeCommands(pair.Key, pair.Value);

              foreach (Tuple<CommandType, string[]> command in pair.Value)
              {
                switch (command.Item1)
                {
                  case CommandType.ExifTool:
                    for (int i = 0; i < command.Item2.Length; i++)
                    {
                      writer.WriteLine(command.Item2[i]);
                    }
                    break;

                  case CommandType.SetLastWriteTimeUtc:
                    fileUpdates.Add(pair.Key, DateTime.FromBinary(long.Parse(command.Item2[0])));
                    break;

                  default:
                    throw new ArgumentOutOfRangeException();
                }
              }
            }
          }

          Console.WriteLine(this.RunStandalone(string.Format("-@ \"{0}\"", workFile.FileName)));
        }

        foreach (KeyValuePair<string, DateTime> pair in fileUpdates)
        {
          File.SetLastWriteTimeUtc(pair.Key, pair.Value);
        }

        _hasFileChanged = false;
        _commands.Clear();
      }

      return this;
    }

    public Exif SetTagValue(string name, string value)
    {
      _tagName = name;
      _tagValue = value;

      this.AddSetCommand(_fileName!, _tagName, _tagValue);

      this.WriteOutput(string.Format("Applying value {1} to {0}", _tagName, _tagValue));

      return this;
    }

    public Exif StartExifTool()
    {
      // keep open derived from example on exiftool forums
      // https://exiftool.org/forum/index.php?topic=11286.0

      this.PrepareArgumentsFile();

      if (!_isExiftoolRunning)
      {
        _exiftoolProcess = new Process
        {
          StartInfo =
          {
            FileName = _exifToolFileName,
            Arguments = string.Format("-stay_open 1 -@ \"{0}\"", _argumentsFile),
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
          }
        };
        _exiftoolProcess.ErrorDataReceived += this.Process_ErrorDataReceived;

        _exiftoolProcess.Start();

        _isExiftoolRunning = true;
      }

      return this;
    }

    public Exif UseFileName(string fileName)
    {
      ArgumentNullException.ThrowIfNull(fileName);

      if (!File.Exists(fileName))
      {
        throw new FileNotFoundException("File not found.", fileName);
      }

      _hasFileChanged = false;
      _fileName = fileName;
      _lastWriteTimeUtc = File.GetLastWriteTimeUtc(fileName);
      _tags.Clear();

      this.WriteOutput(string.Format("Switched to file {0}", fileName));

      return this;
    }

    public Exif Verbose()
    {
      _verbose = true;

      return this;
    }

    public Exif Write(string message)
    {
      Console.WriteLine(message);

      return this;
    }

    #endregion Public Methods

    #region Private Methods

    private void AddCommand(string fileName, Tuple<CommandType, string[]> commands)
    {
      if (!_commands.TryGetValue(fileName, out List<Tuple<CommandType, string[]>>? existingCommands))
      {
        existingCommands = new List<Tuple<CommandType, string[]>>
        {
          commands
        };

        _commands.Add(fileName, existingCommands);
      }
      else
      {
        existingCommands.Add(commands);
      }
    }

    private void AddSetCommand(string fileName, string tagName, string tagValue)
    {
      List<string> commands;

      _hasFileChanged = true;

      commands = new List<string>
      {
        fileName
      };

      if (_overwriteWithoutBackup)
      {
        commands.Add("-overwrite_original_in_place");
      }

      commands.Add(string.Format("-{0}={1}", tagName, tagValue));

      this.AddCommand(fileName, Tuple.Create(CommandType.ExifTool, commands.ToArray()));
    }

    private void MergeCommands(string fileName, List<Tuple<CommandType, string[]>> commands)
    {
      string? timestamp;
      List<string> arguments;

      timestamp = null;
      arguments = new List<string>();

      foreach (Tuple<CommandType, string[]> command in commands)
      {
        switch (command.Item1)
        {
          case CommandType.ExifTool:
            for (int i = 0; i < command.Item2.Length; i++)
            {
              string arg;

              arg = command.Item2[i];

              if (arg != fileName && arg != "-execute" && !arguments.Contains(arg))
              {
                arguments.Add(arg);
              }
            }
            break;

          case CommandType.SetLastWriteTimeUtc:
            timestamp = command.Item2[0];
            break;

          default:
            throw new ArgumentOutOfRangeException();
        }
      }

      arguments.Add(fileName);
      arguments.Add("-execute"); // very important, otherwise if you run as a batch the last tag assignments will be used for all files

      commands.Clear();
      commands.Add(Tuple.Create(CommandType.ExifTool, arguments.ToArray()));

      if (!string.IsNullOrWhiteSpace(timestamp))
      {
        commands.Add(Tuple.Create(CommandType.SetLastWriteTimeUtc, new[] { timestamp }));
      }
    }

    private void PrepareArgumentsFile()
    {
      if (string.IsNullOrWhiteSpace(_argumentsFile))
      {
        _argumentsFile = Path.GetTempFileName();
        _argumentsFile = Path.ChangeExtension(Environment.ProcessPath, ".txt");

        File.WriteAllText(_argumentsFile, string.Empty, Encoding.ASCII);
      }
    }

    private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
      if (!string.IsNullOrEmpty(e.Data))
      {
        Console.WriteLine(e.Data);
      }
    }

    private string Run(string[] arguments)
    {
      StringBuilder sb;
      string? line;

      this.StartExifTool();

      this.WriteAndExecuteArguments(arguments);

      sb = new StringBuilder();

      do
      {
        line = _exiftoolProcess.StandardOutput.ReadLine();

        sb.AppendLine(line);
      }
      while (!_exiftoolProcess.HasExited && (string.IsNullOrWhiteSpace(line) || !line.Contains("{ready}", StringComparison.OrdinalIgnoreCase)));

      return sb.ToString();
    }

    private string RunStandalone(string arguments)
    {
      Process process;

      process = new Process
      {
        StartInfo =
        {
          FileName = _exifToolFileName,
          Arguments = arguments,
          CreateNoWindow = true,
          RedirectStandardOutput = true,
          UseShellExecute = false
        }
      };

      process.Start();

      return process.StandardOutput.ReadToEnd();
    }

    private void WriteAndExecuteArguments(string[] arguments)
    {
      using (Stream stream = File.Open(_argumentsFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
      {
        using (TextWriter writer = new StreamWriter(stream, Encoding.ASCII))
        {
          for (int i = 0; i < arguments.Length; i++)
          {
            writer.WriteLine(arguments[i]);
          }

          writer.WriteLine("-execute");
        }
      }
    }

    private void WriteOutput(string message)
    {
      if (_verbose)
      {
        this.Write(message);
      }
    }

    #endregion Private Methods
  }
}