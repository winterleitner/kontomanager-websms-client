using System;
using System.Collections.Generic;

namespace KontomanagerClient
{
    public class AccountUsage
    {
        public string Number { get; set; }
        
        /// <summary>
        /// Some carriers offer loyalty points, such as A1 Mobilpoints
        /// </summary>
        public int LoyaltyPoints { get; internal set; }
        public decimal Cost { get; set; }

        public DateTime InvoiceDate { get; set; }
        
        public bool Prepaid { get; set; }
        
        /// <summary>
        /// If prepaid = true, indicates when the card will expire (=1 year after last recharge)
        /// </summary>
        public DateTime SimCardValidUntil { get; set; }

        public decimal Credit { get; set; }

        /// <summary>
        /// SIM cards not recharged for one year or longer will be deactivated.
        /// </summary>
        public DateTime LastRecharge { get; set; }

        public List<PackageUsage> PackageUsages { get; set; } = new List<PackageUsage>();

        public void PrintToConsole()
        {
            Console.WriteLine();
            Console.WriteLine($"Usage for {Number}");
            foreach (var pu in PackageUsages)
            {
                Console.WriteLine(pu.PackageName);
                Console.WriteLine("-------------");
                Console.WriteLine($"Min: {pu.Minutes.Used} / {pu.Minutes.Total}");
                Console.WriteLine($"SMS: {pu.Sms.Used} / {pu.Sms.Total}");
                Console.WriteLine($"Min/SMS Shared: {pu.MinutesAndSmsQuotasShared}");
                Console.WriteLine($"EUMin: {pu.AustriaToEuMinutes.Used} / {pu.AustriaToEuMinutes.Total}");
                Console.WriteLine($"Data: {pu.Data.Used} / {pu.Data.Total} ({pu.Data.RemainingFree} remaining)");
                Console.WriteLine($"EUData: {pu.DataEu.Used} / {pu.DataEu.Total} ({pu.DataEu.RemainingFree} remaining)");
                foreach (var aq in pu.AdditionalQuotas)
                {
                    Console.WriteLine($"{aq.Key}: {aq.Value.Used} / {aq.Value.Total} ({aq.Value.RemainingFree} remaining)");
                }
                Console.WriteLine("-------------");
                Console.WriteLine();
            }
        }
    }
}