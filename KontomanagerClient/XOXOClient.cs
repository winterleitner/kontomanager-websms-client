using System;

namespace KontomanagerClient
{
    public class XOXOClient : KontomanagerClient
    {
        public XOXOClient(string username, string password)
            : base(username, password, new Uri("https://xoxo.kontomanager.at"),
                new Uri("https://xoxo.kontomanager.at/index.php"),
                new Uri("https://xoxo.kontomanager.at/websms_send.php"))
        {
            
        }

    }
}