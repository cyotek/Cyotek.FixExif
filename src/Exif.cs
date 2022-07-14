using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Cyotek.FixExif
{
  internal class Exif
  {
    #region Internal Fields

    internal const string ExifDateFormat = "yyyy:MM:dd HH:mm:ss";

    #endregion Internal Fields

    #region Private Fields

    private static readonly string _exifToolFileName;

    private static char[] _separators = { '\n' };

    private readonly Dictionary<string, List<Tuple<CommandType, string>>> _commands;

    private string? _fileName;

    private bool _hasFileChanged;

    private DateTime _lastWriteTimeUtc;

    private string? _tagName;

    private Dictionary<string, string> _tags;

    private string? _tagValue;

    #endregion Private Fields

    #region Public Constructors

    static Exif()
    {
      _exifToolFileName = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "exiftool.exe");
    }

    public Exif()
    {
      _commands = new Dictionary<string, List<Tuple<CommandType, string>>>(StringComparer.OrdinalIgnoreCase);
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

    public Exif GetTagValue(string tagName)
    {
      _tagName = tagName;
      _tagValue = null;

      if (_tags.Count == 0)
      {
        string output;

        Console.WriteLine("Reading tags");

        output = this.Run(string.Format("-S \"{0}\"", _fileName));

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
      }

      _tags.TryGetValue(tagName, out _tagValue);

      Console.WriteLine("Current value of tag {0} is {1}", _tagName, _tagValue ?? "<Not set>");

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

        Console.WriteLine("Replacing invalid date {0} with {1}", oldValue, _tagValue);
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

        Console.WriteLine("Applying missing value {0}", _tagValue);
      }

      return this;
    }

    public Exif PreserveDateFileModified()
    {
      if (_hasFileChanged)
      {
        this.AddCommand(_fileName!, Tuple.Create(CommandType.SetLastWriteTimeUtc, _lastWriteTimeUtc.Ticks.ToString()));
      }

      return this;
    }

    public Exif Preview()
    {
      foreach (KeyValuePair<string, List<Tuple<CommandType, string>>> pair in _commands)
      {
        Console.Write(pair.Key);
        Console.WriteLine(':');

        this.MergeCommands(pair.Value);

        foreach (Tuple<CommandType, string> command in pair.Value)
        {
          Console.WriteLine("\t{0}: {1}", command.Item1, command.Item2);
        }
      }

      return this;
    }

    public Exif SaveChanges()
    {
      if (_commands.Count > 0)
      {
        Console.WriteLine("Saving changes");

        foreach (KeyValuePair<string, List<Tuple<CommandType, string>>> pair in _commands)
        {
          Console.Write(pair.Key);

          this.MergeCommands(pair.Value);

          foreach (Tuple<CommandType, string> command in pair.Value)
          {
            switch (command.Item1)
            {
              case CommandType.ExifTool:
                Console.WriteLine(this.Run(string.Format("{0} \"{1}\"", command.Item2, pair.Key)));
                break;
              case CommandType.SetLastWriteTimeUtc:
                File.SetLastWriteTimeUtc(pair.Key, DateTime.FromBinary(long.Parse(command.Item2)));
                break;
              default:
                throw new ArgumentOutOfRangeException();
            }
          }
        }

        _hasFileChanged = false;
        _commands.Clear();
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

      Console.WriteLine("Switched to file {0}", fileName);

      return this;
    }

    #endregion Public Methods

    #region Private Methods

    private void AddCommand(string fileName, Tuple<CommandType, string> command)
    {
      if (!_commands.TryGetValue(fileName, out List<Tuple<CommandType, string>> commands))
      {
        commands = new List<Tuple<CommandType, string>>
        {
          command
        };

        _commands.Add(fileName, commands);
      }
      else
      {
        commands.Add(command);
      }
    }

    private void AddSetCommand(string fileName, string tagName, string tagValue)
    {
      _hasFileChanged = true;

      this.AddCommand(fileName, Tuple.Create(CommandType.ExifTool, string.Format("-{0}=\"{1}\"", tagName, tagValue)));
    }

    private void MergeCommands(List<Tuple<CommandType, string>> commands)
    {
      string? timestamp;
      StringBuilder arguments;

      timestamp = null;
      arguments = new StringBuilder();

      foreach (Tuple<CommandType, string> command in commands)
      {
        switch (command.Item1)
        {
          case CommandType.ExifTool:
            if (arguments.Length > 0)
            {
              arguments.Append(' ');
            }

            arguments.Append(command.Item2);
            break;

          case CommandType.SetLastWriteTimeUtc:
            timestamp = command.Item2;
            break;

          default:
            throw new ArgumentOutOfRangeException();
        }
      }

      commands.Clear();
      commands.Add(Tuple.Create(CommandType.ExifTool, arguments.ToString()));

      if (!string.IsNullOrWhiteSpace(timestamp))
      {
        commands.Add(Tuple.Create(CommandType.SetLastWriteTimeUtc, timestamp));
      }
    }

    private string Run(string arguments)
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

    #endregion Private Methods
  }
}