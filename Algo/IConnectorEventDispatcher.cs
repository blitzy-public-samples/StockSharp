namespace StockSharp.Algo;

/// <summary>
/// Abstraction over the connector's event-raising (firing) logic. Implementations fire the events
/// declared on the connector fa&#231;ade, preserving the exact firing order and side-effects
/// (logging, connection-state transitions and counters).
/// </summary>
/// <remarks>
/// The public events themselves remain declared on the connector fa&#231;ade because C# events can only
/// be raised from within their declaring type; implementations therefore fire those events through the
/// fa&#231;ade's internal invoker hooks while keeping identical null-conditional invocation semantics.
/// </remarks>
public interface IConnectorEventDispatcher
{
	// Order / lifecycle events.

	/// <summary>
	/// Raises the new own trade notification for the specified trade.
	/// </summary>
	/// <param name="trade">The newly received own trade.</param>
	void RaiseNewMyTrade(MyTrade trade);

	/// <summary>
	/// Raises the new order notification for the specified order.
	/// </summary>
	/// <param name="order">The newly registered order.</param>
	void RaiseNewOrder(Order order);

	/// <summary>
	/// Raises the order changed notification for the specified order.
	/// </summary>
	/// <param name="order">The order whose state changed.</param>
	void RaiseOrderChanged(Order order);

	/// <summary>
	/// Raises the order edited notification for the specified order.
	/// </summary>
	/// <param name="transactionId">The transaction identifier of the edit operation.</param>
	/// <param name="order">The edited order.</param>
	void RaiseOrderEdited(long transactionId, Order order);

	/// <summary>
	/// Raises a generic order failure, logging the failure and invoking the supplied failure callback.
	/// </summary>
	/// <param name="name">The name of the failed operation used for logging.</param>
	/// <param name="transactionId">The transaction identifier associated with the failure.</param>
	/// <param name="fail">The order failure details.</param>
	/// <param name="failed">The callback invoked with the transaction identifier and the failure.</param>
	void RaiseOrderFailed(string name, long transactionId, OrderFail fail, Action<long, OrderFail> failed);

	/// <summary>
	/// Raises the order registration failure notification.
	/// </summary>
	/// <param name="transactionId">The transaction identifier associated with the failure.</param>
	/// <param name="fail">The order failure details.</param>
	void RaiseOrderRegisterFailed(long transactionId, OrderFail fail);

	/// <summary>
	/// Raises the order cancellation failure notification.
	/// </summary>
	/// <param name="transactionId">The transaction identifier associated with the failure.</param>
	/// <param name="fail">The order failure details.</param>
	void RaiseOrderCancelFailed(long transactionId, OrderFail fail);

	/// <summary>
	/// Raises the order edit failure notification.
	/// </summary>
	/// <param name="transactionId">The transaction identifier associated with the failure.</param>
	/// <param name="fail">The order failure details.</param>
	void RaiseOrderEditFailed(long transactionId, OrderFail fail);

	/// <summary>
	/// Raises the mass order cancellation notification.
	/// </summary>
	/// <param name="transactionId">The transaction identifier of the mass cancel request.</param>
	/// <param name="time">The time at which the mass cancellation occurred.</param>
	void RaiseMassOrderCanceled(long transactionId, DateTime time);

	/// <summary>
	/// Raises the mass order cancellation failure notification.
	/// </summary>
	/// <param name="transactionId">The transaction identifier of the mass cancel request.</param>
	/// <param name="error">The error that caused the mass cancellation to fail.</param>
	/// <param name="time">The time at which the failure occurred.</param>
	void RaiseMassOrderCancelFailed(long transactionId, Exception error, DateTime time);

	// Portfolio / position events.

	/// <summary>
	/// Raises the new portfolio notification for the specified portfolio.
	/// </summary>
	/// <param name="portfolio">The newly received portfolio.</param>
	void RaiseNewPortfolio(Portfolio portfolio);

