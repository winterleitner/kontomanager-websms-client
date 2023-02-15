using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Web;
using HtmlAgilityPack;
using KontomanagerClient.Exceptions;
using KontomanagerClient.Model;

namespace KontomanagerClient
{
    /// <summary>
    /// Sms Client for Sim Cards from providers using kontomanager.at
    /// </summary>
    public abstract class KontomanagerClient : IDisposable
    {
        #region Parameters

        private int _sessionTimeoutSeconds = 10 * 60;
        private bool _useQueue = false;
        private bool _enableDebugLogging = false;

        protected HashSet<string> _excludedSections = new HashSet<string>()
        {
            "Ukraine Freieinheiten", "Ihre Kostenkontrolle", "TUR SYR Einheiten", "Verknüpfte Rufnummern",
            "Aktuelle Kosten", "Oft benutzt"
        };

        #endregion

        #region Events

        /// <summary>
        /// Fires when the connection to Kontomanager was successfully made.
        /// </summary>
        public event EventHandler ConnectionEstablished;

        #endregion

        private CookieContainer _cookieContainer = new CookieContainer();
        private readonly HttpClientHandler _httpClientHandler;
        private readonly HttpClient _httpClient;

        protected readonly Uri BaseUri;
        public string LoginPath { get; set; } = "index.php";
        public string SettingsPath { get; set; } = "einstellungen_profil.php";
        public string AccountUsagePath { get; set; } = "kundendaten.php";

        private DateTime _lastConnected = DateTime.MinValue;


        private readonly string _user;
        private readonly string _password;

        private readonly Dictionary<string, string> _numberToSubscriberId = new Dictionary<string, string>();

        public bool Connected => DateTime.Now - _lastConnected < TimeSpan.FromSeconds(_sessionTimeoutSeconds);


        /// <summary>
        /// Initializes the client with credentials to the respective kontomanager web portal. (*.kontomanager.at)
        /// </summary>
        /// <param name="user"></param>
        /// <param name="password"></param>
        protected KontomanagerClient(string user, string password, Uri baseUri)
        {
            _user = user;
            _password = password;
            BaseUri = baseUri;

            _cookieContainer.Add(new Cookie("CookieSettings",
                "%7B%22categories%22%3A%5B%22necessary%22%2C%22improve_offers%22%5D%7D", "/", "yesss.at"));
            _httpClientHandler = new HttpClientHandler();
            _httpClientHandler.CookieContainer = _cookieContainer;
            _httpClient = new HttpClient(_httpClientHandler);
            _httpClient.BaseAddress = baseUri;
        }

        /// <summary>
        /// Configures the Session Timeout for the client. Default is 600.
        /// </summary>
        /// <param name="seconds"></param>
        /// <returns></returns>
        public KontomanagerClient SetSessionTimeoutSeconds(int seconds)
        {
            _sessionTimeoutSeconds = seconds;
            return this;
        }

        /// <summary>
        /// Returns the phone number for which the client is currently active.
        /// This is relevant when multiple phone numbers are grouped in one account.
        /// </summary>
        /// <returns></returns>
        public async Task<string> GetSelectedPhoneNumber()
        {
            if (!Connected)
                await Reconnect();
            HttpResponseMessage response = await _httpClient.GetAsync(SettingsPath);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("Could not determine the selected phone number");
            var responseHtml = await response.Content.ReadAsStringAsync();
            string number = ExtractSelectedPhoneNumberFromSettingsPage(responseHtml);

            if (number is null) throw new Exception("Phone number could not be found");
            return number;
        }

        private string ExtractSelectedPhoneNumberFromSettingsPage(string settingsPageHtml)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(settingsPageHtml);

            foreach (var row in doc.DocumentNode.SelectNodes("//li[@class='list-group-item']"))
            {
                if (row.HasClass("list-group-header")) continue;
                var d = row.SelectSingleNode("./div");
                var sides = d.SelectNodes("./div");
                if (sides.Count < 2 ||
                    !sides[0].InnerText.StartsWith("Rufnummer")) continue;
                return sides.Last().InnerText;
            }

            return null;
        }
        
