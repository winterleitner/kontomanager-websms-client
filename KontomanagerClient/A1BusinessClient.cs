using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace KontomanagerClient
{
    /// <summary>
    /// Client for the Mein A1 interface for business accounts.
    /// </summary>
    public class A1BusinessClient : ICarrierAccount, IDisposable
    {
        public event EventHandler ConnectionEstablished;

        private readonly string _username;
        private readonly string _password;

        private CookieContainer _cookieContainer = new CookieContainer();
        private readonly HttpClientHandler _httpClientHandler;
        private readonly HttpClient _httpClient;

        private DateTime _lastConnected = DateTime.MinValue;

        private PhoneNumber _selectedPhoneNumber = null;

        public A1BusinessClient(string username, string password)
        {
            _username = username;
            _password = password;
            _httpClientHandler = new HttpClientHandler
            {
                CookieContainer = _cookieContainer,
                UseCookies = true,
                UseDefaultCredentials = false,
                AllowAutoRedirect = true
            };
            _httpClient = new HttpClient(_httpClientHandler);
        }

        public async Task<bool> CreateConnection()
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("UserID", _username),
                new KeyValuePair<string, string>("Password", _password),
                new KeyValuePair<string, string>("service", "mein-a1-PROD"),
                new KeyValuePair<string, string>("level", "10"),
                new KeyValuePair<string, string>("wrongLoginType", "false"),
                new KeyValuePair<string, string>("userRequestURL", "https://www.a1.net/mein-a1"),
                new KeyValuePair<string, string>("SetMsisdn", "false"),
                new KeyValuePair<string, string>("u3", "u3"),
            });

            _ = await _httpClient.GetAsync("https://ppp.a1.net/start/index.sp?execution=e1s1");

            HttpResponseMessage response = await _httpClient.PostAsync(
                "https://asmp.a1.net/asmp/ProcessLoginServlet/lvpaaa4/lvpbbgw3?aaacookie=lvpaaa4&eacookie=lvpbbgw3",
                content);

            string responseHTML = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(responseHTML);
            _lastConnected = DateTime.Now;
            ConnectionEstablished?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public async Task<IEnumerable<PhoneNumber>> GetSelectablePhoneNumbers(bool skipCurrentlySelected = false)
        {
            var url = "https://ppp.a1.net/start/index.sp?execution=e1s1";
            var response = await _httpClient.GetAsync(url);

            string responseHTML = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(responseHTML);
            var contractItems = doc.DocumentNode.SelectNodes("//li[contains(@class, 'contract-product')]");
            if (contractItems is null) return new List<PhoneNumber>();
            var numbers = new List<PhoneNumber>();
            foreach (var contractItem in contractItems)
            {
                var number = contractItem.SelectSingleNode(".//strong[@class='product-title']");
                var contractName = contractItem.SelectSingleNode(".//span[@class='pi-modify-name']");
                var detailsButton = contractItem.SelectSingleNode(".//a[@role='button']");
                var phoneNumber = new PhoneNumber
                {
                    Name = contractName.InnerText,
                    Number = number.InnerText,
                    SubscriberId = detailsButton.GetAttributeValue("href", null)?.Split('=').Last()
                };
                numbers.Add(phoneNumber);
            }

            return numbers;
        }

        public Task<string> GetSelectedPhoneNumber()
        {
            return Task.FromResult(_selectedPhoneNumber?.Number);
        }

        public Task SelectPhoneNumber(PhoneNumber number)
        {
            _selectedPhoneNumber = number;
            return Task.CompletedTask;
        }

        public Task<AccountUsage> GetAccountUsage()
        {
            if (_selectedPhoneNumber is null)
                throw new Exception("No phone number selected");
            return GetAccountUsage(_selectedPhoneNumber);
        }

        public async Task<AccountUsage> GetAccountUsage(PhoneNumber number)
        {
            var url = $"https://ppp.a1.net/start/mobileTariff.sp?subscriptionId={number.SubscriberId}";
            var response = await _httpClient.GetAsync(url);

            string responseHTML = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(responseHTML);
            var usage = new AccountUsage
            {
                Number = number.Number
            };
            var pu = new PackageUsage() { PackageName = "Main" };
            usage.PackageUsages.Add(pu);
            var header = doc.DocumentNode.SelectSingleNode("//header[@id='detail-header']");
            if (header != null)
            {
                var r = new Regex(@"Tarif:\s*(.*)\s*<\/p>");
                var contractName = r.Match(header.InnerHtml).Groups[1].Value.Trim();
                pu.PackageName = contractName;
            }

            var priceContainer = doc.DocumentNode.SelectSingleNode("//div[@class='price']");
            if (priceContainer != null)
            {
                var before = priceContainer.SelectSingleNode(".//span[@class='before-decimal']");
                var after = priceContainer.SelectSingleNode(".//span[@class='after-decimal']");
                if (before != null && after != null)
                {
                    usage.Cost = int.Parse(before.InnerText.Trim().Trim(',')) +
                                 int.Parse(after.InnerText.Trim().Trim(',')) / 100;
                }
            }

            var conversationsContainer = doc.DocumentNode.SelectSingleNode("//div[@id='conversations']");
            var dataContainer = doc.DocumentNode.SelectSingleNode("//div[@id='data']");
            var messagesContainer = doc.DocumentNode.SelectSingleNode("//div[@id='messages']");
            var containers = new[] { conversationsContainer, dataContainer, messagesContainer };
            foreach (var container in containers)
            {
                var freeUnits = container.SelectNodes(".//div[@class='free-units']");
                foreach (var freeUnit in freeUnits)
                {
                    var packages = freeUnit.SelectNodes(".//div[@class='circular-progress-wrap']");
                    if (packages != null)
                    {
                        foreach (var package in packages)
                        {
                            var uq = new UnitQuota();
                            var descriptionNode = package.SelectSingleNode(".//div[@class='circular-progress-label']");
                            if (descriptionNode != null)
                            {
                                uq.Name = descriptionNode.InnerText;
                            }

                            var circle = package.SelectSingleNode(".//div[contains(@class, 'circle100')]");
                            var usageSpan = circle.SelectSingleNode(".//span");
                            uq.Used = (int)Math.Round(decimal.Parse(usageSpan.FirstChild.InnerText.Trim()));
                            var regex = new Regex(@"\/(\d+)(\\n)?\s+([a-zA-Z]+)");
                            if (regex.IsMatch(usageSpan.InnerHtml))
                            {
                                var match = regex.Match(usageSpan.InnerHtml);
                                uq.Total = int.Parse(match.Groups[1].Value);
                                //var unit = match.Groups[3].Value;
                            }
                            else if (usageSpan.InnerText.Contains("unlimitiert"))
                            {
                                uq.Total = int.MaxValue;
                            }

                            if (uq.Name.Contains("Freimin") && uq.Name.Contains("EU") && !uq.Name.Contains("Ö"))
                            {
                                pu.AustriaToEuMinutes = uq;
                            }
                            else if (uq.Name.Contains("Freiminuten"))
                            {
                                pu.Minutes = uq;
                            }
                            else if (uq.Name.Contains("Roaming"))
                            {
                                pu.AdditionalQuotas.Add(uq.Name, uq);
                            }
                            else if (uq.Name.Contains("Daten") && uq.Name.Contains("Roaming"))
                            {
                                pu.AdditionalQuotas.Add(uq.Name, uq);
                            }
                            else if (uq.Name.Contains("Daten"))
                            {
                                pu.Data = uq;
                                pu.DataEu = uq;
                            }
                            else if (uq.Name.Contains("SMS") & uq.Name.Contains("Roaming"))
                            {
                                pu.AdditionalQuotas.Add(uq.Name, uq);
                            }
                            else if (uq.Name.Contains("SMS Ö"))
                            {
                                pu.Sms = uq;
                            }
                            else
                            {
                                pu.AdditionalQuotas.Add(uq.Name, uq);
                            }
                        }
                    }
                }
            }

            return usage;
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            _httpClientHandler.Dispose();
        }
    }
}