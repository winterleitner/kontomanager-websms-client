using System;
using System.Collections.Generic;

namespace KontomanagerClient
{
    public class PackageUsage
    {
        public string PackageName { get; set; }
        
        public DateTime UnitsValidFrom { get; set; }
        public DateTime UnitsValidUntil { get; set; }

        public UnitQuota Minutes { get; set; } = new UnitQuota();
        public UnitQuota Sms { get; set; } = new UnitQuota();
        public bool MinutesAndSmsQuotasShared { get; set; }

        public UnitQuota AustriaToEuMinutes { get; set; } = new UnitQuota();

        /// <summary>
        /// Unit is MB. Domestic Usage only.
        /// </summary>
        public UnitQuota Data { get; set; } = new UnitQuota();

        /// <summary>
        /// Unit is MB. Non-Domestic usage within other EU countries.
        /// </summary>
        public UnitQuota DataEu { get; set; } = new UnitQuota();
        
        /// <summary>
        /// Additional units included in a specific package, for example CH or US minutes or data.
        /// The dictionary key is the name or description of the additional unit.
        /// </summary>
        public Dictionary<string, UnitQuota> AdditionalQuotas { get; set; } = new Dictionary<string, UnitQuota>();

        public Dictionary<string, string> AdditionalInformation { get; set; } = new Dictionary<string, string>();
    }

    public class UnitQuota
    {
        private int _externallyUsed;

        protected internal void CorrectRemainingFree(int actualRemaining)
        {
            var difference = Total - Used;
            _externallyUsed = difference - actualRemaining;
        }
        
        /// <summary>
        /// Optional Property that can be used to store a description of the unit quota.
        /// </summary>
        public string Name { get; set; }
        public int Used { get; set; }
        public int Total { get; set; }
        
        /// <summary>
        /// If the total value is int.MaxValue, the quota is unlimited.
        /// </summary>
        public bool Unlimited => Total == int.MaxValue;
        
        public int RemainingFree {
            get
            {
                if ((Used + _externallyUsed) >= Total) return 0;
                return Total - Used - _externallyUsed;
            }
            
        }
    }
}