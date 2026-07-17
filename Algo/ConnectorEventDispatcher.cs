namespace StockSharp.Algo;

/// <summary>
/// Default implementation of <see cref="IConnectorEventDispatcher"/> that performs the event-firing
/// orchestration extracted from the <see cref="Connector"/> fa&#231;ade's <c>Raise*</c> helpers.
/// </summary>
/// <remarks>
/// The public events remain declared on the <see cref="Connector"/> fa&#231;ade (C# events can only be raised
/// from within their declaring type); this dispatcher fires them through the fa&#231;ade's internal invoker
/// hooks, preserving the exact firing order, the null-conditional invocation semantics, logging,
/// connection-state transitions and counters of the original monolithic implementation.
/// </remarks>
public class ConnectorEventDispatcher : IConnectorEventDispatcher
{
	private readonly Connector _connector;

	/// <summary>
	/// Initializes a new instance of the <see cref="ConnectorEventDispatcher"/> class.
	/// </summary>
	/// <param name="connector">The owning connector fa&#231;ade whose events are fired by this dispatcher.</param>
	/// <exception cref="ArgumentNullException"><paramref name="connector"/> is <see langword="null"/>.</exception>
	public ConnectorEventDispatcher(Connector connector)
	{
		_connector = connector ?? throw new ArgumentNullException(nameof(connector));
	}

	/// <inheritdoc />
	public void RaiseNewMyTrade(MyTrade trade)
	{
		_connector.LogInfo("New own trade: {0}", trade);

		_connector.FireNewMyTrade(trade);
	}

	/// <inheritdoc />
	public void RaiseNewOrder(Order order)
	{
		_connector.FireNewOrder(order);
	}

	/// <inheritdoc />
	public void RaiseOrderChanged(Order order)
	{
		_connector.FireOrderChanged(order);
	}

	/// <inheritdoc />
	public void RaiseOrderEdited(long transactionId, Order order)
	{
		_connector.LogDebug("Order {0} edited by transaction {1}.", order, transactionId);
		_connector.FireOrderEdited(transactionId, order);
	}

	/// <inheritdoc />
	public void RaiseOrderFailed(string name, long transactionId, OrderFail fail, Action<long, OrderFail> failed)
	{
		_connector.AddErrorLog(() => name + Environment.NewLine + fail.Order + Environment.NewLine + fail.Error);
		failed?.Invoke(transactionId, fail);
	}

	/// <inheritdoc />
	public void RaiseOrderRegisterFailed(long transactionId, OrderFail fail)
	{
		RaiseOrderFailed("OrderRegisterFailed", transactionId, fail, (id, f) => _connector.FireOrderRegisterFailed(f));
	}

	/// <inheritdoc />
	public void RaiseOrderCancelFailed(long transactionId, OrderFail fail)
	{
		RaiseOrderFailed("OrderCancelFailed", transactionId, fail, (id, f) => _connector.FireOrderCancelFailed(f));
	}

	/// <inheritdoc />
	public void RaiseOrderEditFailed(long transactionId, OrderFail fail)
	{
		RaiseOrderFailed("OrderEditFailed", transactionId, fail, _connector.FireOrderEditFailed);
	}

	/// <inheritdoc />
	public void RaiseMassOrderCanceled(long transactionId, DateTime time)
	{
		_connector.FireMassOrderCanceled(transactionId);
		_connector.FireMassOrderCanceled2(transactionId, time);
	}

	/// <inheritdoc />
	public void RaiseMassOrderCancelFailed(long transactionId, Exception error, DateTime time)
	{
		_connector.FireMassOrderCancelFailed(transactionId, error);
		_connector.FireMassOrderCancelFailed2(transactionId, error, time);
	}

	/// <inheritdoc />
	public void RaiseNewPortfolio(Portfolio portfolio)
	{
		_connector.FireNewPortfolio(portfolio);
	}

	/// <inheritdoc />
	public void RaisePortfolioChanged(Portfolio portfolio)
	{
		_connector.FirePortfolioChanged(portfolio);
	}

	/// <inheritdoc />
	public void RaiseNewPosition(Position position)
	{
		_connector.FireNewPosition(position);
	}

	/// <inheritdoc />
	public void RaisePositionChanged(Position position)
	{
		_connector.FirePositionChanged(position);
	}

	/// <inheritdoc />
	public void RaiseConnected()
	{
		_connector.ConnectionState = ConnectionStates.Connected;
		_connector.FireConnected();
	}

	/// <inheritdoc />
	public void RaiseConnectedEx(IMessageAdapter adapter)
	{
		_connector.FireConnectedEx(adapter);
	}

	/// <inheritdoc />
	public void RaiseDisconnected()
	{
		_connector.ConnectionState = ConnectionStates.Disconnected;
		_connector.FireDisconnected();
	}

