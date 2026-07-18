namespace StockSharp.Tests;

/// <summary>
/// Interface-level coverage for <see cref="IConnectorSubscriptionManager"/>, the subscription-management
/// seam extracted from the <see cref="Connector"/> god object.
/// </summary>
/// <remarks>
/// Every test drives the manager through the <see cref="IConnectorSubscriptionManager"/> abstraction rather
/// than the concrete <see cref="ConnectorSubscriptionManager"/> type, so the extracted contract itself is
/// exercised. Behavioral parity with the concrete implementation is guaranteed elsewhere; this class
/// deliberately emphasizes the interface members that <c>SubscriptionManagerConnectorTests</c> does not
/// already cover (connection/restore properties, <see cref="IConnectorSubscriptionManager.UnSubscribeAll"/>,
/// both <c>HandleConnected</c> overloads, <see cref="IConnectorSubscriptionManager.ProcessLookupResponse{T}"/>
/// and <see cref="IConnectorSubscriptionManager.UpdateCandles"/>), while smoke-testing the shared members
/// once through the abstraction.
/// </remarks>
[TestClass]
public class SubscriptionManagerInterfaceTests : BaseTestClass
{
	private sealed class TestReceiver : TestLogReceiver { }

	#region Helpers

	/// <summary>
	/// Fixed UTC timestamp used by the test helpers so message construction is fully deterministic and does
	/// not depend on wall-clock time.
	/// </summary>
	private static readonly DateTime _fixedTime = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	/// <summary>
	/// Create a manager but expose it as the <see cref="IConnectorSubscriptionManager"/> abstraction so that
	/// every subsequent call in a test goes through the interface, not the concrete type.
	/// </summary>
	private static IConnectorSubscriptionManager CreateManager(bool sendUnsubscribeWhenDisconnected = true)
		=> new ConnectorSubscriptionManager(new TestReceiver(), new IncrementalIdGenerator(), sendUnsubscribeWhenDisconnected);

	private static Subscription CreateTickSubscription()
		=> new(new MarketDataMessage
		{
			IsSubscribe = true,
			SecurityId = Helper.CreateSecurityId(),
			DataType2 = DataType.Ticks,
		});

	private static Subscription CreateCandleSubscription()
		=> new(new MarketDataMessage
		{
			IsSubscribe = true,
			SecurityId = Helper.CreateSecurityId(),
			DataType2 = TimeSpan.FromMinutes(1).TimeFrame(),
		});

	private static Subscription CreateOrderStatusSubscription()
		=> new(new OrderStatusMessage { IsSubscribe = true });

	/// <summary>
	/// A security-lookup subscription resolves to <see cref="DataType.Securities"/>, which causes the manager
	/// to allocate a lookup-item buffer, so it is a valid target for <see cref="IConnectorSubscriptionManager.ProcessLookupResponse{T}"/>.
	/// </summary>
	private static Subscription CreateLookupSubscription()
		=> new(new SecurityLookupMessage());

	/// <summary>
	/// Subscribe and process a success response so the subscription becomes <see cref="SubscriptionStates.Active"/>.
	/// </summary>
	private static long SubscribeAndActivate(IConnectorSubscriptionManager manager, Subscription subscription)
	{
		manager.Subscribe(subscription);
		var transId = subscription.TransactionId;

		manager.ProcessResponse(
			new SubscriptionResponseMessage { OriginalTransactionId = transId },
			out _, out _, out _);

		return transId;
	}