	/// <summary>
	/// Raises the portfolio changed notification for the specified portfolio.
	/// </summary>
	/// <param name="portfolio">The portfolio whose values changed.</param>
	void RaisePortfolioChanged(Portfolio portfolio);

	/// <summary>
	/// Raises the new position notification for the specified position.
	/// </summary>
	/// <param name="position">The newly received position.</param>
	void RaiseNewPosition(Position position);

	/// <summary>
	/// Raises the position changed notification for the specified position.
	/// </summary>
	/// <param name="position">The position whose values changed.</param>
	void RaisePositionChanged(Position position);

	// Connection events.

	/// <summary>
	/// Raises the connected notification, transitioning the connection state to connected.
	/// </summary>
	void RaiseConnected();

	/// <summary>
	/// Raises the adapter-specific connected notification.
	/// </summary>
	/// <param name="adapter">The adapter that initiated the event.</param>
	void RaiseConnectedEx(IMessageAdapter adapter);

	/// <summary>
	/// Raises the disconnected notification, transitioning the connection state to disconnected.
	/// </summary>
	void RaiseDisconnected();

	/// <summary>
	/// Raises the adapter-specific disconnected notification.
	/// </summary>
	/// <param name="adapter">The adapter that initiated the event.</param>
	void RaiseDisconnectedEx(IMessageAdapter adapter);

	/// <summary>
	/// Raises the connection error notification, transitioning the connection state to failed and logging the error.
	/// </summary>
	/// <param name="exception">The connection error.</param>
	void RaiseConnectionError(Exception exception);

	/// <summary>
	/// Raises the adapter-specific connection error notification.
	/// </summary>
	/// <param name="adapter">The adapter that initiated the event.</param>
	/// <param name="exception">The connection error.</param>
	void RaiseConnectionErrorEx(IMessageAdapter adapter, Exception exception);

	/// <summary>
	/// Raises the connection lost notification for the specified adapter.
	/// </summary>
	/// <param name="adapter">The adapter whose connection was lost.</param>
	void RaiseConnectionLost(IMessageAdapter adapter);

	/// <summary>
	/// Raises the connection restored notification for the specified adapter.
	/// </summary>
	/// <param name="adapter">The adapter whose connection was restored.</param>
	void RaiseConnectionRestored(IMessageAdapter adapter);

	// Time / lookup / values events.

	/// <summary>
	/// Raises the current time changed notification.
	/// </summary>
	/// <param name="diff">The elapsed time since the previous notification; the first notification passes a zero difference.</param>
	void RaiseCurrentTimeChanged(TimeSpan diff);

	/// <summary>
	/// Raises the securities lookup result notification.
	/// </summary>
	/// <param name="message">The originating lookup message.</param>
	/// <param name="error">The lookup error, or <see langword="null"/> when the lookup completed successfully.</param>
	/// <param name="newSecurities">The securities found by the lookup.</param>
	void RaiseLookupSecuritiesResult(SecurityLookupMessage message, Exception error, Security[] newSecurities);

	/// <summary>
	/// Raises the portfolios lookup result notification.
	/// </summary>
	/// <param name="message">The originating lookup message.</param>
	/// <param name="error">The lookup error, or <see langword="null"/> when the lookup completed successfully.</param>
	/// <param name="newPortfolios">The portfolios found by the lookup.</param>
	void RaiseLookupPortfoliosResult(PortfolioLookupMessage message, Exception error, Portfolio[] newPortfolios);

	/// <summary>
	/// Raises the level1 values changed notification for the specified security.
	/// </summary>
	/// <param name="security">The security whose values changed.</param>
	/// <param name="changes">The changed level1 field/value pairs.</param>
	/// <param name="serverTime">The server time of the change.</param>
	/// <param name="localTime">The local time at which the change was received.</param>
	void RaiseValuesChanged(Security security, IEnumerable<KeyValuePair<Level1Fields, object>> changes, DateTime serverTime, DateTime localTime);

