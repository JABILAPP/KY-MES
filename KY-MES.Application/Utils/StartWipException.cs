namespace KY_MES.Application;

public class StartWipException : Exception
{
  public StartWipException(string message) : base(message) {}
}
