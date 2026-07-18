namespace StockSharp.Algo.Strategies;

// StrategyOld ITransactionProvider fragment: the legacy engine's explicit transaction-provider surface.
// TransactionIdGenerator and the NewOrder stream delegate to the underlying connector via SafeGetConnector();
// the MassOrderCanceled/MassOrderCanceled2/MassOrderCancelFailed/MassOrderCancelFailed2/LookupPortfoliosResult/
// LookupPortfoliosResult2 events are no-op stubs and CancelOrders forwards to CancelActiveOrders. Part of the
// legacy StrategyOld monolith, retained for reference/equivalence testing only and superseded by the modern
// Strategy engine (see Strategy_TransactionProvider.cs).
partial class StrategyOld
{
	IdGenerator ITransactionProvider.TransactionIdGenerator => SafeGetConnector().TransactionIdGenerator;

	private Action<Order> _newOrder;

	event Action<Order> ITransactionProvider.NewOrder
	{
		add => _newOrder += value;
		remove => _newOrder -= value;
	}

	event Action<long> ITransactionProvider.MassOrderCanceled
	{
		add { }
		remove { }
	}

	event Action<long, DateTime> ITransactionProvider.MassOrderCanceled2
	{
		add { }
		remove { }
	}

	event Action<long, Exception> ITransactionProvider.MassOrderCancelFailed
	{
		add { }
		remove { }
	}

	event Action<long, Exception, DateTime> ITransactionProvider.MassOrderCancelFailed2
	{
		add { }
		remove { }
	}

	event Action<PortfolioLookupMessage, IEnumerable<Portfolio>, Exception> ITransactionProvider.LookupPortfoliosResult
	{
		add { }
		remove { }
	}

	event Action<PortfolioLookupMessage, IEnumerable<Portfolio>, IEnumerable<Portfolio>, Exception> ITransactionProvider.LookupPortfoliosResult2
	{
		add { }
		remove { }
	}

	void ITransactionProvider.CancelOrders(bool? isStopOrder, Portfolio portfolio, Sides? direction, ExchangeBoard board, Security security, SecurityTypes? securityType, long? transactionId)
	{
		CancelActiveOrders(isStopOrder, portfolio, direction, board, security, securityType, transactionId);
	}
}