	/// <summary>
	/// Raises the change-password result notification.
	/// </summary>
	/// <param name="transactionId">The transaction identifier of the change-password request.</param>
	/// <param name="error">The error that occurred, or <see langword="null"/> when the operation completed successfully.</param>
	void RaiseChangePassword(long transactionId, Exception error);

	// Market-data subscription events.

	/// <summary>
	/// Raises the notification for a successful market-data subscription, logging the outcome and marking the subscription as started.
	/// </summary>
	/// <param name="message">The market-data subscription request.</param>
	/// <param name="subscription">The affected subscription.</param>
	void RaiseMarketDataSubscriptionSucceeded(MarketDataMessage message, Subscription subscription);

	/// <summary>
	/// Raises the notification for a failed market-data subscription.
	/// </summary>
	/// <param name="origin">The originating market-data subscription request.</param>
	/// <param name="reply">The subscription response carrying the error.</param>
	/// <param name="subscription">The affected subscription.</param>
	void RaiseMarketDataSubscriptionFailed(MarketDataMessage origin, SubscriptionResponseMessage reply, Subscription subscription);

	/// <summary>
	/// Raises the notification for a successful market-data unsubscription.
	/// </summary>
	/// <param name="message">The market-data unsubscription request.</param>
	/// <param name="subscription">The affected subscription.</param>
	void RaiseMarketDataUnSubscriptionSucceeded(MarketDataMessage message, Subscription subscription);

	/// <summary>
	/// Raises the notification for a failed market-data unsubscription.
	/// </summary>
	/// <param name="origin">The originating market-data unsubscription request.</param>
	/// <param name="reply">The subscription response carrying the error.</param>
	/// <param name="subscription">The affected subscription.</param>
	void RaiseMarketDataUnSubscriptionFailed(MarketDataMessage origin, SubscriptionResponseMessage reply, Subscription subscription);

	/// <summary>
	/// Raises the notification indicating that a market-data subscription has finished.
	/// </summary>
	/// <param name="message">The subscription finished message.</param>
	/// <param name="subscription">The affected subscription.</param>
	void RaiseMarketDataSubscriptionFinished(SubscriptionFinishedMessage message, Subscription subscription);

	/// <summary>
	/// Raises the notification for an unexpectedly cancelled market-data subscription.
	/// </summary>
	/// <param name="message">The market-data subscription request.</param>
	/// <param name="error">The error that caused the cancellation.</param>
	/// <param name="subscription">The affected subscription.</param>
	void RaiseMarketDataUnexpectedCancelled(MarketDataMessage message, Exception error, Subscription subscription);

	/// <summary>
	/// Raises the notification indicating that a market-data subscription has switched to the online state.
	/// </summary>
	/// <param name="subscription">The affected subscription.</param>
	void RaiseMarketDataSubscriptionOnline(Subscription subscription);

	// Subscription lifecycle events.

	/// <summary>
	/// Raises the subscription online notification.
	/// </summary>
	/// <param name="subscription">The subscription that switched to the online state.</param>
	void RaiseSubscriptionOnline(Subscription subscription);

	/// <summary>
	/// Raises the subscription started notification.
	/// </summary>
	/// <param name="subscription">The subscription that started.</param>
	void RaiseSubscriptionStarted(Subscription subscription);

	/// <summary>
	/// Raises the subscription stopped notification.
	/// </summary>
	/// <param name="subscription">The subscription that stopped.</param>
	/// <param name="error">The error that stopped the subscription, or <see langword="null"/> for a normal stop.</param>
	void RaiseSubscriptionStopped(Subscription subscription, Exception error);

	// Received (generic) events and the message pump.

	/// <summary>
	/// Raises the new-message notifications for the specified outgoing message, invoking both the synchronous and asynchronous message hooks.
	/// </summary>
	/// <param name="message">The outgoing message.</param>
	/// <param name="cancellationToken">The token used to cancel the asynchronous notification.</param>
	/// <returns>A <see cref="ValueTask"/> that completes when the asynchronous message hook has finished.</returns>
	ValueTask RaiseNewMessage(Message message, CancellationToken cancellationToken);

