namespace KY_MES.Application;

public class CompleteWipException : Exception
{
  public CompleteWipException(string message) : base(message) {}
}