	/// <inheritdoc />
	public void RaiseDisconnectedEx(IMessageAdapter adapter)
	{
		_connector.FireDisconnectedEx(adapter);
	}

	/// <inheritdoc />
	public void RaiseConnectionError(Exception exception)
	{
		if (exception == null)
			throw new ArgumentNullException(nameof(exception));

		_connector.ConnectionState = ConnectionStates.Failed;
		_connector.FireConnectionError(exception);

		_connector.LogError(exception);
	}

	/// <inheritdoc />
	public void RaiseConnectionErrorEx(IMessageAdapter adapter, Exception exception)
	{
		if (exception == null)
			throw new ArgumentNullException(nameof(exception));

		_connector.FireConnectionErrorEx(adapter, exception);
	}

	/// <inheritdoc />
	public void RaiseConnectionLost(IMessageAdapter adapter)
	{
		_connector.FireConnectionLost(adapter);
	}

	/// <inheritdoc />
	public void RaiseConnectionRestored(IMessageAdapter adapter)
	{
		_connector.FireConnectionRestored(adapter);
	}

	/// <inheritdoc />
	public void RaiseCurrentTimeChanged(TimeSpan diff)
	{
		_connector.FireCurrentTimeChanged(diff);
	}

	/// <inheritdoc />
	public void RaiseLookupSecuritiesResult(SecurityLookupMessage message, Exception error, Security[] newSecurities)
	{
		_connector.FireLookupSecuritiesResult(message, newSecurities, error);
		_connector.FireLookupSecuritiesResult2(message, [], newSecurities, error);
	}

	/// <inheritdoc />
	public void RaiseLookupPortfoliosResult(PortfolioLookupMessage message, Exception error, Portfolio[] newPortfolios)
	{
		_connector.FireLookupPortfoliosResult(message, newPortfolios, error);
		_connector.FireLookupPortfoliosResult2(message, [], newPortfolios, error);
	}

	/// <inheritdoc />
	public void RaiseValuesChanged(Security security, IEnumerable<KeyValuePair<Level1Fields, object>> changes, DateTime serverTime, DateTime localTime)
	{
		_connector.FireValuesChanged(security, changes, serverTime, localTime);
	}

	/// <inheritdoc />
	public void RaiseChangePassword(long transactionId, Exception error)
	{
		_connector.FireChangePasswordResult(transactionId, error);
	}

	/// <inheritdoc />
	public void RaiseMarketDataSubscriptionSucceeded(MarketDataMessage message, Subscription subscription)
	{
		if (message == null)
			throw new ArgumentNullException(nameof(message));

		if (subscription == null)
			throw new ArgumentNullException(nameof(subscription));

		var securityId = subscription.SecurityId;

		var msg = LocalizedStrings.SubscribedOk.Put(securityId, message.DataType2);

		if (message.From != null && message.To != null)
			msg += LocalizedStrings.FromTill.Put(message.From.Value, message.To.Value);

		_connector.LogDebug(msg + ".");

		RaiseSubscriptionStarted(subscription);
	}

	/// <inheritdoc />
	public void RaiseMarketDataSubscriptionFailed(MarketDataMessage origin, SubscriptionResponseMessage reply, Subscription subscription)
	{
		if (origin == null)
			throw new ArgumentNullException(nameof(origin));

		if (reply == null)
			throw new ArgumentNullException(nameof(reply));

		if (subscription == null)
			throw new ArgumentNullException(nameof(subscription));

		var securityId = subscription.SecurityId;
		var error = reply.Error ?? new NotSupportedException(LocalizedStrings.SubscriptionNotSupported.Put(origin));

		if (reply.IsNotSupported())
			_connector.LogWarning(LocalizedStrings.SubscriptionNotSupported, origin);
		else
			_connector.LogError(LocalizedStrings.SubscribedError, securityId, origin.DataType2, error.Message);

		_connector.RaiseSubscriptionFailedCore(subscription, error, true);
	}

	/// <inheritdoc />
	public void RaiseMarketDataUnSubscriptionSucceeded(MarketDataMessage message, Subscription subscription)
	{
		if (message == null)
			throw new ArgumentNullException(nameof(message));

		if (subscription == null)
			throw new ArgumentNullException(nameof(subscription));

		var securityId = subscription.SecurityId;

		var msg = LocalizedStrings.UnSubscribedOk.Put(securityId, message.DataType2);

		if (message.From != null && message.To != null)
			msg += LocalizedStrings.FromTill.Put(message.From.Value, message.To.Value);

		_connector.LogDebug(msg + ".");

		RaiseSubscriptionStopped(subscription, null);
	}

