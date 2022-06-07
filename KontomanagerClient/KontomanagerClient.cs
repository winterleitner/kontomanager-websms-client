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
        private bool _autoReconnect = true;
        private bool _useQueue = false;
        private bool _enableDebugLogging = false;
        private bool _exceptionOnInvalidNumberFormat = false;

        protected HashSet<string> _excludedSections = new HashSet<string>()
        {
            "Ukraine Freieinheiten", "Ihre Kostenkontrolle"
        };

        #endregion

        #region Events

        /// <summary>
        /// Fires when the connection to Kontomanager was successfully made.
        /// </summary>
        public event EventHandler ConnectionEstablished;

        #endregion

        protected readonly Uri BaseUri;
        protected readonly Uri LoginUri;
        protected readonly Uri SendUri;
        protected readonly Uri SettingsUri;

        private DateTime _lastConnected = DateTime.MinValue;

        private CookieContainer _cookieContainer = new CookieContainer();

        private readonly string _user;
        private readonly string _password;

        #region Send Queue

        private BlockingCollection<Message> Messages = new BlockingCollection<Message>();
        public MessageCounter _counter = new MessageCounter(60 * 60, 50);

        #endregion


        public bool Connected => DateTime.Now - _lastConnected < TimeSpan.FromSeconds(_sessionTimeoutSeconds);


        /// <summary>
        /// Initializes the client with credentials to the respective kontomanager web portal. (*.kontomanager.at)
        /// </summary>
        /// <param name="user"></param>
        /// <param name="password"></param>
        protected KontomanagerClient(string user, string password, Uri baseUri, Uri loginUri, Uri sendUri)
        {
            _user = user;
            _password = password;
            BaseUri = baseUri;
            LoginUri = loginUri;
            SendUri = sendUri;

            /*
             * More uris can be changed in subclasses via protected fields
             */
            SettingsUri = new Uri(Path.Combine(BaseUri.AbsoluteUri, "einstellungen.php"));
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

        public KontomanagerClient UseAutoReconnect(bool value)
        {
            _autoReconnect = value;
            return this;
        }

        /// <summary>
        /// Configures the Client to use a FIFO queue to ensure all messages are sent (provided that the app does not shut down).
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public KontomanagerClient UseQueue()
        {
            _useQueue = true;
            Task.Run(StartQueueConsumer);
            return this;
        }

        private async void StartQueueConsumer()
        {
            while (!Messages.IsCompleted)
            {
                if (!_counter.CanSend())
                {
                    var delay = _counter.TimeUntilNewElementPossible();
                    Log($"Waiting {delay.TotalSeconds} seconds for next message. {Messages.Count} messages waiting.");
                    await Task.Delay(_counter.TimeUntilNewElementPossible());
                }

                var m = Messages.Take();
                var res = await SendMessageWithReconnect(m);
                while (res != MessageSendResult.Ok)
                {
                    var delay = _counter.TimeUntilNewElementPossible();
                    Log(
                        $"Waiting {delay.TotalSeconds} seconds for resending message. Result was {res}. {Messages.Count} messages waiting in queue.");
                    await Task.Delay(_counter.TimeUntilNewElementPossible());
                }

                await Task.Delay(1000);
            }
        }

        /// <summary>
        /// Specifies if the SendMessage method should throw an exception when the number format specified is invalid.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public KontomanagerClient ThrowExceptionOnInvalidNumberFormat(bool value)
        {
            _exceptionOnInvalidNumberFormat = value;
            return this;
        }

        /// <summary>
        /// Returns the phone number for which the client is currently active.
        /// This is relevant when multiple phone numbers are grouped in one account.
        /// </summary>
        /// <returns></returns>
        public async Task<string> GetSelectedPhoneNumber()
        {
            using (HttpClientHandler handler = new HttpClientHandler())
            {
                handler.CookieContainer = _cookieContainer;
                using (var client = new HttpClient(handler))
                {
                    HttpResponseMessage response = await client.GetAsync(SettingsUri);
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException("Could not determine the selected phone number");
                    var responseHtml = await response.Content.ReadAsStringAsync();
                    string number = null;
                    if (response.RequestMessage.RequestUri.AbsoluteUri.EndsWith("kundendaten.php"))
                    {
                        number = ExtractSelectedPhoneNumberFromHeaderElement(responseHtml);
                        var nums = ExtractSelectablePhoneNumbersFromHomePage(responseHtml).ToList();
                        number = !nums.Any() ? ExtractSelectedPhoneNumberFromHeaderElement(responseHtml) : nums.FirstOrDefault(n => n.Selected)?.Number;
                    }
                    else number = ExtractSelectedPhoneNumberFromSettingsPage(responseHtml);
                    if (number is null) throw new Exception("Phone number could not be found");
                    return number;
                }
            }
        }

        private string ExtractSelectedPhoneNumberFromHeaderElement(string pageHtml)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(pageHtml);

            var nodes = doc.DocumentNode.SelectNodes("//div[@class='loggedin']");
            var selectedNumberNode = nodes.LastOrDefault();
            if (selectedNumberNode is null) return null;
            var pattern = @"\d+\/\d*";
            var match = Regex.Match(selectedNumberNode.InnerHtml, pattern);
            return $"43{match.Value.Replace("/", "").Substring(1)}";
        }

        private string ExtractSelectedPhoneNumberFromSettingsPage(string settingsPageHtml)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(settingsPageHtml);

            foreach (var row in doc.DocumentNode.SelectNodes("//tr"))
            {
                var childTds = row.SelectNodes("td");
                if (childTds is null || childTds.Count == 0 || !childTds.First().InnerText.StartsWith("Ihre Rufnummer")) continue;
                return childTds.Last().InnerText;
            }

            return null;
        }

        private IEnumerable<PhoneNumber> ExtractSelectablePhoneNumbersFromHomePage(string homePageHtml)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(homePageHtml);
            var form = doc.GetElementbyId("subscriber_dropdown_form");
            if (form is null)
                return new List<PhoneNumber>();
            return form.SelectNodes("//select/option").Select(o => new PhoneNumber()
            {
                Number = o.InnerText.Split('-').First().Trim(),
                SubscriberId = o.GetAttributeValue("value", null),
                Selected = o.GetAttributeValue("selected", "") == "selected"
            });
        }

        /// <summary>
        /// Returns a list of all phone numbers linked to the account.
        /// If the account is a single-sim account without a group, an empty list is returned.
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<PhoneNumber>> GetSelectablePhoneNumbers()
        {
            using (HttpClientHandler handler = new HttpClientHandler())
            {
                handler.CookieContainer = _cookieContainer;
                using (var client = new HttpClient(handler))
                {
                    HttpResponseMessage response = await client.GetAsync(SettingsUri);
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException("Could not determine the selectable phone numbers");
                    var responseHtml = await response.Content.ReadAsStringAsync();
                    return ExtractSelectablePhoneNumbersFromHomePage(responseHtml);
                }
            }
        }

        /// <summary>
        /// Sets the phone number to use for the client.
        /// </summary>
        public async Task SelectPhoneNumber(PhoneNumber number)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("groupaction", "change_subscriber"),
                new KeyValuePair<string, string>("subscriber", number.SubscriberId),
            });
            using (HttpClientHandler handler = new HttpClientHandler())
            {
                handler.CookieContainer = _cookieContainer;
                using (var client = new HttpClient(handler))
                {
                    HttpResponseMessage response = await client.PostAsync(SettingsUri, content);
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException("Could not change the selected phone number");
                    var responseHtml = await response.Content.ReadAsStringAsync();
                    // If the corresponding sim card has been deactivated, settings.php will automatically redirect to kundendaten.php
                    if (response.RequestMessage.RequestUri.AbsoluteUri.EndsWith("kundendaten.php"))
                    {
                        if (ExtractSelectablePhoneNumbersFromHomePage(responseHtml)
                                .FirstOrDefault(n => n.SubscriberId == number.SubscriberId) is null)
                        {
                            throw new Exception("Could not change the selected phone number");
                        }
                    }
                    else if (ExtractSelectedPhoneNumberFromSettingsPage(responseHtml) != number.Number)
                        throw new Exception("Could not change the selected phone number");
                }
            }
        }

        public async Task<AccountUsage> GetAccountUsage()
        {
            using (HttpClientHandler handler = new HttpClientHandler())
            {
                handler.CookieContainer = _cookieContainer;
                using (var client = new HttpClient(handler))
                {
                    HttpResponseMessage response = await client.GetAsync(BaseUri);
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException("Could not get account usage");
                    var responseHtml = await response.Content.ReadAsStringAsync();
                    var doc = new HtmlDocument();
                    doc.LoadHtml(responseHtml);

                    var result = new AccountUsage();

                    IEnumerable<PackageUsage> ParseBasePageSections()
                    {
                        var res = new List<PackageUsage>();
                        var contentAreas = doc.DocumentNode.SelectNodes("//div[@class='progress-list']");
                        foreach (var section in contentAreas)
                        {
                            var pu = new PackageUsage();
                            var heading = section.SelectSingleNode("h3");
                            if (heading != null)
                            {
                                pu.PackageName = HttpUtility.HtmlDecode(heading.InnerText.TrimEnd(':'));
                            }

                            var progressItems = section.SelectNodes("div[@class='progress-item']");
                            if (progressItems != null)
                            {
                                foreach (var progressItem in progressItems)
                                {
                                    var ul = progressItem.SelectSingleNode("div[@class='progress-heading left']");
                                    var ur = progressItem.SelectSingleNode("div[@class='progress-heading right']");
                                    var bl = progressItem.SelectSingleNode("div[@class='bar-label']");
                                    var br = progressItem.SelectSingleNode("div[@class='bar-label-right']");

                                    if (ul == null) continue;
                                    var ulTextLowered = ul.InnerText.ToLower();
                                    if (ulTextLowered.Contains("daten"))
                                    {
                                        UnitQuota q = ulTextLowered.Contains("eu") ? pu.DataEu : pu.Data;
                                        if (ur != null)
                                        {
                                            var totText = ur.InnerText.Split(' ').First();
                                            q.Total = totText.ToLower() == "unlimited"
                                                ? 999999
                                                : int.Parse(ur.InnerText.Split(' ').First());
                                        }

                                        if (bl != null)
                                        {
                                            q.Used = int.Parse(bl.ChildNodes[0].InnerText.Trim().Split(' ').Last());
                                        }
                                    }
                                    else if (ulTextLowered.Contains("minuten") ||
                                             (ur != null && ur.InnerText.ToLower().Contains("minuten")))
                                    {
                                        if (ur != null)
                                        {
                                            if (ur.InnerText.ToLower().Contains("sms"))
                                                pu.MinutesAndSmsQuotasShared = true;
                                            if (ur.InnerText.ToLower().Split(' ').First() == "unlimited")
                                                pu.Minutes.Total = 10000;
                                            else pu.Minutes.Total = int.Parse(ulTextLowered.Split(' ').First());
                                        }

                                        if (bl != null)
                                        {
                                            pu.Minutes.Used = int.Parse(bl.InnerText.Split(' ').Last());
                                        }

                                        if (pu.MinutesAndSmsQuotasShared)
                                        {
                                            pu.Sms = pu.Minutes;
                                        }
                                    }
                                    else if (ulTextLowered.Contains("sms") &&
                                             !ulTextLowered.Contains("kostenwarnung") ||
                                             (ur != null && ur.InnerText.ToLower().Contains("sms")))
                                    {
                                        if (ur != null)
                                        {
                                            if (ur.InnerText.ToLower().Split(' ').First() == "unlimited")
                                                pu.Sms.Total = 10000;
                                            else pu.Sms.Total = int.Parse(ur.InnerText.ToLower().Split(' ').First());
                                        }

                                        if (bl != null)
                                        {
                                            pu.Sms.Used = int.Parse(bl.InnerText.Split(' ').Last());
                                        }
                                    }
                                    else if (ulTextLowered.Contains("ö") && ulTextLowered.Contains("eu") &&
                                             ulTextLowered.Contains("minuten"))
                                    {
                                        if (ur != null)
                                        {
                                            if (ulTextLowered.Split(' ').First() == "unlimited")
                                                pu.AustriaToEuMinutes.Total = 10000;
                                            else
                                                pu.AustriaToEuMinutes.Total =
                                                    int.Parse(ulTextLowered.Split(' ').First());
                                        }

                                        if (bl != null)
                                        {
                                            pu.AustriaToEuMinutes.Used = int.Parse(bl.InnerText.Split(' ').First());
                                        }
                                    }
                                }
                            }

                            var infoTable = section.SelectSingleNode(".//table[@class='info-list']");
                            if (infoTable != null)
                            {
                                var infoItems = section.SelectNodes(".//td[@class='info-item']");
                                if (infoItems != null)
                                {
                                    foreach (var item in infoItems)
                                    {
                                        if (item.ChildNodes.Count == 2)
                                        {
                                            var infoTitle = HttpUtility.HtmlDecode(item.ChildNodes[0].InnerText.TrimEnd(':', ' '));
                                            var lowerTitle = infoTitle.ToLower();
                                            if (lowerTitle.Contains("datenvolumen") && lowerTitle.Contains("eu"))
                                            {
                                                try
                                                {
                                                    pu.DataEu.Total =
                                                        int.Parse(item.ChildNodes[1].InnerText.Split(' ')[3]);
                                                    pu.DataEu.CorrectRemainingFree(
                                                        int.Parse(item.ChildNodes[1].InnerText.Split(' ')[0]));
                                                }
                                                catch
                                                {
                                                }
                                            }
                                            else if (lowerTitle.Contains("gültigkeit") && lowerTitle.Contains("sim"))
                                            {
                                                result.Prepaid = true;
                                                result.SimCardValidUntil = DateTime.ParseExact(item.ChildNodes[1].InnerText, "dd.MM.yyyy", null);
                                            }
                                            else if (lowerTitle.Contains("letzte aufladung"))
                                            {
                                                result.LastRecharge = DateTime.ParseExact(item.ChildNodes[1].InnerText, "dd.MM.yyyy", null);
                                            }
                                            else if (lowerTitle.Contains("guthaben"))
                                            {
                                                result.Credit =
                                                    decimal.Parse(item.ChildNodes[1].ChildNodes[0].InnerText.Split(' ')[1].Replace(',', '.'));
                                            }
                                            else if (lowerTitle.Contains("gültig von") || lowerTitle.Contains("aktivierung des paket"))
                                            {
                                                pu.UnitsValidFrom = DateTime.ParseExact(item.ChildNodes[1].InnerText,
                                                    "dd.MM.yyyy HH:mm", null);
                                            }
                                            else if (lowerTitle.Contains("gültig bis") || lowerTitle.Contains("gültigkeit des paket"))
                                            {
                                                pu.UnitsValidUntil = DateTime.ParseExact(item.ChildNodes[1].InnerText,
                                                    "dd.MM.yyyy HH:mm", null);
                                            }
                                            else
                                            {
                                                pu.AdditionalInformation[infoTitle] = HttpUtility.HtmlDecode(item.ChildNodes[1].InnerText);
                                            }
                                        }
                                    }
                                }
                            }
                            
                            if (!_excludedSections.Contains(pu.PackageName))
                                res.Add(pu);
                        }

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
            }
        }


        /// <summary>
        /// Depending on configuration enqueues or directly sends the message m.
        /// If the queue is used, MessageSendResult.Enqueued is returned. The actual result is then obtained by subscribing to the message's SendingAttempted event.
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        public async Task<MessageSendResult> SendMessage(Message m)
        {
            if (_useQueue)
            {
                Messages.Add(m);
                return MessageSendResult.MessageEnqueued;
            }
            else
            {
                if (_autoReconnect)
                    return await SendMessageWithReconnect(m);
                else return await CallMessageSendingEndpoint(m);
            }
        }

        public async Task<MessageSendResult> SendMessage(string recipient, string message)
        {
            return await SendMessage(new Message(recipient, message));
        }

        /// <summary>
        /// Calls the Message Sending endpoint and tries to send the message. Returns the result.
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        private async Task<MessageSendResult> CallMessageSendingEndpoint(Message m)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("telefonbuch", "-"),
                new KeyValuePair<string, string>("to_netz", "a"),
                new KeyValuePair<string, string>("to_nummer", m.RecipientNumber),
                new KeyValuePair<string, string>("nachricht", m.Body),
                new KeyValuePair<string, string>("token", await GetToken())
            });
            try
            {
                using (HttpClientHandler handler = new HttpClientHandler())
                {
                    handler.CookieContainer = _cookieContainer;
                    using (var client = new HttpClient(handler))
                    {
                        HttpResponseMessage response = await client.PostAsync(SendUri, content);
                        var responseHTML = await response.Content.ReadAsStringAsync();
                        MessageSendResult res;
                        if (responseHTML.Contains("erfolgreich"))
                            res = MessageSendResult.Ok;
                        else if (responseHTML.Contains("Pro Rufnummer sind maximal"))
                            res = MessageSendResult.LimitReached;
                        else if (responseHTML.Contains(
                                     "Eine oder mehrere SMS konnte(n) nicht versendet werden, da die angegebene Empfängernummer ungültig war.")
                                )
                        {
                            if (_exceptionOnInvalidNumberFormat)
                                throw new FormatException(
                                    $"The format of the recipient number {m.RecipientNumber} does not match the expected format 00[country][number_without_leading_0]");
                            else return MessageSendResult.InvalidNumberFormat;
                        }

                        else res = MessageSendResult.SessionExpired;

                        m.NotifySendingAttempt(res);
                        if (res == MessageSendResult.Ok) _counter.Success();
                        else _counter.Fail();
                        return res;
                    }
                }
            }
            catch (Exception e)
            {
                if (e is InvalidOperationException || e is HttpRequestException || e is TaskCanceledException)
                {
                    Log("Error: " + e.Message);
                    m.NotifySendingAttempt(MessageSendResult.OtherError);
                    return MessageSendResult.OtherError;
                }

                throw;
            }
        }

        /// <summary>
        /// Sends messages with automatic reconnection enabled.
        /// If message sending fails once due to expired session, the sending process is retried once.
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        private async Task<MessageSendResult> SendMessageWithReconnect(Message m)
        {
            if (!Connected) await Reconnect();
            Log($"Sending SMS to {m.RecipientNumber}");
            var res = await CallMessageSendingEndpoint(m);
            if (res == MessageSendResult.SessionExpired)
            {
                Log("Kontomanager connection expired.");
                await Reconnect();
                Log("Resending...");
                return await CallMessageSendingEndpoint(m);
            }

            // else return true if message sent.
            return res;
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
        /// Obtains the hidden input "token" from the HTML form needed to send a message.
        /// </summary>
        /// <returns></returns>
        private async Task<string> GetToken()
        {
            try
            {
                using (HttpClientHandler handler = new HttpClientHandler())
                {
                    handler.CookieContainer = _cookieContainer;
                    using (var client = new HttpClient(handler))
                    {
                        HttpResponseMessage response = await client.GetAsync(SendUri);
                        var responseHTML = await response.Content.ReadAsStringAsync();
                        var regex = new Regex(".*<input type=\"hidden\" name=\"token\" value=\"([^\"]*)\">");
                        var match = regex.Match(responseHTML);
                        return match.Groups[1].Value;
                    }
                }
            }
            catch (Exception e)
            {
                Log("Error: " + e.Message);
                return string.Empty;
            }
        }

        /// <summary>
        /// Initializes a session by calling the login endpoint.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> CreateConnection()
        {
            return await CreateConnection(_user, _password);
        }

        /// <summary>
        /// Initializes a Kontomanager.at session.
        /// </summary>
        /// <param name="user"></param>
        /// <param name="password"></param>
        /// <returns></returns>
        private async Task<bool> CreateConnection(string user, string password)
        {
            HttpClientHandler handler = new HttpClientHandler();
            _cookieContainer = new CookieContainer();
            handler.CookieContainer = _cookieContainer;
            using (var client = new HttpClient(handler))
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("login_rufnummer", user),
                    new KeyValuePair<string, string>("login_passwort", password)
                });

                HttpResponseMessage response = await client.PostAsync(LoginUri, content);
                IEnumerable<Cookie> responseCookies = _cookieContainer.GetCookies(LoginUri).Cast<Cookie>();
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
        }

        /// <summary>
        /// Runs CreateConnection.
        /// </summary>
        /// <returns></returns>
        private async Task<bool> Reconnect()
        {
            Log("Reconnecting...");
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
            Messages?.Dispose();
        }
    }
}