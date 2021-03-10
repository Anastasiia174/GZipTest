namespace GZipTest
{
    public class ArgumentError
    {
        public string Message { get; set; }

        public bool IsWarning { get; set; }

        public ArgumentError(string message, bool isWarning = false)
        {
            Message = message;
            IsWarning = isWarning;
        }
    }
}