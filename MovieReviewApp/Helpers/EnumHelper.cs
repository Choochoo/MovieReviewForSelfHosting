namespace MovieReviewApp.Helpers
{
    public static class EnumHelper
    {
        public static string GetFormattedName(Enum value)
        {
            string name = value.ToString();
            return string.Concat(name.Select(x => char.IsUpper(x) ? " " + x : x.ToString())).TrimStart(' ');
        }
    }

}
