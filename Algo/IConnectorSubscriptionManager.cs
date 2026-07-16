namespace StockSharp.Algo;

/// <summary>
/// Abstraction over connector subscription management: the subscribe/unsubscribe lifecycle,
/// connection-driven subscription restore, response/finished/online message processing and
/// candle aggregation. Implemented by <see cref="ConnectorSubscriptionManager"/> and composed
/// by the <see cref="Connector"/> façade as its subscription-management seam.
/// </summary>
public interface IConnectorSubscriptionManager
{
	/// <summary>
	/// Transaction id generator.
	/// </summary>
	IdGenerator TransactionIdGenerator { get; set; }

	/// <summary>
	/// Indicates whether to send unsubscribe requests while disconnected.
	/// </summary>
	bool SendUnsubscribeWhenDisconnected { get; }

	/// <summary>
	/// Current connection state.
	/// </summary>
	ConnectionStates ConnectionState { get; set; }

	/// <summary>
	/// Restore subscription on reconnect.
	/// </summary>
	/// <remarks>
	/// Normal case connect/disconnect.
	/// </remarks>
	bool IsRestoreSubscriptionOnNormalReconnect { get; set; }

	/// <summary>
	/// Send subscriptions on connect.
	/// </summary>
	CachedSynchronizedSet<Subscription> SubscriptionsOnConnect { get; }

	/// <summary>
	/// Current subscriptions snapshot.
	/// </summary>
	IEnumerable<Subscription> Subscriptions { get; }

	/// <summary>
	/// Clear internal state.
	/// </summary>
	void ClearCache();

	/// <summary>
	/// Get securities that have active subscriptions for the specified data type.
	/// </summary>
	/// <param name="dataType">Data type.</param>
	/// <returns>Securities with active subscriptions.</returns>
	IEnumerable<SecurityId> GetSubscribers(DataType dataType);

	/// <summary>
	/// Resolve subscriptions for the specified message.
	/// </summary>
	/// <param name="message">Message with subscription ids.</param>
	/// <returns>Subscriptions.</returns>
	IEnumerable<Subscription> GetSubscriptions(ISubscriptionIdMessage message);

	/// <summary>
	/// Try get subscription by id.
	/// </summary>
	/// <param name="id">Subscription id.</param>
	/// <param name="ignoreAll">Ignore "all securities" mapping.</param>
	/// <param name="remove">Remove from cache.</param>
	/// <param name="time">Optional server time.</param>
	/// <returns>Subscription or <see langword="null"/>.</returns>
	Subscription TryGetSubscription(long id, bool ignoreAll, bool remove, DateTime? time);

	/// <summary>
	/// Process a subscription response message.
	/// </summary>
	/// <param name="response">Response message.</param>
	/// <param name="originalMsg">Original subscription request.</param>
	/// <param name="unexpectedCancelled">Indicates that active subscription was canceled due to error.</param>
	/// <param name="items">Collected lookup items.</param>
	/// <returns>Subscription instance.</returns>
	Subscription ProcessResponse(SubscriptionResponseMessage response, out ISubscriptionMessage originalMsg, out bool unexpectedCancelled, out object[] items);

	/// <summary>
	/// Send a subscription request.
	/// </summary>
	/// <param name="subscription">Subscription.</param>
	/// <param name="isAllExtension">Indicates "all securities" extension.</param>
	/// <returns>Actions to apply.</returns>
	ConnectorSubscriptionManager.Actions Subscribe(Subscription subscription, bool isAllExtension = false);

	/// <summary>
	/// Send an unsubscribe request.
	/// </summary>
	/// <param name="subscription">Subscription.</param>
	/// <returns>Actions to apply.</returns>
	ConnectorSubscriptionManager.Actions UnSubscribe(Subscription subscription);

	/// <summary>
	/// Handle connection event and restore subscriptions as needed.
	/// </summary>
	/// <param name="subscriptionFilter">Filter for <see cref="SubscriptionsOnConnect"/>.</param>
	/// <returns>Actions to apply.</returns>
	ConnectorSubscriptionManager.Actions HandleConnected(Func<Subscription, bool> subscriptionFilter);

	/// <summary>
	/// Handle connection event and restore subscriptions as needed.
	/// </summary>
	/// <param name="subscriptions">Default subscriptions.</param>
	/// <returns>Actions to apply.</returns>
	ConnectorSubscriptionManager.Actions HandleConnected(Subscription[] subscriptions);

	/// <summary>
	/// Unsubscribe all active subscriptions.
	/// </summary>
	/// <returns>Actions to apply.</returns>
	ConnectorSubscriptionManager.Actions UnSubscribeAll();

	/// <summary>
	/// Process a lookup response item.
	/// </summary>
	/// <typeparam name="T">Item type.</typeparam>
	/// <param name="message">Lookup message.</param>
	/// <param name="item">Lookup item.</param>
	/// <returns>Subscriptions that received the item.</returns>
	IEnumerable<Subscription> ProcessLookupResponse<T>(ISubscriptionIdMessage message, T item);

	/// <summary>
	/// Process a subscription finished message.
	/// </summary>
	/// <param name="message">Finished message.</param>
	/// <param name="items">Collected lookup items.</param>
	/// <returns>Subscription instance.</returns>
	Subscription ProcessSubscriptionFinishedMessage(SubscriptionFinishedMessage message, out object[] items);

	/// <summary>
	/// Process a subscription online message.
	/// </summary>
	/// <param name="message">Online message.</param>
	/// <param name="items">Collected lookup items.</param>
	/// <returns>Subscription instance.</returns>
	Subscription ProcessSubscriptionOnlineMessage(SubscriptionOnlineMessage message, out object[] items);

	/// <summary>
	/// Update candles for subscriptions and return updated results.
	/// </summary>
	/// <param name="message">Candle message.</param>
	/// <returns>Updated subscriptions with candles.</returns>
	IEnumerable<(Subscription subscription, ICandleMessage candle)> UpdateCandles(CandleMessage message);
}