	/// <inheritdoc />
	public void RaiseMarketDataUnSubscriptionFailed(MarketDataMessage origin, SubscriptionResponseMessage reply, Subscription subscription)
	{
		if (origin == null)
			throw new ArgumentNullException(nameof(origin));

		if (reply == null)
			throw new ArgumentNullException(nameof(reply));

		if (subscription == null)
			throw new ArgumentNullException(nameof(subscription));

		var securityId = subscription.SecurityId;
		var error = reply.Error ?? new NotSupportedException();

		_connector.LogError(LocalizedStrings.UnSubscribedError, securityId, origin.DataType2, error.Message);

		_connector.RaiseSubscriptionFailedCore(subscription, error, false);
	}

	/// <inheritdoc />
	public void RaiseMarketDataSubscriptionFinished(SubscriptionFinishedMessage message, Subscription subscription)
	{
		if (message == null)
			throw new ArgumentNullException(nameof(message));

		if (subscription == null)
			throw new ArgumentNullException(nameof(subscription));

		var securityId = subscription.SecurityId;

		_connector.LogDebug(LocalizedStrings.SubscriptionFinished, securityId, message);

		RaiseSubscriptionStopped(subscription, null);
	}

	/// <inheritdoc />
	public void RaiseMarketDataUnexpectedCancelled(MarketDataMessage message, Exception error, Subscription subscription)
	{
		if (message == null)
			throw new ArgumentNullException(nameof(message));

		if (error == null)
			throw new ArgumentNullException(nameof(error));

		if (subscription == null)
			throw new ArgumentNullException(nameof(subscription));

		var securityId = subscription.SecurityId;

		_connector.LogError(LocalizedStrings.SubscriptionUnexpectedCancelled, securityId, message.DataType2, error.Message);

		RaiseSubscriptionStopped(subscription, error);
	}

	/// <inheritdoc />
	public void RaiseMarketDataSubscriptionOnline(Subscription subscription)
	{
		if (subscription == null)
			throw new ArgumentNullException(nameof(subscription));

		var securityId = subscription.SecurityId;

		_connector.LogDebug(LocalizedStrings.SubscriptionOnline, securityId, subscription.SubscriptionMessage);

		RaiseSubscriptionOnline(subscription);
	}

	/// <inheritdoc />
	public void RaiseSubscriptionOnline(Subscription subscription)
	{
		if (subscription == null)
			throw new ArgumentNullException(nameof(subscription));

		_connector.FireSubscriptionOnline(subscription);
	}

	/// <inheritdoc />
	public void RaiseSubscriptionStarted(Subscription subscription)
	{
		if (subscription == null)
			throw new ArgumentNullException(nameof(subscription));

		_connector.FireSubscriptionStarted(subscription);
	}

	/// <inheritdoc />
	public void RaiseSubscriptionStopped(Subscription subscription, Exception error)
	{
		if (subscription == null)
			throw new ArgumentNullException(nameof(subscription));

		_connector.FireSubscriptionStopped(subscription, error);
	}

	/// <inheritdoc />
	public ValueTask RaiseNewMessage(Message message, CancellationToken cancellationToken)
	{
		_connector.FireNewMessage(message);
		return _connector.FireNewOutMessageAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	public bool? RaiseReceived<TEntity>(TEntity entity, ISubscriptionIdMessage message, Action<Subscription, TEntity> evt)
	{
		return RaiseReceived(entity, message, evt, out _);
	}

	/// <inheritdoc />
	public bool? RaiseReceived<TEntity>(TEntity entity, ISubscriptionIdMessage message, Action<Subscription, TEntity> evt, out bool? anyCanOnline)
	{
		return RaiseReceived(entity, _connector._subscriptionManager.GetSubscriptions(message), evt, out anyCanOnline);
	}

	/// <inheritdoc />
	public void RaiseReceived<TEntity>(TEntity entity, IEnumerable<Subscription> subscriptions, Action<Subscription, TEntity> evt)
	{
		RaiseReceived(entity, subscriptions, evt, out _);
	}

	/// <inheritdoc />
	public bool? RaiseReceived<TEntity>(TEntity entity, IEnumerable<Subscription> subscriptions, Action<Subscription, TEntity> evt, out bool? anyCanOnline)
	{
		if (subscriptions is null)
			throw new ArgumentNullException(nameof(subscriptions));

		bool? anyOnline = null;
		anyCanOnline = null;

		foreach (var subscription in subscriptions)
		{
			anyOnline = anyOnline == true || subscription.State == SubscriptionStates.Online;
			anyCanOnline = anyCanOnline == true || (subscription.State == SubscriptionStates.Active && !subscription.SubscriptionMessage.IsHistoryOnly());

			evt?.Invoke(subscription, entity);
			RaiseSubscriptionReceived(subscription, entity);
		}

		return anyOnline;
	}

	/// <inheritdoc />
	public void RaiseSubscriptionReceived(Subscription subscription, object arg)
	{
		_connector.FireSubscriptionReceived(subscription, arg);
	}

	/// <inheritdoc />
	public void RaiseLevel1Received(Subscription subscription, Level1ChangeMessage message)
	{
		_connector.FireLevel1Received(subscription, message);
	}
}
