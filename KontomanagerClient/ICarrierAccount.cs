using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KontomanagerClient
{
    public interface ICarrierAccount
    {
        event EventHandler ConnectionEstablished;
        /// <summary>
        /// Creates a connection to the carrier account by logging in.
        /// </summary>
        /// <returns></returns>
        Task<bool> CreateConnection();
        Task<IEnumerable<PhoneNumber>> GetSelectablePhoneNumbers(bool skipCurrentlySelected = false);
        Task<string> GetSelectedPhoneNumber();
        Task SelectPhoneNumber(PhoneNumber number);
        Task<AccountUsage> GetAccountUsage();
        
        /// <summary>
        /// Selects the account if necessary, and then returns the account usage.
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        Task<AccountUsage> GetAccountUsage(PhoneNumber number);
    }
}