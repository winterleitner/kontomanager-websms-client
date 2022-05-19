using System;

namespace KontomanagerClient.Exceptions
{
    public class NumberNotAvailableException : Exception
    {
        public NumberNotAvailableException(string message) : base(message)
        {
            
        }
    }
}