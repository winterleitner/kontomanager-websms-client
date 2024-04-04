using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading.Tasks;

namespace KontomanagerClient
{
    public interface ICarrierAccount
    {
        /// <summary>
        /// Fires when the connection to the carrier account has been established = when login was successful.
        /// </summary>
        event EventHandler ConnectionEstablished;
        /// <summary>
        /// Creates a connection to the carrier account by logging in.
        /// </summary>
        /// <returns><b>true</b> on login success, else <b>false</b></returns>
        /// <exception cref="HttpRequestException">When the login request does not return a success status code.</exception>
        Task<bool> CreateConnection();
        
        /// <summary>
        /// Returns a list of phone numbers that are managed by the logged in account.
        /// This usually triggers a web request.
        /// </summary>
        /// <param name="skipCurrentlySelected"></param>
        /// <returns></returns>
        Task<IEnumerable<PhoneNumber>> GetSelectablePhoneNumbers(bool skipCurrentlySelected = false);
        
        /// <summary>
        /// Returns the phone number that is currently used when calling <see cref="GetAccountUsage()"/> without parameter.
        /// </summary>
        /// <returns>The phone number as string.</returns>
        Task<string> GetSelectedPhoneNumber();
        
        /// <summary>
        /// Selects a phone number that is managed by the logged in account to be used as default when calling <see cref="GetAccountUsage()"/> without parameter.
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        Task SelectPhoneNumber(PhoneNumber number);
        
        /// <summary>
        /// Loads the account usage for the currently selected phone number.
        /// </summary>
        /// <returns>The account usage for the currently selected account.</returns>
        /// <exception cref="InvalidOperationException">When no phone number was previously selected</exception>
        /// <exception cref="HttpRequestException">When the account is not logged in or the request does not return a success status code for any other reason</exception>
        Task<AccountUsage> GetAccountUsage();
        
        /// <summary>
        /// Selects the account if necessary, and then returns the account usage.
        /// </summary>
        /// <param name="number">The phone number to retrieve the usage for.</param>
        /// <returns>The account usage for the passed phone number.</returns>
        /// <exception cref="HttpRequestException">When the account is not logged in or the request does not return a success status code for any other reason</exception>
        Task<AccountUsage> GetAccountUsage(PhoneNumber number);
    }
}