	/// <summary>
	/// Raises the received notification for an entity across every subscription referenced by the message.
	/// </summary>
	/// <typeparam name="TEntity">The type of the received entity.</typeparam>
	/// <param name="entity">The received entity.</param>
	/// <param name="message">The message carrying the subscription identifiers.</param>
	/// <param name="evt">The callback invoked for each matching subscription together with the entity.</param>
	/// <returns><see langword="true"/> if at least one matching subscription is online; <see langword="false"/> if matching subscriptions exist but none are online; <see langword="null"/> when there are no matching subscriptions.</returns>
	bool? RaiseReceived<TEntity>(TEntity entity, ISubscriptionIdMessage message, Action<Subscription, TEntity> evt);

	/// <summary>
	/// Raises the received notification for an entity across every subscription referenced by the message, additionally reporting whether any subscription can transition online.
	/// </summary>
	/// <typeparam name="TEntity">The type of the received entity.</typeparam>
	/// <param name="entity">The received entity.</param>
	/// <param name="message">The message carrying the subscription identifiers.</param>
	/// <param name="evt">The callback invoked for each matching subscription together with the entity.</param>
	/// <param name="anyCanOnline">When this method returns, indicates whether at least one matching subscription is active and eligible to transition online; <see langword="null"/> when there are no matching subscriptions.</param>
	/// <returns><see langword="true"/> if at least one matching subscription is online; <see langword="false"/> if matching subscriptions exist but none are online; <see langword="null"/> when there are no matching subscriptions.</returns>
	bool? RaiseReceived<TEntity>(TEntity entity, ISubscriptionIdMessage message, Action<Subscription, TEntity> evt, out bool? anyCanOnline);

	/// <summary>
	/// Raises the received notification for an entity across the specified subscriptions.
	/// </summary>
	/// <typeparam name="TEntity">The type of the received entity.</typeparam>
	/// <param name="entity">The received entity.</param>
	/// <param name="subscriptions">The subscriptions to notify.</param>
	/// <param name="evt">The callback invoked for each subscription together with the entity.</param>
	void RaiseReceived<TEntity>(TEntity entity, IEnumerable<Subscription> subscriptions, Action<Subscription, TEntity> evt);

	/// <summary>
	/// Raises the received notification for an entity across the specified subscriptions, additionally reporting whether any subscription can transition online.
	/// </summary>
	/// <typeparam name="TEntity">The type of the received entity.</typeparam>
	/// <param name="entity">The received entity.</param>
	/// <param name="subscriptions">The subscriptions to notify.</param>
	/// <param name="evt">The callback invoked for each subscription together with the entity.</param>
	/// <param name="anyCanOnline">When this method returns, indicates whether at least one subscription is active and eligible to transition online; <see langword="null"/> when there are no subscriptions.</param>
	/// <returns><see langword="true"/> if at least one subscription is online; <see langword="false"/> if subscriptions exist but none are online; <see langword="null"/> when there are no subscriptions.</returns>
	bool? RaiseReceived<TEntity>(TEntity entity, IEnumerable<Subscription> subscriptions, Action<Subscription, TEntity> evt, out bool? anyCanOnline);

	/// <summary>
	/// Raises the generic subscription-received notification.
	/// </summary>
	/// <param name="subscription">The subscription associated with the received payload.</param>
	/// <param name="arg">The received payload.</param>
	void RaiseSubscriptionReceived(Subscription subscription, object arg);

	/// <summary>
	/// Raises the level1 received notification.
	/// </summary>
	/// <param name="subscription">The subscription associated with the received message.</param>
	/// <param name="message">The received level1 change message.</param>
	void RaiseLevel1Received(Subscription subscription, Level1ChangeMessage message);
}
