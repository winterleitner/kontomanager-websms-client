using System;

namespace KontomanagerClient
{
    public class YesssClient : KontomanagerClient
    {
        public YesssClient(string username, string password) :
            base(username, password, new Uri("https://www.yesss.at/kontomanager.at/"),
                new Uri("https://www.yesss.at/kontomanager.at/index.php"),
                new Uri("https://www.yesss.at/kontomanager.at/websms_send.php"))
        {
            
        }
    }
}