        private PhoneNumber ExtractPhoneNumberFromDropdown(HtmlNode liNode)
        {
            if (liNode is null || liNode.InnerHtml.Contains("index.php?dologout=2") || (liNode.FirstChild != null && liNode.FirstChild.HasClass("dropdown-divider"))) return null;
            var name = liNode.SelectSingleNode(".//span").InnerText;
            
            var pattern = @"\d+\/\d*";
            var match = Regex.Match(liNode.InnerHtml, pattern);
            var number = $"43{match.Value.Replace("/", "").Substring(1)}";
            
            var subscriberId = liNode.SelectSingleNode("./a").Attributes["href"].Value == "#" ? 
                null :
                HttpUtility.UrlDecode(Regex.Match(liNode.SelectSingleNode("./a").Attributes["href"].Value, @"subscriber=([^&]*)").Groups[1].Value);
            if (string.IsNullOrEmpty(subscriberId))
            {
                if (_numberToSubscriberId.ContainsKey(number))
                    subscriberId = _numberToSubscriberId[number];
            }
            else
            {
                _numberToSubscriberId[number] = subscriberId;
            }
            var isSelected = GetPreviousActualSibling(liNode)?.InnerText.ToLower().Contains("aktuell gewählte rufnummer")??false;

            return new PhoneNumber()
            {
                Name = name, SubscriberId = subscriberId, Number = number,
                Selected = isSelected
            };
        }

        private HtmlNode GetPreviousActualSibling(HtmlNode n)
        {
            HtmlNode res = n;
            while (res != null && (res == n || res.NodeType != n.NodeType))
            {
                if (res.PreviousSibling == null) return null;
                res = res.PreviousSibling;
            }

            return res;
        }
        private HtmlNode GetNextActualSibling(HtmlNode n)
        {
            HtmlNode res = n;
            while (res != null && (res == n || res.NodeType != n.NodeType))
            {
                if (res.NextSibling == null) return null;
                res = res.NextSibling;
            }

            return res;
        }
        private IEnumerable<PhoneNumber> ExtractSelectablePhoneNumbersFromDropdown(string homePageHtml)
        {
            var res = new List<PhoneNumber>();
            var doc = new HtmlDocument();
            doc.LoadHtml(homePageHtml);

            var dd = doc.DocumentNode.SelectSingleNode("//ul[@aria-labelledby='user-dropdown']");
            if (dd is null) return res;

            // Selected Number
            var sn = dd.ChildNodes.FirstOrDefault(n => n.InnerText.ToLower().Contains("aktuell gewählte rufnummer"));
            if (sn != null && GetNextActualSibling(sn) != null)
            {
                res.Add(ExtractPhoneNumberFromDropdown(GetNextActualSibling(sn)));
            }
            //other numbers
            var an = dd.ChildNodes.FirstOrDefault(n => n.InnerText.ToLower().Contains("rufnummer wechseln"));
            if (an is null) return null;
            while (GetNextActualSibling(an) != null)
            {
                an = GetNextActualSibling(an);
                res.Add(ExtractPhoneNumberFromDropdown(an));
            }

            return res.Where(n => n != null);
        }

        /// <summary>
        /// Returns a list of all phone numbers linked to the account.
        /// If the account is a single-sim account without a group, an empty list is returned.
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<PhoneNumber>> GetSelectablePhoneNumbers()
        {
            if (!Connected)
                await Reconnect();
            HttpResponseMessage response = await _httpClient.GetAsync(SettingsPath);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("Could not determine the selectable phone numbers");
            var responseHtml = await response.Content.ReadAsStringAsync();
            return ExtractSelectablePhoneNumbersFromDropdown(responseHtml);
        }

