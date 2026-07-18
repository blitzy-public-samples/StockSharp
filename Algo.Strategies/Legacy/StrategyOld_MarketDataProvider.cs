namespace StockSharp.Algo.Strategies;

// Fragment: StrategyOld's explicit IMarketDataProvider implementation. These members are
// deliberately no-op / default stubs; the legacy engine does not itself serve market-data
// values here. The ValuesChanged, LookupSecuritiesResult and LookupSecuritiesResult2 events
// have empty add/remove accessors, GetSecurityValue returns default, and GetLevel1Fields
// returns an empty sequence, so these stubs are intentional, not incomplete. This file is
// part of the legacy StrategyOld monolith, retained for reference/equivalence testing only
// and superseded by the modern Strategy engine (see Strategy_MarketDataProvider.cs).
partial class StrategyOld
{
	event Action<Security, IEnumerable<KeyValuePair<Level1Fields, object>>, DateTime, DateTime> IMarketDataProvider.ValuesChanged
	{
		add { }
		remove { }
	}

	object IMarketDataProvider.GetSecurityValue(Security security, Level1Fields field)
		=> default;

	IEnumerable<Level1Fields> IMarketDataProvider.GetLevel1Fields(Security security)
		=> [];

	event Action<SecurityLookupMessage, IEnumerable<Security>, Exception> IMarketDataProvider.LookupSecuritiesResult
	{
		add { }
		remove { }
	}

	event Action<SecurityLookupMessage, IEnumerable<Security>, IEnumerable<Security>, Exception> IMarketDataProvider.LookupSecuritiesResult2
	{
		add { }
		remove { }
	}
}