	/// <summary>
	/// Build a data message carrying the specified subscription ids (mirrors the pattern file helper).
	/// </summary>
	private static ISubscriptionIdMessage CreateDataMessage(params long[] subscriptionIds)
	{
		var msg = new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			ServerTime = _fixedTime,
		};
		msg.SetSubscriptionIds(subscriptionIds);
		return msg;
	}

	#endregion

	#region Property / contract surface

	[TestMethod]
	public void SendUnsubscribeWhenDisconnected_ReflectsCtorFlag()
	{
		var enabled = CreateManager(sendUnsubscribeWhenDisconnected: true);
		enabled.SendUnsubscribeWhenDisconnected.AssertTrue();

		var disabled = CreateManager(sendUnsubscribeWhenDisconnected: false);
		disabled.SendUnsubscribeWhenDisconnected.AssertFalse();
	}

	[TestMethod]
	public void ConnectionState_DefaultDisconnected_AndSettable()
	{
		var manager = CreateManager();

		manager.ConnectionState.AssertEqual(ConnectionStates.Disconnected);

		manager.ConnectionState = ConnectionStates.Connected;
		manager.ConnectionState.AssertEqual(ConnectionStates.Connected);
	}

	[TestMethod]
	public void IsRestoreSubscriptionOnNormalReconnect_DefaultTrue_AndSettable()
	{
		var manager = CreateManager();

		manager.IsRestoreSubscriptionOnNormalReconnect.AssertTrue();

		manager.IsRestoreSubscriptionOnNormalReconnect = false;
		manager.IsRestoreSubscriptionOnNormalReconnect.AssertFalse();
	}

	[TestMethod]
	public void TransactionIdGenerator_ReturnsInjected_AndSettable()
	{
		var generator = new IncrementalIdGenerator();
		IConnectorSubscriptionManager manager = new ConnectorSubscriptionManager(new TestReceiver(), generator, true);

		ReferenceEquals(manager.TransactionIdGenerator, generator).AssertTrue("Getter must return the injected generator");

		var replacement = new IncrementalIdGenerator();
		manager.TransactionIdGenerator = replacement;
		ReferenceEquals(manager.TransactionIdGenerator, replacement).AssertTrue("Setter must store the new generator");
	}

	[TestMethod]
	public void FreshManager_HasNoSubscriptions()
	{
		var manager = CreateManager();

		manager.Subscriptions.Count().AssertEqual(0, "Fresh manager must have no active subscriptions");
		manager.SubscriptionsOnConnect.Count.AssertEqual(0, "Fresh manager must have no on-connect subscriptions");
	}

	#endregion

	#region Uncovered interface methods

	[TestMethod]
	public void Subscribe_ThroughInterface_AssignsTransactionId_AndSendsRequest()
	{
		var manager = CreateManager();
		var subscription = CreateTickSubscription();

		var actions = manager.Subscribe(subscription);

		subscription.TransactionId.AssertNotEqual(0);
		actions.Items.Length.AssertEqual(1);
		actions.Items[0].Type.AssertEqual(ConnectorSubscriptionManager.Actions.Item.Types.SendInMessage);

		var sent = (MarketDataMessage)actions.Items[0].Message;
		sent.TransactionId.AssertEqual(subscription.TransactionId);
	}

	[TestMethod]
	public void Subscribe_OrderStatus_ThroughInterface_ProducesAddOrderStatusAction()
	{
		var manager = CreateManager();
		var subscription = CreateOrderStatusSubscription();

		var actions = manager.Subscribe(subscription);

		actions.Items.Count(i => i.Type == ConnectorSubscriptionManager.Actions.Item.Types.SendInMessage)
			.AssertEqual(1, "Order-status subscribe should still send the request");
		actions.Items.Count(i => i.Type == ConnectorSubscriptionManager.Actions.Item.Types.AddOrderStatus)
			.AssertEqual(1, "Order-status subscribe should register the order-status transaction id");
	}

	/// <summary>
	/// The single-subscription <see cref="IConnectorSubscriptionManager.UnSubscribe(Subscription)"/> overload,
	/// driven through the interface, must emit exactly one outbound unsubscribe request that references the
	/// original subscribe transaction id and carries a fresh transaction id of its own.
	/// </summary>
	[TestMethod]
	public void UnSubscribe_ThroughInterface_SendsUnsubscribeRequest()
	{
		var manager = CreateManager();
		var subscription = CreateTickSubscription();

		var subscribeId = SubscribeAndActivate(manager, subscription);

		var actions = manager.UnSubscribe(subscription);

		var sendItems = actions.Items
			.Where(i => i.Type == ConnectorSubscriptionManager.Actions.Item.Types.SendInMessage)
			.ToArray();
		sendItems.Length.AssertEqual(1, "UnSubscribe should send exactly one unsubscribe request");

		var sent = (MarketDataMessage)sendItems[0].Message;
		sent.IsSubscribe.AssertFalse("Unsubscribe request must have IsSubscribe == false");
		sent.OriginalTransactionId.AssertEqual(subscribeId,
			"Unsubscribe request must reference the original subscribe transaction id");
		sent.TransactionId.AssertNotEqual(0, "Unsubscribe request must carry a fresh transaction id");
		sent.TransactionId.AssertNotEqual(subscribeId,
			"Unsubscribe request must use a new transaction id, not the subscribe id");
	}

	/// <summary>
	/// When <see cref="IConnectorSubscriptionManager.SendUnsubscribeWhenDisconnected"/> is <see langword="false"/>
	/// and the manager is disconnected, unsubscribing through the interface must remove the subscription locally
	/// without emitting any outbound request.
	/// </summary>
	[TestMethod]
	public void UnSubscribe_ThroughInterface_WhenDisconnected_RemovesLocallyWithoutSending()
	{
		var manager = CreateManager(sendUnsubscribeWhenDisconnected: false);
		manager.ConnectionState = ConnectionStates.Disconnected;

		var subscription = CreateTickSubscription();
		var transId = SubscribeAndActivate(manager, subscription);

		var actions = manager.UnSubscribe(subscription);

		actions.Items.Count(i => i.Type == ConnectorSubscriptionManager.Actions.Item.Types.SendInMessage)
			.AssertEqual(0, "Disconnected unsubscribe with SendUnsubscribeWhenDisconnected=false must not send a request");

		manager.Subscriptions.Count(s => s.TransactionId == transId)
			.AssertEqual(0, "Subscription should be removed locally when unsubscribing while disconnected");
	}

	/// <summary>
	/// <see cref="IConnectorSubscriptionManager.UnSubscribe(Subscription)"/> must reject a <see langword="null"/> subscription.
	/// </summary>
	[TestMethod]
	public void UnSubscribe_Null_Throws()
	{
		var manager = CreateManager();

		ThrowsExactly<ArgumentNullException>(() => manager.UnSubscribe(null));
	}

	[TestMethod]
	public void UnSubscribeAll_UnsubscribesEveryActiveSubscription()
	{
		var manager = CreateManager();

		var first = CreateTickSubscription();
		var second = CreateTickSubscription();

		SubscribeAndActivate(manager, first);
		SubscribeAndActivate(manager, second);

		var activeCount = manager.Subscriptions.Count(s => s.State == SubscriptionStates.Active);
		activeCount.AssertEqual(2);

		var actions = manager.UnSubscribeAll();

		actions.Items.Count(i => i.Type == ConnectorSubscriptionManager.Actions.Item.Types.SendInMessage)
			.AssertEqual(activeCount, "UnSubscribeAll should send one unsubscribe request per active subscription");
	}

	[TestMethod]
	public void HandleConnected_Array_FirstConnect_SubscribesDefaults()
	{
		var manager = CreateManager();
		var subscription = CreateTickSubscription();

		var actions = manager.HandleConnected([subscription]);

		actions.Items.Count(i => i.Type == ConnectorSubscriptionManager.Actions.Item.Types.SendInMessage)
			.AssertEqual(1, "First connect should subscribe the provided default subscription");

		var sent = (MarketDataMessage)actions.Items
			.First(i => i.Type == ConnectorSubscriptionManager.Actions.Item.Types.SendInMessage).Message;
		sent.TransactionId.AssertEqual(subscription.TransactionId);
	}

	[TestMethod]
	public void HandleConnected_Array_Reconnect_WithRestoreDisabled_ReturnsEmpty()
	{
		var manager = CreateManager();
		manager.IsRestoreSubscriptionOnNormalReconnect = false;

		// First connect flips the internal "was connected" flag without producing actions.
		manager.HandleConnected([]);

		// A subsequent reconnect with restore disabled must produce no actions at all.
		var actions = manager.HandleConnected([CreateTickSubscription()]);

		actions.Items.Length.AssertEqual(0, "Reconnect with restore disabled must be a no-op");
	}

	[TestMethod]
	public void HandleConnected_Filter_SubscribesMatchingSubscriptionsOnConnect()
	{
		var manager = CreateManager();
		var subscription = CreateTickSubscription();

		manager.SubscriptionsOnConnect.Add(subscription);

		var actions = manager.HandleConnected(s => ReferenceEquals(s, subscription));

		actions.Items.Count(i => i.Type == ConnectorSubscriptionManager.Actions.Item.Types.SendInMessage)
			.AssertEqual(1, "Filter overload should subscribe the matching SubscriptionsOnConnect entry");
	}

	[TestMethod]
	public void HandleConnected_Filter_Null_Throws()
	{
		var manager = CreateManager();

		ThrowsExactly<ArgumentNullException>(() => manager.HandleConnected((Func<Subscription, bool>)null));
	}

	[TestMethod]
	public void HandleConnected_Array_Null_Throws()
	{
		var manager = CreateManager();

		ThrowsExactly<ArgumentNullException>(() => manager.HandleConnected((Subscription[])null));
	}

	[TestMethod]
	public void ProcessLookupResponse_CollectsItem_ForLookupSubscription()
	{
		var manager = CreateManager();
		var subscription = CreateLookupSubscription();

		manager.Subscribe(subscription);
		var transId = subscription.TransactionId;

		var carrier = CreateDataMessage(transId);
		var securityId = Helper.CreateSecurityId();
		var item = new SecurityMessage { SecurityId = securityId };

		var affected = manager.ProcessLookupResponse(carrier, item).ToArray();

		affected.Any(s => s.TransactionId == transId)
			.AssertTrue("Lookup subscription should receive the response item");

		// Complete the lookup flow: finishing the subscription drains the buffered items, letting us assert
		// the buffered payload's identity and content rather than merely that the subscription resolved.
		var finished = manager.ProcessSubscriptionFinishedMessage(
			new SubscriptionFinishedMessage { OriginalTransactionId = transId }, out var items);

		finished.AssertSame(subscription);
		items.AssertNotNull();
		items.Length.AssertEqual(1, "Exactly the one buffered lookup item should be drained on finish");
		items[0].AssertSame(item);
		((SecurityMessage)items[0]).SecurityId.AssertEqual(securityId, "Drained item must carry the original security id");
	}

	[TestMethod]
	public void ProcessLookupResponse_UnknownId_ReturnsEmpty()
	{
		var manager = CreateManager();

		var carrier = CreateDataMessage(4242);
		var affected = manager.ProcessLookupResponse(carrier, new SecurityMessage()).ToArray();

		affected.Length.AssertEqual(0, "Unknown subscription id must not resolve to any subscription");
	}

	[TestMethod]
	public void UpdateCandles_ReturnsSubscription_ForKnownCandle()
	{
		var manager = CreateManager();
		var subscription = CreateCandleSubscription();
		var transId = SubscribeAndActivate(manager, subscription);

		var openTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var candle = new TimeFrameCandleMessage
		{
			OpenTime = openTime,
		};
		candle.SetSubscriptionIds([transId]);

		var updated = manager.UpdateCandles(candle).ToArray();

		updated.Length.AssertEqual(1, "Exactly the one known candle subscription should be returned");

		// Assert the returned payload, not just that the subscription resolved: the tuple must carry the
		// matching subscription and the very candle instance we passed in, with its fields preserved.
		var (returnedSubscription, returnedCandle) = updated[0];
		returnedSubscription.TransactionId.AssertEqual(transId, "Returned tuple must carry the matching subscription");
		returnedCandle.AssertSame(candle);
		returnedCandle.OpenTime.AssertEqual(openTime, "Returned candle must preserve its OpenTime");
	}

	[TestMethod]
	public void UpdateCandles_UnknownId_ReturnsEmpty()
	{
		var manager = CreateManager();

		var candle = new TimeFrameCandleMessage
		{
			OpenTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
		};
		candle.SetSubscriptionIds([9999]);

		manager.UpdateCandles(candle).Count().AssertEqual(0, "Unknown candle subscription id must yield no updates");
	}

	#endregion

	#region ProcessResponse error / unexpected-cancel branch

	/// <summary>
	/// The failure branch of <see cref="IConnectorSubscriptionManager.ProcessResponse"/>: when an
	/// <em>already-active</em> subscription receives a <see cref="SubscriptionResponseMessage"/> carrying an
	/// <see cref="SubscriptionResponseMessage.Error"/>, the manager must surface the original subscribe request
	/// through <c>originalMsg</c>, report the cancellation as <em>unexpected</em> (because the subscription was
	/// active), return an empty <c>items</c> buffer (a ticks subscription buffers no lookup items) and move the
	/// subscription into <see cref="SubscriptionStates.Error"/>, removing it from the active set. This is the
	/// negative counterpart to the success path exercised by every other test via <c>SubscribeAndActivate</c>.
	/// </summary>
	[TestMethod]
	public void ProcessResponse_ErrorAfterActivation_ReportsUnexpectedCancelledWithOriginalMessage()
	{
		var manager = CreateManager();
		var subscription = CreateTickSubscription();

		var transId = SubscribeAndActivate(manager, subscription);
		subscription.State.AssertEqual(SubscriptionStates.Active, "Pre-condition: subscription must be active before the error");

		var error = new InvalidOperationException("subscription failed");

		var returned = manager.ProcessResponse(
			new SubscriptionResponseMessage { OriginalTransactionId = transId, Error = error },
			out var originalMsg, out var unexpectedCancelled, out var items);

		// The active subscription is the one returned.
		returned.AssertSame(subscription);

		// The original subscribe request is surfaced for the caller.
		originalMsg.AssertNotNull();
		originalMsg.TransactionId.AssertEqual(transId, "originalMsg must be the original subscribe request");
		originalMsg.IsSubscribe.AssertTrue("originalMsg must be a subscribe request");

		// Because the subscription was already active, its cancellation is unexpected.
		unexpectedCancelled.AssertTrue("An active subscription cancelled by error is an unexpected cancellation");

		// A ticks subscription buffers no lookup items, so the drained buffer is empty (never null).
		items.AssertNotNull();
		items.Length.AssertEqual(0, "A non-lookup subscription must drain an empty item buffer");

		// The subscription transitions to the error state and is removed from the active set.
		subscription.State.AssertEqual(SubscriptionStates.Error);
		manager.Subscriptions.Count(s => s.TransactionId == transId)
			.AssertEqual(0, "An errored subscription must be removed from the active set");
	}

	/// <summary>
	/// The failure branch when the subscription has <em>not yet been activated</em>: an error response for a
	/// freshly-subscribed (still <see cref="SubscriptionStates.Stopped"/>) subscription must still surface the
	/// original request and return the subscription, but the cancellation is <em>not</em> unexpected because the
	/// subscription was never active. This distinguishes the <c>unexpectedCancelled</c> flag's two outcomes.
	/// </summary>
	[TestMethod]
	public void ProcessResponse_ErrorBeforeActivation_IsNotUnexpectedCancelled()
	{
		var manager = CreateManager();
		var subscription = CreateTickSubscription();

		manager.Subscribe(subscription);
		var transId = subscription.TransactionId;
		subscription.State.AssertNotEqual(SubscriptionStates.Active, "Pre-condition: subscription must not be active yet");

		var error = new InvalidOperationException("rejected before activation");

		var returned = manager.ProcessResponse(
			new SubscriptionResponseMessage { OriginalTransactionId = transId, Error = error },
			out var originalMsg, out var unexpectedCancelled, out var items);

		returned.AssertSame(subscription);
		originalMsg.AssertNotNull();
		originalMsg.TransactionId.AssertEqual(transId);

		// Never active, so cancelling it is expected — the flag must be false.
		unexpectedCancelled.AssertFalse("A never-active subscription cancelled by error is not an unexpected cancellation");

		items.AssertNotNull();
		items.Length.AssertEqual(0);
		subscription.State.AssertEqual(SubscriptionStates.Error);
	}

	/// <summary>
	/// When the response references an unknown original transaction id, <c>ProcessResponse</c> must return
	/// <see langword="null"/> with a <see langword="null"/> <c>originalMsg</c>, a <see langword="false"/>
	/// <c>unexpectedCancelled</c> and an empty (never <see langword="null"/>) <c>items</c> buffer — the safe
	/// no-match contract that the connector relies on to raise a bare error event without touching subscriptions.
	/// </summary>
	[TestMethod]
	public void ProcessResponse_UnknownOriginalId_ReturnsNullWithEmptyOutParams()
	{
		var manager = CreateManager();

		var returned = manager.ProcessResponse(
			new SubscriptionResponseMessage { OriginalTransactionId = 987654321, Error = new InvalidOperationException("boom") },
			out var originalMsg, out var unexpectedCancelled, out var items);

		IsNull(returned);
		IsNull(originalMsg);
		unexpectedCancelled.AssertFalse();
		items.AssertNotNull();
		items.Length.AssertEqual(0);
	}

	#endregion

	#region Interface pass-through smoke tests

	[TestMethod]
	public void Lifecycle_Active_Online_Finished_ThroughInterface()
	{
		var manager = CreateManager();
		var subscription = CreateTickSubscription();

		var transId = SubscribeAndActivate(manager, subscription);
		subscription.State.AssertEqual(SubscriptionStates.Active);

		var online = manager.ProcessSubscriptionOnlineMessage(
			new SubscriptionOnlineMessage { OriginalTransactionId = transId }, out _);
		IsNotNull(online);
		subscription.State.AssertEqual(SubscriptionStates.Online);

		var finished = manager.ProcessSubscriptionFinishedMessage(
			new SubscriptionFinishedMessage { OriginalTransactionId = transId }, out _);
		IsNotNull(finished);
		subscription.State.AssertEqual(SubscriptionStates.Finished);

		manager.Subscriptions.Count(s => s.TransactionId == transId)
			.AssertEqual(0, "Finished subscription should be removed");
	}

	[TestMethod]
	public void Interface_Exposes_Query_And_Cache_Members()
	{
		var manager = CreateManager();
		var subscription = CreateTickSubscription();
		var transId = SubscribeAndActivate(manager, subscription);

		// GetSubscriptions resolves ids through the interface.
		manager.GetSubscriptions(CreateDataMessage(transId))
			.Count(s => s.TransactionId == transId)
			.AssertEqual(1);

		// TryGetSubscription returns the live subscription.
		var found = manager.TryGetSubscription(transId, ignoreAll: false, remove: false, time: null);
		IsNotNull(found);
		found.TransactionId.AssertEqual(transId);

		// GetSubscribers exposes the active subscription's security.
		manager.GetSubscribers(DataType.Ticks)
			.Contains(subscription.SecurityId.Value)
			.AssertTrue("Active tick subscription's security should be reported");

		// ClearCache empties the manager.
		manager.ClearCache();
		manager.Subscriptions.Count().AssertEqual(0, "ClearCache should remove all subscriptions");
	}

	#endregion
}
