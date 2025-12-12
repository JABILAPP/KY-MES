namespace KY_MES.Services;

public class StartWipException : Exception
{
  public StartWipException(string message) : base(message) {}
}
