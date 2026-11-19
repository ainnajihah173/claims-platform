namespace Claims.Api.Domain;

public enum ClaimStatus { Submitted, InReview, AwaitingInfo, Settled, Rejected }

public enum ClaimType { Motor, Property }

public enum Market { MY, SG, HK, AU, TH, ID }

public enum ClaimEventType { Submitted, Assigned, InfoRequested, InfoProvided, ReserveSet, Settled, Rejected }

public static class MarketExtensions
{
    // One currency per market (assumption 3).
    public static string Currency(this Market market) => market switch
    {
        Market.AU => "AUD",
        Market.ID => "IDR",
        Market.SG => "SGD",
        Market.HK => "HKD",
        Market.MY => "MYR",
        Market.TH => "THB",
        _ => throw new ArgumentOutOfRangeException(nameof(market)),
    };
}
