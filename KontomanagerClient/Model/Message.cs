using System;

namespace KontomanagerClient.Model
{
    public class Message
    {
        private string _body;

        public string RecipientNumber { get; }

        public string Body
        {
            get => _body;
            protected set => _body = value;
        }

        private bool _sent;
        public bool Sent => _sent;

        /// <summary>
        /// Creates a new message with a given recipient number and message _body.
        /// </summary>
        /// <param name="recipientNumber">Needs to start with either +[country code] or 00[country code]</param>
        /// <param name="body"></param>
        public Message(string recipientNumber, string body)
        {
            if (recipientNumber.StartsWith("+")) RecipientNumber = "+" + recipientNumber.Substring(1);
            else RecipientNumber = recipientNumber;
            this._body = body;
        }
        
        /// <summary>
        /// Fires when a Client has attempted to send the message.
        /// </summary>
        public event EventHandler<MessageSendResult> SendingAttempted;
        
        internal void NotifySendingAttempt(MessageSendResult res)
        {
            if (res == MessageSendResult.Ok) _sent = true;
            SendingAttempted?.Invoke(this, res);
        }
    }
}