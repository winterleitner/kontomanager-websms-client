namespace KontomanagerClient
{

    public class PhoneNumber
    {
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