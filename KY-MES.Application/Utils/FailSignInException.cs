namespace KY_MES.Application.Utils
{
    public class FailSignInException : Exception
    {
        public FailSignInException(string message) : base(message) { }
    }
}
