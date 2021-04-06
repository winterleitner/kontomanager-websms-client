using System;

namespace KontomanagerClient
{
    public class EducomClient : KontomanagerClient
    {
        public EducomClient(string username, string password)
            : base(username, password, new Uri("https://educom.kontomanager.at"),
                new Uri("https://educom.kontomanager.at/index.php"),
                new Uri("https://educom.kontomanager.at/websms_send.php"))
        {
            
        }

    }
}