using System;

namespace KontomanagerClient
{
    public class YesssClient : KontomanagerClient
    {
        public YesssClient(string user, string password) : 
            base(user, password, new Uri("https://www.yesss.at/kontomanager.at/app/"))
        {
        }
    }
}