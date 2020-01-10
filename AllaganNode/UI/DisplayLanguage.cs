namespace AllaganNode.UI
{
    public class DisplayLanguage
    {
        public string Code { get; set; }
        public string DisplayName { get; set; }

        public DisplayLanguage(string code, string displayName)
        {
            Code = code;
            DisplayName = displayName;
        }
    }
}