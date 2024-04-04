using System;
using System.Collections.Generic;
using System.Linq;

namespace KontomanagerClient
{
    [Obsolete("This class is not used anymore and will be removed in the future.")]
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
            SuccessfulMessages = new List<DateTime>();
            FailedMessages = new List<DateTime>();
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