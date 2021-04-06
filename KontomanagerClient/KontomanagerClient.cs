using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
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

        #endregion
        
        #region Events

        /// <summary>
        /// Fires when the connection to Kontomanager was successfully made.
        /// </summary>
        public event EventHandler ConnectionEstablished;
        
        #endregion

        private readonly Uri BaseURI;
        private readonly Uri LoginURI;
        private readonly Uri SendURI;

        private DateTime _lastConnected = DateTime.MinValue;

        private CookieContainer _cookieContainer = new ();

        private readonly string _user;
        private readonly string _password;

        #region Send Queue
        private BlockingCollection<Message> Messages = new ();
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
            BaseURI = baseUri;
            LoginURI = loginUri;
            SendURI = sendUri;
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
                    Log($"Waiting {delay.TotalSeconds} seconds for resending message. Result was {res}. {Messages.Count} messages waiting in queue.");
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
                using HttpClientHandler handler = new HttpClientHandler();
                handler.CookieContainer = _cookieContainer;
                using var client = new HttpClient(handler);

                HttpResponseMessage response = await client.PostAsync(SendURI, content);
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
                using HttpClientHandler handler = new HttpClientHandler();
                handler.CookieContainer = _cookieContainer;
                using var client = new HttpClient(handler);

                HttpResponseMessage response = await client.GetAsync(SendURI);
                var responseHTML = await response.Content.ReadAsStringAsync();
                var regex = new Regex(".*<input type=\"hidden\" name=\"token\" value=\"([^\"]*)\">");
                var match = regex.Match(responseHTML);
                return match.Groups[1].Value;
            }
            catch (Exception e)
            {
                Log("Error: " + e.Message);
                return string.Empty;
            }
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
            using var client = new HttpClient(handler);

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("login_rufnummer", user),
                new KeyValuePair<string, string>("login_passwort", password)
            });

            HttpResponseMessage response = await client.PostAsync(LoginURI, content);
            IEnumerable<Cookie> responseCookies = _cookieContainer.GetCookies(LoginURI).Cast<Cookie>();
            foreach (Cookie cookie in responseCookies)
                Log(cookie.Name + ": " + cookie.Value);
            string responseHTML = await response.Content.ReadAsStringAsync();
            if (responseHTML.Contains("Sie sind angemeldet als:"))
            {
                _lastConnected = DateTime.Now;
                ConnectionEstablished?.Invoke(this, EventArgs.Empty);
                return true;
            }
            else return false;
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
    
    public class MessageCounter
    {
        private int _timeout;
        private int _limit;
            
        private List<DateTime> SuccessfulMessages;
        private List<DateTime> FailedMessages;

        public MessageCounter(int timeoutSeconds, int limit)
        {
            _timeout = timeoutSeconds;
            _limit = limit;
            SuccessfulMessages = new ();
            FailedMessages = new();
        }

        public void Success()
        {
            SuccessfulMessages.Add(DateTime.Now);
        }

        public void Fail()
        {
            FailedMessages.Add(DateTime.Now);
        }

        public void Purge()
        {
            var purgeStart = DateTime.Now.Subtract(TimeSpan.FromSeconds(_timeout));

            SuccessfulMessages.RemoveAll(m => m < purgeStart);
            FailedMessages.RemoveAll(m => m < purgeStart);
        }

        public int CountSuccesses()
        {
            Purge();
            return SuccessfulMessages.Count;
        }

        public int CountFailures()
        {
            Purge();
            return FailedMessages.Count;
        }

        public TimeSpan TimeUntilNewElementPossible()
        {
            if (CanSend()) return TimeSpan.Zero;
            if (SuccessfulMessages.Count() < _limit && FailedMessages.OrderByDescending(e => e).FirstOrDefault() > DateTime.Now.Subtract(TimeSpan.FromMinutes(1)))
                return TimeSpan.FromMinutes(3);
            var last = SuccessfulMessages.OrderByDescending(e => e).ElementAtOrDefault(_limit);
            return (DateTime.Now - last).Duration();
        }

        public bool CanSend()
        {
            Purge();
            return SuccessfulMessages.Count() < _limit && (!(SuccessfulMessages.Any() && FailedMessages.Any()) || SuccessfulMessages.Max() > FailedMessages.Max());
        }
    }
}