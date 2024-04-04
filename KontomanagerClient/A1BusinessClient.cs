using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
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

        private readonly CookieContainer _cookieContainer = new CookieContainer();
        private readonly HttpClientHandler _httpClientHandler;
        private readonly HttpClient _httpClient;

        public DateTime? LastConnected { get; protected set; } = null;
        
        /// <summary>
        /// Determines if an exception shall be thrown when a login attempt with invalid credentials is made.
        /// </summary>
        public bool ThrowOnInvalidCredentials { get; set; } = false;

        /// <summary>
        /// Determines how long the client will assume that a session is still valid before checking it via a new login attempt.
        /// </summary>
        public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromMinutes(2);

        private PhoneNumber _selectedPhoneNumber;
        
        /// <summary>
        /// The customer number assigned to the current login.
        /// Available after first query of selectable numbers.
        /// </summary>
        public string CustomerNumber { get; protected set; }
        
        /// <summary>
        /// The contract number assigned to the current login.
        /// Available after first query of selectable numbers.
        /// </summary>
        public string ContractNumber { get; protected set; }

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

        /// <summary>
        /// Configures the client to throw an <see cref="InvalidCredentialException"/> when a login attempt with invalid credentials is made.
        /// </summary>
        /// <param name="enabled">True if the exception should be thrown.</param>
        /// <returns></returns>
        public A1BusinessClient ConfigureThrowOnInvalidCredentials(bool enabled = true)
        {
            ThrowOnInvalidCredentials = enabled;
            return this;
        }

        /// <summary>
        /// Configures how long the client will assume that a session is still valid before checking it via a new login attempt.
        /// </summary>
        /// <param name="lifetime"></param>
        /// <returns></returns>
        public A1BusinessClient ConfigureSessionLifetime(TimeSpan lifetime)
        {
            SessionLifetime = lifetime;
            return this;
        }

        private async Task<bool> IsLoggedIn()
        {
            try
            {
                var initResponse = await _httpClient.GetAsync("https://ppp.a1.net/start/index.sp?execution=e1s1");
                initResponse.EnsureSuccessStatusCode();

                var initResponseText = await initResponse.Content.ReadAsStringAsync();
                var doc = new HtmlDocument();
                doc.LoadHtml(initResponseText);
                var logoutElement = doc.DocumentNode.SelectSingleNode("//a[@title='Logout']");
                if (logoutElement != null)
                {
                    LastConnected = DateTime.Now;
                    ConnectionEstablished?.Invoke(this, EventArgs.Empty);
                    return true;
                }

                return false;
            }
            catch (HttpRequestException)
            {
                return false;
            }
        }

        private string ParseCustomerNumber(string page)
        {
            var r = new Regex(@"Kundennummer: (\d*)");
            var match = r.Match(page);
            var result = match.Groups.Count > 1 ? match.Groups[1].Value : null;
            if (result != null)
                CustomerNumber = result;
            return result;
        }

        private string ParseContractNumber(string page)
        {
            var r = new Regex(@"accountId=([^""]*)");
            var match = r.Match(page);
            var result = match.Groups.Count > 1 ? match.Groups[1].Value : null;
            if (result != null)
                ContractNumber = result;
            return result;
        }

        private async Task ReconnectIfRequired()
        {
            if (LastConnected is null || DateTime.Now - LastConnected > SessionLifetime)
            {
                await CreateConnection();
            }
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
                new KeyValuePair<string, string>("u3", "u3")
            });

            if (await IsLoggedIn())
                return true;
            
            var response = await _httpClient.PostAsync(
                "https://asmp.a1.net/asmp/ProcessLoginServlet/lvpaaa4/lvpbbgw3?aaacookie=lvpaaa4&eacookie=lvpbbgw3",
                content);

            var responseText = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(responseText);
            var logoutElement = doc.DocumentNode.SelectSingleNode("//a[@title='Logout']");
            if (logoutElement is null)
            {
                if (ThrowOnInvalidCredentials)
                {
                    var errorElement = doc.DocumentNode.SelectSingleNode("//div[@id='lbun-login-error-text-1']");
                    if (errorElement != null && errorElement.InnerText.ToLower().Contains("passwor"))
                    {
                        throw new InvalidCredentialException(errorElement.InnerText);
                    }
                }

                return false;
            }

            LastConnected = DateTime.Now;
            ConnectionEstablished?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public async Task<IEnumerable<PhoneNumber>> GetSelectablePhoneNumbers(bool skipCurrentlySelected = false)
        {
            await ReconnectIfRequired();
            const string url = "https://ppp.a1.net/start/index.sp?execution=e1s1";
            var response = await _httpClient.GetAsync(url);

            string responseText = await response.Content.ReadAsStringAsync();
            
            ParseCustomerNumber(responseText);
            ParseContractNumber(responseText);
            var doc = new HtmlDocument();
            doc.LoadHtml(responseText);
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
                throw new InvalidOperationException("No phone number selected");
            return GetAccountUsage(_selectedPhoneNumber);
        }

        public async Task<AccountUsage> GetAccountUsage(PhoneNumber number)
        {
            await ReconnectIfRequired();
            var url = $"https://ppp.a1.net/start/mobileTariff.sp?subscriptionId={number.SubscriberId}";
            var response = await _httpClient.GetAsync(url);

            var responseText = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(responseText);
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
                                 decimal.Parse(after.InnerText.Trim().Trim(',')) / 100;
                }
            }

            // Parse validity period of package
            DateTime? unitsStart = null, unitsEnd = null;
            var dateContainer = doc.DocumentNode.SelectSingleNode("//div[@class='price-date']");
            if (dateContainer != null)
            {
                (unitsStart, unitsEnd) = ParseUnitsValidityPeriod(dateContainer.InnerHtml);
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

                            if (unitsStart.HasValue)
                                pu.UnitsValidFrom = unitsStart.Value;
                            if (unitsEnd.HasValue)
                                pu.UnitsValidUntil = unitsEnd.Value;

                            var circle = package.SelectSingleNode(".//div[contains(@class, 'circle100')]");
                            var usageSpan = circle.SelectSingleNode(".//span");
                            if (uq.Name.Contains("Daten"))
                                uq.Used = (int)Math.Round(decimal.Parse(usageSpan.FirstChild.InnerText.Trim()) * 1024); //convert to MB
                            else 
                                uq.Used = (int)Math.Round(decimal.Parse(usageSpan.FirstChild.InnerText.Trim()));
                            var regex = new Regex(@"\/(\d+)(\\n)?\s+([a-zA-Z]+)");
                            if (regex.IsMatch(usageSpan.InnerHtml))
                            {
                                var match = regex.Match(usageSpan.InnerHtml);
                                if (uq.Name.Contains("Daten"))
                                    uq.Total = int.Parse(match.Groups[1].Value) * 1024;
                                else
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

        /// <summary>
        /// From the account usage page, parses the current start and end dates.
        /// </summary>
        /// <param name="containerHtml"></param>
        /// <returns></returns>
        private (DateTime? unitsStart, DateTime? unitsEnd) ParseUnitsValidityPeriod(string containerHtml)
        {
            var range = containerHtml.Split(new [] { "<br>" }, StringSplitOptions.None).Last().Trim();
            var parts = range.Split(new [] { "bis" }, StringSplitOptions.None);
            var start = parts[0].Trim();
            var end = parts[1].Trim();
            var startDay = int.Parse(start.Split('.').First());
            var endDay = int.Parse(end.Split('.').First());
            var today = DateTime.Today;
            DateTime? unitsStart = today.Day >= startDay ? new DateTime(today.Year, today.Month, startDay) : new DateTime(today.AddMonths(-1).Year, today.AddMonths(-1).Month, startDay);
            DateTime? unitsEnd = today.Day <= endDay ? new DateTime(today.Year, today.Month, endDay) : new DateTime(today.AddMonths(1).Year, today.AddMonths(1).Month, endDay);
            return (unitsStart, unitsEnd);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            _httpClientHandler.Dispose();
        }
    }
}