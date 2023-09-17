namespace ScubaDiver.API
{
    public record CharStar
    {
        public nuint Address { get; set; }
        public string Value { get; set; }

        public CharStar(nuint address, string value)
        {
            Address = address;
            Value = value;
        }
    }
}