namespace MovieReviewApp.Constants;

/// <summary>
/// Cache window configuration - SINGLE SOURCE OF TRUTH.
/// Used by PersonRotationService and PhaseCalculator to maintain consistent cache boundaries.
/// </summary>
public static class CacheConstants
{
    /// <summary>
    /// Number of months to cache forward from DateTime.Now.
    /// Cache range: clubStartDate → DateTime.Now + WINDOW_MONTHS (2 years future).
    /// </summary>
    public const int WINDOW_MONTHS = 24;
}
