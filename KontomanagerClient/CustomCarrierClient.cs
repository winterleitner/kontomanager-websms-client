using System;

namespace KontomanagerClient
{
    /// <summary>
    /// This type can be used to utilize a non-implemented carrier installation of Kontomanager.
    /// Alternatively, simply extending KontomanagerClient should be considered.
    /// </summary>
    public class CustomCarrierClient : KontomanagerClient
    {
        public CustomCarrierClient(string username, string password, string baseUri)
        : base(username, password, new Uri(baseUri))
        {
            
        }
    }
}