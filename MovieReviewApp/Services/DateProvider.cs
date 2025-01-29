namespace MovieReviewApp.Services
{
    public static class DateProvider
    {
        private static DateTime? _customDate;// = new DateTime(2025,3,1);

        public static DateTime Now => (_customDate ?? DateTime.Now).ToUniversalTime();

        public static void SetCustomDate(DateTime? date)
        {
            _customDate = date;
        }

        public static void ResetDate()
        {
            _customDate = null;
        }
    }
}
