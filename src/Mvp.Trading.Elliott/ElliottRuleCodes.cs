namespace Mvp.Trading.Elliott;

/// <summary>
/// Rule codes used by Elliott candidate validation.
/// </summary>
public static class ElliottRuleCodes
{
    public const string ImpulseWave2BeyondWave1Start = "EW_IMP_R1_W2_NOT_BEYOND_W1_START";
    public const string ImpulseWave3NotShortest = "EW_IMP_R2_W3_NOT_SHORTEST";
    public const string ImpulseWave4OverlapWave1 = "EW_IMP_R3_W4_NO_OVERLAP_W1";
    public const string PivotsInsufficient = "EW_PIVOTS_INSUFFICIENT";
    public const string TimeframeUnsupported = "EW_TIMEFRAME_UNSUPPORTED";
}