        /// <summary>
        /// Sets the phone number to use for the client.
        /// </summary>
        public async Task SelectPhoneNumber(PhoneNumber number)
        {
            if (number == null || number.SubscriberId == null)
                throw new Exception("No subscriber id provided!");
            
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("groupaction", "change_subscriber"),
                new KeyValuePair<string, string>("subscriber", number.SubscriberId),
            });
            
            if (!Connected)
                await Reconnect();

            HttpResponseMessage response = await _httpClient.PostAsync(AccountUsagePath, content);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("Could not change the selected phone number");
            var responseHtml = await response.Content.ReadAsStringAsync();
            // If the corresponding sim card has been deactivated, settings.php will automatically redirect to kundendaten.php
            if (response.RequestMessage.RequestUri.AbsoluteUri.EndsWith("kundendaten.php"))
            {
                if (ExtractSelectablePhoneNumbersFromDropdown(responseHtml)
                        .FirstOrDefault(n => n.SubscriberId == number.SubscriberId) is null)
                {
                    throw new Exception("Could not change the selected phone number");
                }
            }
            else if (ExtractSelectedPhoneNumberFromSettingsPage(responseHtml) != number.Number)
                throw new Exception("Could not change the selected phone number");
        }

        /// <summary>
        /// Loads the account usage for the selected phone number.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="HttpRequestException"></exception>
        public async Task<AccountUsage> GetAccountUsage()
        {
            if (!Connected)
                await Reconnect();
            
            HttpResponseMessage response = await _httpClient.GetAsync(AccountUsagePath);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("Could not get account usage");
            var responseHtml = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(responseHtml);

            var result = new AccountUsage();

            IEnumerable<PackageUsage> ParseBasePageSections()
            {
                var res = new List<PackageUsage>();
                var contentAreas = doc.DocumentNode.SelectNodes("//div[@class='card']");
                foreach (var section in contentAreas)
                {
                    var pu = new PackageUsage();
                    var heading = section.SelectSingleNode(".//h1");
                    if (heading != null)
                    {
                        pu.PackageName = HttpUtility.HtmlDecode(heading.InnerText.TrimEnd(':'));
                    }

                    var progressItems = section.SelectNodes(".//div[@class='progress-item']");
                    if (progressItems != null)
                    {
                        foreach (var progressItem in progressItems)
                        {
                            var progressHeading = progressItem.SelectSingleNode(".//div[@class='progress-heading']");
                            var available = progressItem.SelectSingleNode(".//div[@class='bar-label-left']");
                            var used = progressItem.SelectSingleNode(".//div[@class='bar-label-right']");

                            if (progressHeading == null) continue;
                            var headingLowered = progressHeading.InnerText.ToLower();
                            if (headingLowered.Contains("daten"))
                            {
                                UnitQuota q = headingLowered.Contains("eu") ? pu.DataEu : pu.Data;
                                if (used != null)
                                {
                                    var match = Regex.Match(used.InnerText, @"Verbraucht: (\d*) \(von (\S*)");
                                    var totText = match.Groups[2].Value;
                                    q.Total = totText.ToLower() == "unlimited"
                                        ? int.MaxValue
                                        : int.Parse(totText);
                                    q.Used = int.Parse(match.Groups[1].Value);
                                }
                            }
                            else if (headingLowered.Contains("minuten") && !headingLowered.Contains("eu"))
                            {
                                if (headingLowered.Contains("sms"))
                                    pu.MinutesAndSmsQuotasShared = true;
                                if (used != null)
                                {
                                    var match = Regex.Match(used.InnerText, @"Verbraucht: (\d*) \(von (\S*)");
                                    var totText = match.Groups[2].Value;
                                    pu.Minutes.Total = totText.ToLower() == "unlimited"
                                        ? 10000
                                        : int.Parse(totText);
                                    pu.Minutes.Used = int.Parse(match.Groups[1].Value);
                                }

                                if (pu.MinutesAndSmsQuotasShared)
                                {
                                    pu.Sms = pu.Minutes;
                                }
                            }
                            else if (headingLowered.Contains("sms") && !headingLowered.Contains("minuten") &&
                                     !headingLowered.Contains("kostenwarnung"))
                            {
                                if (used != null)
                                {
                                    var match = Regex.Match(used.InnerText, @"Verbraucht: (\d*) \(von (\S*)");
                                    var totText = match.Groups[2].Value;
                                    pu.Sms.Total = totText.ToLower() == "unlimited"
                                        ? 10000
                                        : int.Parse(totText);
                                    pu.Sms.Used = int.Parse(match.Groups[1].Value);
                                }
                            }
                            else if (headingLowered.Contains("ö") && headingLowered.Contains("eu") &&
                                     headingLowered.Contains("minuten"))
                            {
                                if (used != null)
                                {
                                    var match = Regex.Match(used.InnerText, @"Verbraucht: (\d*) \(von (\S*)");
                                    var totText = match.Groups[2].Value;
                                    pu.AustriaToEuMinutes.Total = totText.ToLower() == "unlimited"
                                        ? 10000
                                        : int.Parse(totText);
                                    pu.AustriaToEuMinutes.Used = int.Parse(match.Groups[1].Value);
                                }
                            }
                        }
                    }

                    var infoTable = section.SelectSingleNode(".//ul[@class='list-group list-group-flush']");
                    if (infoTable != null)
                    {
                        var infoItems = section.SelectNodes(".//li[@class='list-group-item']");
                        if (infoItems != null)
                        {
                            foreach (var item in infoItems.Where(i => i.InnerText.Contains(":")))
                            {
                                var infoTitle =
                                    HttpUtility.HtmlDecode(item.InnerText.Split(':').First());
                                var lowerTitle = infoTitle.ToLower();
                                var infoValue = HttpUtility.HtmlDecode(item.InnerText.Split(new []{':'}, 2)[1].Trim());
                                if (lowerTitle.Contains("datenvolumen") && lowerTitle.Contains("eu"))
                                {
                                    try
                                    {
                                        var match = Regex.Match(infoValue, @"(\d*) MB von (\d*) MB");
                                        pu.DataEu.Total =
                                            int.Parse(match.Groups[2].Value);
                                        pu.DataEu.CorrectRemainingFree(int.Parse(match.Groups[1].Value));
                                    }
                                    catch { }
                                }
                                else if (lowerTitle.Contains("gültigkeit") && lowerTitle.Contains("sim"))
                                {
                                    result.Prepaid = true;
                                    result.SimCardValidUntil =
                                        DateTime.ParseExact(infoValue, "dd.MM.yyyy",
                                            null);
                                }
                                else if (lowerTitle.Contains("letzte aufladung"))
                                {
                                    result.LastRecharge = DateTime.ParseExact(infoValue,
                                        "dd.MM.yyyy", null);
                                }
                                else if (lowerTitle.Contains("guthaben"))
                                {
                                    result.Credit =
                                        decimal.Parse(
                                            item.ChildNodes[1].ChildNodes[0].InnerText.Split(' ')[1]
                                                .Replace(',', '.'));
                                }
                                else if (lowerTitle.Contains("gültig von") ||
                                         lowerTitle.Contains("aktivierung des paket"))
                                {
                                    pu.UnitsValidFrom = DateTime.ParseExact(infoValue,
                                        "dd.MM.yyyy HH:mm", null);
                                }
                                else if (lowerTitle.Contains("gültig bis") ||
                                         lowerTitle.Contains("gültigkeit des paket"))
                                {
                                    pu.UnitsValidUntil = DateTime.ParseExact(infoValue,
                                        "dd.MM.yyyy HH:mm", null);
                                }
                                else
                                {
                                    pu.AdditionalInformation[infoTitle] =
                                        HttpUtility.HtmlDecode(infoValue);
                                }
                            }
                        }
                    }

                    if (!_excludedSections.Contains(pu.PackageName))
                        res.Add(pu);
                }


                // TODO: Aktuelle Kosten section auf neues UI updaten
                var dataItemListTable = doc.DocumentNode.SelectSingleNode("//table[@class='data-item-list']");
                if (dataItemListTable != null)
                {
                    var trs = dataItemListTable.SelectNodes(".//tr");
                    if (trs != null)
                    {
                        foreach (var tr in trs)
                        {
                            var tds = tr.SelectNodes(".//td");
                            if (tds.Count != 2) continue;
                            var lowerTitle = tds[0].InnerText.ToLower();
                            if (lowerTitle.Contains("rechnungsdatum"))
                            {
                                result.InvoiceDate = DateTime.ParseExact(tds[1].InnerText, "dd.MM.yyyy", null);
                            }
                            else if (lowerTitle.Contains("vorläufige kosten"))
                            {
                                result.Cost = decimal.Parse(tds[1].InnerText.Split(' ')[1].Replace(',', '.'));
                            }
                        }
                    }
                }


                return res;
            }

            var sections = ParseBasePageSections();
            result.Number = await GetSelectedPhoneNumber();
            result.PackageUsages = sections.ToList();
            return result;
            // TODO: parse base page
        }

        /// <summary>
        /// Enables Debug Logs in the console. Default: Disabled.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public KontomanagerClient EnableDebugLogging(bool value)
        {
            _enableDebugLogging = value;
            return this;
        }

        /// <summary>
        /// Initializes a session by calling the login endpoint.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> CreateConnection()
        {
            await AcceptCookies();
            return await CreateConnection(_user, _password);
        }

        private async Task AcceptCookies()
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("dosave", "1"),
                new KeyValuePair<string, string>("accept-all", "1")
            });

            HttpResponseMessage response =
                await _httpClient.PostAsync("einstellungen_datenschutz_web.php", content);
            Console.WriteLine(response.ToString());
        }

        /// <summary>
        /// Initializes a Kontomanager.at session.
        /// </summary>
        /// <param name="user"></param>
        /// <param name="password"></param>
        /// <returns></returns>
        private async Task<bool> CreateConnection(string user, string password)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("login_rufnummer", user),
                new KeyValuePair<string, string>("login_passwort", password)
            });

            HttpResponseMessage response = await _httpClient.PostAsync(LoginPath, content);
            IEnumerable<Cookie> responseCookies =
                _cookieContainer.GetCookies(new Uri(BaseUri, LoginPath)).Cast<Cookie>();
            foreach (Cookie cookie in responseCookies)
                Log(cookie.Name + ": " + cookie.Value);
            string responseHTML = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(responseHTML);
            var success = doc.DocumentNode.SelectSingleNode("//form[@name='loginform']") == null;
            if (success)
            {
                _lastConnected = DateTime.Now;
                ConnectionEstablished?.Invoke(this, EventArgs.Empty);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Runs CreateConnection.
        /// </summary>
        /// <returns></returns>
        private async Task<bool> Reconnect()
        {
            Log("Reconnecting...");
            await AcceptCookies();
            return await CreateConnection(_user, _password);
        }

        /// <summary>
        /// Writes to console if _enableDebugLogging is true.
        /// </summary>
        /// <param name="message"></param>
        private void Log(string message)
        {
            if (_enableDebugLogging)
                Console.WriteLine(message);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            _httpClientHandler.Dispose();
        }
    }
}