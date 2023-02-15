using System;

namespace KontomanagerClient
{
    public class XOXOClient : KontomanagerClient
    {
        public XOXOClient(string user, string password) : 
            base(user, password, new Uri("https://xoxo.kontomanager.at/app/"))
        {
        }

    }
    
}