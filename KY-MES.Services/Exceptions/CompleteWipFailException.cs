namespace KY_MES.Services;

public class CompleteWipFailException : Exception
{
  public CompleteWipFailException(string message) : base(message) {}
}
