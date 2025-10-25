namespace KY_MES.Application.Exceptions;

public class CompleteWipException : Exception
{
  public CompleteWipException(string message) : base(message) {}
}
