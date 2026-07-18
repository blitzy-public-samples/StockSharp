namespace StockSharp.Algo;

partial class Connector
{
	internal async ValueTask ApplySubscriptionManagerActionsAsync(ConnectorSubscriptionManager.Actions actions, CancellationToken cancellationToken)
	{
		if (actions == null)
			throw new ArgumentNullException(nameof(actions));

		foreach (var action in actions.Items)
		{
			switch (action.Type)
			{
				case ConnectorSubscriptionManager.Actions.Item.Types.SendInMessage:
					await SendInMessageAsync(action.Message, cancellationToken);
					break;
				case ConnectorSubscriptionManager.Actions.Item.Types.AddOrderStatus:
					_entityCache.AddOrderStatusTransactionId(action.TransactionId);
					break;
				case ConnectorSubscriptionManager.Actions.Item.Types.RemoveOrderStatus:
					_entityCache.RemoveOrderStatusTransactionId(action.TransactionId);
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(actions), action.Type, LocalizedStrings.InvalidValue);
			}
		}
	}

	private void ApplySubscriptionManagerActions(ConnectorSubscriptionManager.Actions actions)
		=> AsyncHelper.Run(() => ApplySubscriptionManagerActionsAsync(actions, default));

	/// <inheritdoc />
	public IEnumerable<Subscription> Subscriptions => _subscriptionManager.Subscriptions;

	/// <inheritdoc />
	public void Subscribe(Subscription subscription)
		=> ApplySubscriptionManagerActions(_subscriptionManager.Subscribe(subscription));

	/// <inheritdoc />
	public void UnSubscribe(Subscription subscription)
		=> ApplySubscriptionManagerActions(_subscriptionManager.UnSubscribe(subscription));

	/// <inheritdoc />
	[Obsolete("Use OwnTradeReceived event.")]
	public event Action<MyTrade> NewMyTrade;

	/// <inheritdoc />
	[Obsolete("Use OrderReceived event.")]
	public event Action<Order> NewOrder;

	/// <inheritdoc />
	[Obsolete("Use OrderReceived event.")]
	public event Action<Order> OrderChanged;

	/// <inheritdoc />
	[Obsolete("Use OrderReceived event.")]
	public event Action<long, Order> OrderEdited;

	/// <inheritdoc />
	[Obsolete("Use OrderRegisterFailReceived event.")]
	public event Action<OrderFail> OrderRegisterFailed;

	/// <inheritdoc />
	[Obsolete("Use OrderCancelFailReceived event.")]
	public event Action<OrderFail> OrderCancelFailed;

	/// <inheritdoc />
	[Obsolete("Use OrderEditFailReceived event.")]
	public event Action<long, OrderFail> OrderEditFailed;

	/// <inheritdoc />
	public event Action<long> MassOrderCanceled;

	/// <inheritdoc />
	public event Action<long, DateTime> MassOrderCanceled2;

	/// <inheritdoc />
	public event Action<long, Exception> MassOrderCancelFailed;

	/// <inheritdoc />
	public event Action<long, Exception, DateTime> MassOrderCancelFailed2;

	/// <inheritdoc />
	[Obsolete("Use PortfolioReceived event.")]
	public event Action<Portfolio> NewPortfolio;

	/// <inheritdoc />
	[Obsolete("Use PortfolioReceived event.")]
	public event Action<Portfolio> PortfolioChanged;

	/// <inheritdoc />
	[Obsolete("Use PositionReceived event.")]
	public event Action<Position> NewPosition;

	/// <inheritdoc />
	[Obsolete("Use PositionReceived event.")]
	public event Action<Position> PositionChanged;

	/// <inheritdoc />
	[Obsolete("Use NewOutMessageAsync event.")]
	public event Action<Message> NewMessage;

	/// <inheritdoc />
	public event Func<Message, CancellationToken, ValueTask> NewOutMessageAsync;

	/// <inheritdoc />
	public event Action<TimeSpan> CurrentTimeChanged;

	/// <inheritdoc />
	public event Action Connected;

	/// <inheritdoc />
	public event Action Disconnected;

	/// <inheritdoc />
	public event Action<Exception> ConnectionError;

	/// <inheritdoc />
	public event Action<IMessageAdapter> ConnectedEx;

	/// <inheritdoc />
	public event Action<IMessageAdapter> DisconnectedEx;

	/// <inheritdoc />
	public event Action<IMessageAdapter, Exception> ConnectionErrorEx;

	/// <inheritdoc cref="IConnector" />
	public event Action<Exception> Error;

	/// <inheritdoc />
	// TODO
	//[Obsolete("Use SecurityReceived and SubscriptionStopped events.")]
	public event Action<SecurityLookupMessage, IEnumerable<Security>, Exception> LookupSecuritiesResult;

	/// <inheritdoc />
	// TODO
	//[Obsolete("Use PortfolioReceived and SubscriptionStopped events.")]
	public event Action<PortfolioLookupMessage, IEnumerable<Portfolio>, Exception> LookupPortfoliosResult;

	/// <inheritdoc />
	// TODO
	[Obsolete("Use SecurityReceived and SubscriptionStopped events.")]
	public event Action<SecurityLookupMessage, IEnumerable<Security>, IEnumerable<Security>, Exception> LookupSecuritiesResult2;

	/// <inheritdoc />
	[Obsolete("Use PortfolioReceived and SubscriptionStopped events.")]
	public event Action<PortfolioLookupMessage, IEnumerable<Portfolio>, IEnumerable<Portfolio>, Exception> LookupPortfoliosResult2;

	/// <inheritdoc />
	public event Action<Security, IEnumerable<KeyValuePair<Level1Fields, object>>, DateTime, DateTime> ValuesChanged;

	/// <inheritdoc />
	public event Action<Subscription, Level1ChangeMessage> Level1Received;

	/// <inheritdoc />
	public event Action<Subscription, IOrderBookMessage> OrderBookReceived;

	/// <inheritdoc />
	public event Action<Subscription, ITickTradeMessage> TickTradeReceived;

	/// <inheritdoc />
	public event Action<Subscription, IOrderLogMessage> OrderLogReceived;

	/// <inheritdoc />
	public event Action<Subscription, Security> SecurityReceived;

	/// <inheritdoc />
	public event Action<Subscription, ExchangeBoard> BoardReceived;

	/// <inheritdoc />
	public event Action<Subscription, News> NewsReceived;

	/// <inheritdoc />
	public event Action<Subscription, ICandleMessage> CandleReceived;

	/// <inheritdoc />
	public event Action<Subscription, MyTrade> OwnTradeReceived;

	/// <inheritdoc />
	public event Action<Subscription, Order> OrderReceived;

	/// <inheritdoc />
	public event Action<Subscription, OrderFail> OrderRegisterFailReceived;

	/// <inheritdoc />
	public event Action<Subscription, OrderFail> OrderCancelFailReceived;

	/// <inheritdoc />
	public event Action<Subscription, OrderFail> OrderEditFailReceived;

	/// <inheritdoc />
	public event Action<Subscription, Portfolio> PortfolioReceived;

	/// <inheritdoc />
	public event Action<Subscription, Position> PositionReceived;

	/// <inheritdoc />
	public event Action<Subscription, DataType> DataTypeReceived;

	/// <inheritdoc />
	public event Action<Subscription> SubscriptionOnline;

	/// <inheritdoc />
	public event Action<Subscription> SubscriptionStarted;

	/// <inheritdoc />
	public event Action<Subscription, Exception> SubscriptionStopped;

	/// <inheritdoc />
	public event Action<Subscription, Exception, bool> SubscriptionFailed;

	/// <inheritdoc />
	public event Action<Subscription, object> SubscriptionReceived;

	/// <inheritdoc />
	public event Action<IMessageAdapter> ConnectionRestored;

	/// <inheritdoc />
	public event Action<IMessageAdapter> ConnectionLost;

	/// <inheritdoc />
	public event Action<long, Exception> ChangePasswordResult;

	// Internal accessors exposing the multicast event delegates to the extracted
	// message-processing component. C# forbids referencing an event from outside its
	// declaring type, so the component reads these delegate-typed properties instead of
	// the events directly. Behavior is unchanged: each getter returns the current
	// invocation list (or null), exactly as an in-type reference to the event would.

	/// <summary>Delegate backing the <see cref="ValuesChanged"/> event.</summary>
	internal Action<Security, IEnumerable<KeyValuePair<Level1Fields, object>>, DateTime, DateTime> ValuesChangedEvent => ValuesChanged;

	/// <summary>Delegate backing the <see cref="OrderBookReceived"/> event.</summary>
	internal Action<Subscription, IOrderBookMessage> OrderBookReceivedEvent => OrderBookReceived;

	/// <summary>Delegate backing the <see cref="TickTradeReceived"/> event.</summary>
	internal Action<Subscription, ITickTradeMessage> TickTradeReceivedEvent => TickTradeReceived;

	/// <summary>Delegate backing the <see cref="OrderLogReceived"/> event.</summary>
	internal Action<Subscription, IOrderLogMessage> OrderLogReceivedEvent => OrderLogReceived;

	/// <summary>Delegate backing the <see cref="SecurityReceived"/> event.</summary>
	internal Action<Subscription, Security> SecurityReceivedEvent => SecurityReceived;

	/// <summary>Delegate backing the <see cref="BoardReceived"/> event.</summary>
	internal Action<Subscription, ExchangeBoard> BoardReceivedEvent => BoardReceived;

	/// <summary>Delegate backing the <see cref="NewsReceived"/> event.</summary>
	internal Action<Subscription, News> NewsReceivedEvent => NewsReceived;

	/// <summary>Delegate backing the <see cref="CandleReceived"/> event.</summary>
	internal Action<Subscription, ICandleMessage> CandleReceivedEvent => CandleReceived;

	/// <summary>Delegate backing the <see cref="OwnTradeReceived"/> event.</summary>
	internal Action<Subscription, MyTrade> OwnTradeReceivedEvent => OwnTradeReceived;

	/// <summary>Delegate backing the <see cref="OrderReceived"/> event.</summary>
	internal Action<Subscription, Order> OrderReceivedEvent => OrderReceived;

	/// <summary>Delegate backing the <see cref="OrderRegisterFailReceived"/> event.</summary>
	internal Action<Subscription, OrderFail> OrderRegisterFailReceivedEvent => OrderRegisterFailReceived;

	/// <summary>Delegate backing the <see cref="OrderCancelFailReceived"/> event.</summary>
	internal Action<Subscription, OrderFail> OrderCancelFailReceivedEvent => OrderCancelFailReceived;

	/// <summary>Delegate backing the <see cref="OrderEditFailReceived"/> event.</summary>
	internal Action<Subscription, OrderFail> OrderEditFailReceivedEvent => OrderEditFailReceived;

	/// <summary>Delegate backing the <see cref="PortfolioReceived"/> event.</summary>
	internal Action<Subscription, Portfolio> PortfolioReceivedEvent => PortfolioReceived;

	/// <summary>Delegate backing the <see cref="PositionReceived"/> event.</summary>
	internal Action<Subscription, Position> PositionReceivedEvent => PositionReceived;

	/// <summary>Delegate backing the <see cref="DataTypeReceived"/> event.</summary>
	internal Action<Subscription, DataType> DataTypeReceivedEvent => DataTypeReceived;

	// Thin forwarders that delegate all event-firing orchestration to the extracted
	// ConnectorEventDispatcher component (see IConnectorEventDispatcher). The public event
	// declarations above stay on this facade because C# events can only be raised from
	// within their declaring type; the dispatcher fires them through the internal invoker
	// hooks (Fire*) declared at the end of this file.

	internal void RaiseNewMyTrade(MyTrade trade)
		=> _eventDispatcher.RaiseNewMyTrade(trade);

	internal void RaiseNewOrder(Order order)
		=> _eventDispatcher.RaiseNewOrder(order);

	internal void RaiseOrderChanged(Order order)
		=> _eventDispatcher.RaiseOrderChanged(order);

	internal void RaiseOrderEdited(long transactionId, Order order)
		=> _eventDispatcher.RaiseOrderEdited(transactionId, order);

	internal void RaiseOrderRegisterFailed(long transactionId, OrderFail fail)
		=> _eventDispatcher.RaiseOrderRegisterFailed(transactionId, fail);

	internal void RaiseOrderCancelFailed(long transactionId, OrderFail fail)
		=> _eventDispatcher.RaiseOrderCancelFailed(transactionId, fail);

	internal void RaiseOrderEditFailed(long transactionId, OrderFail fail)
		=> _eventDispatcher.RaiseOrderEditFailed(transactionId, fail);

	internal void RaiseMassOrderCanceled(long transactionId, DateTime time)
		=> _eventDispatcher.RaiseMassOrderCanceled(transactionId, time);

	internal void RaiseMassOrderCancelFailed(long transactionId, Exception error, DateTime time)
		=> _eventDispatcher.RaiseMassOrderCancelFailed(transactionId, error, time);

	private void RaiseNewPortfolio(Portfolio portfolio)
		=> _eventDispatcher.RaiseNewPortfolio(portfolio);

	private void RaisePortfolioChanged(Portfolio portfolio)
		=> _eventDispatcher.RaisePortfolioChanged(portfolio);

	private void RaiseNewPosition(Position position)
		=> _eventDispatcher.RaiseNewPosition(position);

	internal void RaisePositionChanged(Position position)
		=> _eventDispatcher.RaisePositionChanged(position);

	internal void RaiseConnected()
		=> _eventDispatcher.RaiseConnected();

	internal void RaiseConnectedEx(IMessageAdapter adapter)
		=> _eventDispatcher.RaiseConnectedEx(adapter);

	internal void RaiseDisconnected()
		=> _eventDispatcher.RaiseDisconnected();

	internal void RaiseDisconnectedEx(IMessageAdapter adapter)
		=> _eventDispatcher.RaiseDisconnectedEx(adapter);

	internal void RaiseConnectionError(Exception exception)
		=> _eventDispatcher.RaiseConnectionError(exception);

	internal void RaiseConnectionErrorEx(IMessageAdapter adapter, Exception exception)
		=> _eventDispatcher.RaiseConnectionErrorEx(adapter, exception);

	internal void RaiseConnectionLost(IMessageAdapter adapter)
		=> _eventDispatcher.RaiseConnectionLost(adapter);

	internal void RaiseConnectionRestored(IMessageAdapter adapter)
		=> _eventDispatcher.RaiseConnectionRestored(adapter);

	private void RaiseCurrentTimeChanged(TimeSpan diff)
		=> _eventDispatcher.RaiseCurrentTimeChanged(diff);

	internal void RaiseLookupSecuritiesResult(SecurityLookupMessage message, Exception error, Security[] newSecurities)
		=> _eventDispatcher.RaiseLookupSecuritiesResult(message, error, newSecurities);

	internal void RaiseLookupPortfoliosResult(PortfolioLookupMessage message, Exception error, Portfolio[] newPortfolios)
		=> _eventDispatcher.RaiseLookupPortfoliosResult(message, error, newPortfolios);

	internal void RaiseValuesChanged(Security security, IEnumerable<KeyValuePair<Level1Fields, object>> changes, DateTime serverTime, DateTime localTime)
		=> _eventDispatcher.RaiseValuesChanged(security, changes, serverTime, localTime);

	internal void RaiseChangePassword(long transactionId, Exception error)
		=> _eventDispatcher.RaiseChangePassword(transactionId, error);

	internal void RaiseMarketDataSubscriptionSucceeded(MarketDataMessage message, Subscription subscription)
		=> _eventDispatcher.RaiseMarketDataSubscriptionSucceeded(message, subscription);

	internal void RaiseMarketDataSubscriptionFailed(MarketDataMessage origin, SubscriptionResponseMessage reply, Subscription subscription)
		=> _eventDispatcher.RaiseMarketDataSubscriptionFailed(origin, reply, subscription);

	internal void RaiseMarketDataUnSubscriptionSucceeded(MarketDataMessage message, Subscription subscription)
		=> _eventDispatcher.RaiseMarketDataUnSubscriptionSucceeded(message, subscription);

	internal void RaiseMarketDataUnSubscriptionFailed(MarketDataMessage origin, SubscriptionResponseMessage reply, Subscription subscription)
		=> _eventDispatcher.RaiseMarketDataUnSubscriptionFailed(origin, reply, subscription);

	internal void RaiseMarketDataSubscriptionFinished(SubscriptionFinishedMessage message, Subscription subscription)
		=> _eventDispatcher.RaiseMarketDataSubscriptionFinished(message, subscription);

	internal void RaiseMarketDataUnexpectedCancelled(MarketDataMessage message, Exception error, Subscription subscription)
		=> _eventDispatcher.RaiseMarketDataUnexpectedCancelled(message, error, subscription);

	internal void RaiseMarketDataSubscriptionOnline(Subscription subscription)
		=> _eventDispatcher.RaiseMarketDataSubscriptionOnline(subscription);

	internal void RaiseSubscriptionStarted(Subscription subscription)
		=> _eventDispatcher.RaiseSubscriptionStarted(subscription);

	private ValueTask RaiseNewMessage(Message message, CancellationToken cancellationToken)
		=> _eventDispatcher.RaiseNewMessage(message, cancellationToken);

	internal bool? RaiseReceived<TEntity>(TEntity entity, ISubscriptionIdMessage message, Action<Subscription, TEntity> evt)
		=> _eventDispatcher.RaiseReceived(entity, message, evt);

	internal bool? RaiseReceived<TEntity>(TEntity entity, ISubscriptionIdMessage message, Action<Subscription, TEntity> evt, out bool? anyCanOnline)
		=> _eventDispatcher.RaiseReceived(entity, message, evt, out anyCanOnline);

	internal void RaiseReceived<TEntity>(TEntity entity, IEnumerable<Subscription> subscriptions, Action<Subscription, TEntity> evt)
		=> _eventDispatcher.RaiseReceived(entity, subscriptions, evt);

	internal bool? RaiseReceived<TEntity>(TEntity entity, IEnumerable<Subscription> subscriptions, Action<Subscription, TEntity> evt, out bool? anyCanOnline)
		=> _eventDispatcher.RaiseReceived(entity, subscriptions, evt, out anyCanOnline);

	internal void RaiseSubscriptionReceived(Subscription subscription, object arg)
		=> _eventDispatcher.RaiseSubscriptionReceived(subscription, arg);

	internal void RaiseLevel1Received(Subscription subscription, Level1ChangeMessage message)
		=> _eventDispatcher.RaiseLevel1Received(subscription, message);

	/// <summary>
	/// To call the event <see cref="Error"/>.
	/// </summary>
	/// <param name="exception">Data processing error.</param>
	protected void RaiseError(Exception exception)
	{
		if (exception is null)
			throw new ArgumentNullException(nameof(exception));

		ErrorCount++;
		Error?.Invoke(exception);

		LogError(exception);
	}

	/// <summary>
	/// Invokes <see cref="RaiseError"/> on behalf of the extracted message-processing component,
	/// preserving the protected visibility of the raiser on the facade rather than widening it.
	/// </summary>
	/// <param name="exception">Data processing error.</param>
	internal void RaiseErrorCore(Exception exception) => RaiseError(exception);

	/// <summary>
	/// </summary>
	protected virtual void RaiseSubscriptionFailed(Subscription subscription, Exception error, bool isSubscribe)
	{
		if (subscription == null)
			throw new ArgumentNullException(nameof(subscription));

		if (error == null)
			throw new ArgumentNullException(nameof(error));

		SubscriptionFailed?.Invoke(subscription, error, isSubscribe);
	}

	/// <summary>
	/// Invokes the overridable <see cref="RaiseSubscriptionFailed"/> on behalf of the extracted
	/// event dispatcher, preserving the protected-virtual override point on the facade.
	/// </summary>
	/// <param name="subscription">The affected subscription.</param>
	/// <param name="error">The error that caused the failure.</param>
	/// <param name="isSubscribe"><see langword="true"/> for a subscribe failure; <see langword="false"/> for an unsubscribe failure.</param>
	internal void RaiseSubscriptionFailedCore(Subscription subscription, Exception error, bool isSubscribe)
		=> RaiseSubscriptionFailed(subscription, error, isSubscribe);

	// Internal invoker hooks. Each fires exactly one facade-owned event using the same
	// null-conditional (?.Invoke) semantics as the original monolithic implementation, so the
	// extracted ConnectorEventDispatcher can raise these events without any behavioral change.

	internal void FireNewMyTrade(MyTrade trade) => NewMyTrade?.Invoke(trade);

	internal void FireNewOrder(Order order) => NewOrder?.Invoke(order);

	internal void FireOrderChanged(Order order) => OrderChanged?.Invoke(order);

	internal void FireOrderEdited(long transactionId, Order order) => OrderEdited?.Invoke(transactionId, order);

	internal void FireOrderRegisterFailed(OrderFail fail) => OrderRegisterFailed?.Invoke(fail);

	internal void FireOrderCancelFailed(OrderFail fail) => OrderCancelFailed?.Invoke(fail);

	internal void FireOrderEditFailed(long transactionId, OrderFail fail) => OrderEditFailed?.Invoke(transactionId, fail);

	internal void FireMassOrderCanceled(long transactionId) => MassOrderCanceled?.Invoke(transactionId);

	internal void FireMassOrderCanceled2(long transactionId, DateTime time) => MassOrderCanceled2?.Invoke(transactionId, time);

	internal void FireMassOrderCancelFailed(long transactionId, Exception error) => MassOrderCancelFailed?.Invoke(transactionId, error);

	internal void FireMassOrderCancelFailed2(long transactionId, Exception error, DateTime time) => MassOrderCancelFailed2?.Invoke(transactionId, error, time);

	internal void FireNewPortfolio(Portfolio portfolio) => NewPortfolio?.Invoke(portfolio);

	internal void FirePortfolioChanged(Portfolio portfolio) => PortfolioChanged?.Invoke(portfolio);

	internal void FireNewPosition(Position position) => NewPosition?.Invoke(position);

	internal void FirePositionChanged(Position position) => PositionChanged?.Invoke(position);

	internal void FireConnected() => Connected?.Invoke();

	internal void FireConnectedEx(IMessageAdapter adapter) => ConnectedEx?.Invoke(adapter);

	internal void FireDisconnected() => Disconnected?.Invoke();

	internal void FireDisconnectedEx(IMessageAdapter adapter) => DisconnectedEx?.Invoke(adapter);

	internal void FireConnectionError(Exception exception) => ConnectionError?.Invoke(exception);

	internal void FireConnectionErrorEx(IMessageAdapter adapter, Exception exception) => ConnectionErrorEx?.Invoke(adapter, exception);

	internal void FireCurrentTimeChanged(TimeSpan diff) => CurrentTimeChanged?.Invoke(diff);

	internal void FireLookupSecuritiesResult(SecurityLookupMessage message, IEnumerable<Security> newSecurities, Exception error) => LookupSecuritiesResult?.Invoke(message, newSecurities, error);

	internal void FireLookupSecuritiesResult2(SecurityLookupMessage message, IEnumerable<Security> online, IEnumerable<Security> newSecurities, Exception error) => LookupSecuritiesResult2?.Invoke(message, online, newSecurities, error);

	internal void FireLookupPortfoliosResult(PortfolioLookupMessage message, IEnumerable<Portfolio> newPortfolios, Exception error) => LookupPortfoliosResult?.Invoke(message, newPortfolios, error);

	internal void FireLookupPortfoliosResult2(PortfolioLookupMessage message, IEnumerable<Portfolio> online, IEnumerable<Portfolio> newPortfolios, Exception error) => LookupPortfoliosResult2?.Invoke(message, online, newPortfolios, error);

	internal void FireValuesChanged(Security security, IEnumerable<KeyValuePair<Level1Fields, object>> changes, DateTime serverTime, DateTime localTime) => ValuesChanged?.Invoke(security, changes, serverTime, localTime);

	internal void FireChangePasswordResult(long transactionId, Exception error) => ChangePasswordResult?.Invoke(transactionId, error);

	internal void FireSubscriptionOnline(Subscription subscription) => SubscriptionOnline?.Invoke(subscription);

	internal void FireSubscriptionStarted(Subscription subscription) => SubscriptionStarted?.Invoke(subscription);

	internal void FireSubscriptionStopped(Subscription subscription, Exception error) => SubscriptionStopped?.Invoke(subscription, error);

	internal void FireSubscriptionReceived(Subscription subscription, object arg) => SubscriptionReceived?.Invoke(subscription, arg);

	internal void FireLevel1Received(Subscription subscription, Level1ChangeMessage message) => Level1Received?.Invoke(subscription, message);

	internal void FireConnectionLost(IMessageAdapter adapter) => ConnectionLost?.Invoke(adapter);

	internal void FireConnectionRestored(IMessageAdapter adapter) => ConnectionRestored?.Invoke(adapter);

	internal void FireNewMessage(Message message) => NewMessage?.Invoke(message);

	internal ValueTask FireNewOutMessageAsync(Message message, CancellationToken cancellationToken) => NewOutMessageAsync?.Invoke(message, cancellationToken) ?? default;
}
