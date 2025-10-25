namespace KY_MES.Application.Exceptions;

public class FailSignInException : Exception
{
    public FailSignInException(string message) : base(message) { }
}
