namespace KontomanagerClient
{

    public class PhoneNumber
    {
        /// <summary>
        /// User chosen name for the number.
        /// </summary>
        public string Name { get; set; }
        public string Number { get; set; }
        public string SubscriberId { get; set; }
        
        public bool Selected { get; set; }

        public override string ToString()
        {
            if (Selected) return $"{Number} (Selected)";
            return Number;
        }
    }
}