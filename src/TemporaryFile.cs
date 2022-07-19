namespace Cyotek.FixExif
{
  internal sealed class TemporaryFile : IDisposable
  {
    #region Private Fields

    private readonly string _fileName;

    #endregion Private Fields

    #region Public Constructors

    public TemporaryFile()
    {
      _fileName = Path.GetTempFileName();
    }

    #endregion Public Constructors

    #region Public Properties

    public string FileName => _fileName;

    #endregion Public Properties

    #region Public Methods

    public static explicit operator string(TemporaryFile value) => value.FileName;

    public bool Delete()
    {
      bool result;

      if (!string.IsNullOrEmpty(_fileName) && File.Exists(_fileName))
      {
        try
        {
          File.Delete(_fileName);
          result = true;
        }
        catch
        {
          result = false;
        }
      }
      else
      {
        result = true;
      }

      return result;
    }

    public void Dispose()
    {
      this.Delete();
    }

    #endregion Public Methods
  }
}