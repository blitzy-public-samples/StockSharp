namespace StockSharp.Tests;

/// <summary>
/// Direct component tests for the extracted <see cref="ConnectorMessageProcessor"/> and its
/// abstraction <see cref="IConnectorMessageProcessor"/>. The processor hosts the inbound-message
/// handler bodies that previously lived as private <c>Process*Message</c> methods inside
/// <c>Connector_ProcessMessage.cs</c>; the <see cref="Connector"/> façade keeps the
/// <c>OnProcessMessage</c> dispatch switch and delegates each case to the processor.
///
/// Two complementary, deliberately separated strategies are used:
///  * Strategy 1 — individual handlers are invoked through the <see cref="IConnectorMessageProcessor"/>
///    surface on a processor built over a live façade, asserting the observable effect on the façade.
///  * Strategy 2 — the façade is driven so that its <em>internal</em> processor runs the real
///    <c>OnProcessMessage</c> switch, asserting that the dispatch order and resulting event sequence
///    are preserved exactly after the decomposition (the top-priority behavioral-parity guard).
///
/// The two strategies are never mixed inside a single assertion: a test either invokes a handler on
/// its own processor instance (Strategy 1) or feeds the façade via <see cref="Connector.SendOutMessageAsync"/>
/// (Strategy 2), so no message is double-processed.
///
/// Every façade created by a test is registered for deterministic disposal in <see cref="DisposeConnectors"/>
/// so the dual In/Out <c>InMemoryMessageChannel</c> workers and timers are always torn down (resource hygiene).
/// </summary>
[TestClass]
public class ConnectorMessageProcessorTests : BaseTestClass
{
	// -----------------------------------------------------------------------------------------
	// Reference contract — the EXACT OnProcessMessage dispatch order the façade must preserve.
	// Every processed message first runs the preamble (which fires the per-message "new message"
	// event via RaiseNewMessage) and only then the type-specific handler below:
	//   1.  Connect              -> ProcessConnectMessage              (async)
	//   2.  Disconnect           -> ProcessDisconnectMessage           (sync)
	//   3.  ConnectionLost       -> ProcessConnectionLostMessage       (sync)
	//   4.  ConnectionRestored   -> ProcessConnectionRestoredMessage   (sync)
	//   5.  QuoteChange          -> ProcessQuotesMessage               (async)
	//   6.  Board                -> ProcessBoardMessage                (sync)
	//   7.  BoardState           -> ProcessBoardStateMessage           (sync)
	//   8.  Security             -> ProcessSecurityMessage             (async)
	//   9.  DataTypeInfo         -> ProcessDataTypeInfoMessage         (sync)
	//   10. Level1Change         -> ProcessLevel1ChangeMessage         (async)
	//   11. News                 -> ProcessNewsMessage                 (async)
	//   12. Execution            -> ProcessExecutionMessage            (async)
	//   13. Portfolio            -> ProcessPortfolioMessage            (sync)
	//   14. PositionChange       -> ProcessPositionChangeMessage       (async)
	//   15. SubscriptionResponse -> ProcessSubscriptionResponseMessage (sync)
	//   16. SubscriptionFinished -> ProcessSubscriptionFinishedMessage (async)
	//   17. SubscriptionOnline   -> ProcessSubscriptionOnlineMessage   (sync)
	//   18. Error                -> ProcessErrorMessage                (sync)
	//   19. RemoveSecurity (ext) -> ProcessSecurityRemoveMessage       (async)
	//   20. ChangePassword       -> ProcessChangePasswordMessage       (sync)
	//   21. default              -> CandleMessage          ? ProcessCandleMessage
	//                               ISubscriptionIdMessage ? ProcessSubscriptionMessage
	// -----------------------------------------------------------------------------------------

	#region Mock adapter

	/// <summary>
	/// Minimal inbound adapter modelled on <c>ConnectorRoutingTests.LiveFeedCryptoAdapter</c> that
	/// echoes the connection lifecycle: it replies to <see cref="MessageTypes.Reset"/>,
	/// <see cref="MessageTypes.Connect"/> and <see cref="MessageTypes.Disconnect"/> with the matching
	/// out messages so that a real connect/disconnect drives the façade's internal processor. When not
	/// attached to a connector it also doubles as a convenient "foreign" adapter instance whose identity
	/// differs from <see cref="Connector.Adapter"/>, exercising the <c>*Ex</c> event branches.
	/// </summary>
	private sealed class ConnectDisconnectAdapter : MessageAdapter
	{
		public ConnectDisconnectAdapter(IdGenerator transactionIdGenerator)
			: base(transactionIdGenerator)
		{
			this.AddMarketDataSupport();
			this.AddTransactionalSupport();
		}

		/// <inheritdoc />
		protected override async ValueTask OnSendInMessageAsync(Message message, CancellationToken cancellationToken)
		{
			switch (message.Type)
			{
				case MessageTypes.Reset:
					await SendOutMessageAsync(new ResetMessage(), cancellationToken);
					break;

				case MessageTypes.Connect:
					await SendOutMessageAsync(new ConnectMessage(), cancellationToken);
					break;

				case MessageTypes.Disconnect:
					await SendOutMessageAsync(new DisconnectMessage(), cancellationToken);
					break;
			}
		}

		/// <inheritdoc />
		public override IMessageAdapter Clone()
			=> new ConnectDisconnectAdapter(TransactionIdGenerator);
	}

	#endregion

	#region Fixture & lifecycle

	/// <summary>
	/// Every façade a test creates is tracked here and disposed by <see cref="DisposeConnectors"/>.
	/// </summary>
	private readonly List<Connector> _connectors = [];

	/// <summary>
	/// Creates a fresh <see cref="Connector"/> façade and registers it for deterministic disposal.
	/// The connect-time lookup subscriptions are cleared so the connection handlers reduce to a clean
	/// event raise with no re-subscription traffic.
	/// </summary>
	private Connector NewConnector()
	{
		var connector = new Connector();
		connector.SubscriptionsOnConnect.Clear();
		_connectors.Add(connector);
		return connector;
	}

	/// <summary>
	/// Disposes every façade created during the test (Issue-4 resource hygiene), guaranteeing the dual
	/// In/Out <c>InMemoryMessageChannel</c> workers and internal timers are torn down. Every tracked façade is
	/// disposed even if an earlier one faults, and any disposal faults are collected and re-thrown as an
	/// <see cref="AggregateException"/> so a teardown, channel or resource failure stays visible instead of
	/// silently passing the test. MSTest aggregates this cleanup fault with any exception the test body already
	/// threw, so a prior assertion failure is surfaced alongside it and is never masked.
	/// </summary>
	[TestCleanup]
	public void DisposeConnectors()
	{
		List<Exception> disposeErrors = null;

		foreach (var connector in _connectors)
		{
			try
			{
				connector.Dispose();
			}
			catch (Exception ex)
			{
				// Collect rather than swallow: every façade must still be disposed, but the fault
				// must remain visible instead of silently passing the test.
				(disposeErrors ??= []).Add(ex);
			}
		}

		_connectors.Clear();

		if (disposeErrors is not null)
			throw new AggregateException("One or more tracked connectors failed to dispose during test cleanup.", disposeErrors);
	}

	/// <summary>
	/// Builds a standalone <see cref="IConnectorMessageProcessor"/> over a fresh tracked façade and
	/// exposes the focused collaborators the processor is constructed with (an <see cref="EntityCache"/>
	/// and a <see cref="ConnectorSubscriptionManager"/>). The returned <paramref name="injectedSm"/> and
	/// <paramref name="entityCache"/> are the very instances the processor delegates to, so Strategy-1
	/// tests can seed subscriptions/orders exactly where the handler looks for them. Both collaborators
	/// are built solely from the connector's public surface — the test never reaches into façade internals.
	/// </summary>
	private (Connector connector, IConnectorMessageProcessor proc, ConnectorSubscriptionManager injectedSm, EntityCache entityCache) Build()
	{
		var connector = NewConnector();
		var entityCache = new EntityCache(connector, _ => null, connector.ExchangeInfoProvider, connector);
		var injectedSm = new ConnectorSubscriptionManager(connector, connector.TransactionIdGenerator, false);
		var proc = new ConnectorMessageProcessor(connector, entityCache, injectedSm);

		return (connector, proc, injectedSm, entityCache);
	}

	/// <summary>
	/// Builds a processor over an already-created façade (kept for the original anchor tests). The
	/// freshly built subscription manager intentionally starts empty so the Connect handler reduces to a
	/// clean connected-event raise with no re-subscription side effects.
	/// </summary>
	private static IConnectorMessageProcessor CreateProcessor(Connector connector)
	{
		var entityCache = new EntityCache(connector, _ => null, connector.ExchangeInfoProvider, connector);
		var subscriptionManager = new ConnectorSubscriptionManager(connector, connector.TransactionIdGenerator, false);

		return new ConnectorMessageProcessor(connector, entityCache, subscriptionManager);
	}

	/// <summary>Builds a market-data <see cref="Subscription"/> for the given security and data type.</summary>
	private static Subscription MarketData(SecurityId secId, DataType dataType)
		=> new(new MarketDataMessage { IsSubscribe = true, SecurityId = secId, DataType2 = dataType });

	/// <summary>
	/// Subscribes and drives a success response so the subscription becomes
	/// <see cref="SubscriptionStates.Active"/> in the supplied manager, returning the transaction id.
	/// </summary>
	private static long SubscribeActivate(IConnectorSubscriptionManager sm, Subscription subscription)
	{
		sm.Subscribe(subscription);
		var id = subscription.TransactionId;

		sm.ProcessResponse(new SubscriptionResponseMessage { OriginalTransactionId = id }, out _, out _, out _);

		return id;
	}

	/// <summary>An asynchronously-completing signal used by the Strategy-2 out-pump parity tests.</summary>
	private static TaskCompletionSource<bool> Signal()
		=> new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>Awaits a Strategy-2 signal with a hard upper bound so a missed route fails fast instead of hanging.</summary>
	private static async Task ShouldSignal(TaskCompletionSource<bool> tcs)
		=> (await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10))).AssertTrue();

	#endregion

	#region Strategy 1 — connection lifecycle handlers

	/// <summary>
	/// Async surface coverage: <see cref="IConnectorMessageProcessor.ProcessConnectMessage"/> for the
	/// façade's own adapter with no error must raise the parameterless <see cref="Connector.Connected"/> event.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ProcessConnectMessage_ForOwnAdapter_RaisesConnected()
	{
		var (connector, proc, _, _) = Build();

		var connectedCount = 0;
		connector.Connected += () => connectedCount++;

		await proc.ProcessConnectMessage(new ConnectMessage { Adapter = connector.Adapter }, CancellationToken);

		connectedCount.AssertEqual(1);
	}

	/// <summary>
	/// A connect ack for a <em>foreign</em> adapter (one whose identity differs from
	/// <see cref="Connector.Adapter"/>) must raise the adapter-scoped <see cref="Connector.ConnectedEx"/>
	/// event carrying that adapter, not the parameterless <see cref="Connector.Connected"/>.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ProcessConnectMessage_ForForeignAdapter_RaisesConnectedEx()
	{
		var (connector, proc, _, _) = Build();

		var foreign = new ConnectDisconnectAdapter(connector.TransactionIdGenerator);

		var plainCount = 0;
		var exAdapters = new List<IMessageAdapter>();
		connector.Connected += () => plainCount++;
		connector.ConnectedEx += exAdapters.Add;

		await proc.ProcessConnectMessage(new ConnectMessage { Adapter = foreign }, CancellationToken);

		plainCount.AssertEqual(0);
		exAdapters.Count.AssertEqual(1);
		exAdapters[0].AssertSame(foreign);
	}

	/// <summary>
	/// A connect message carrying an error for the own adapter must surface as
	/// <see cref="Connector.ConnectionError"/> with the exact exception.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ProcessConnectMessage_OwnAdapterWithError_RaisesConnectionError()
	{
		var (connector, proc, _, _) = Build();

		var errors = new List<Exception>();
		connector.ConnectionError += errors.Add;

		var boom = new InvalidOperationException("connect-fail");
		await proc.ProcessConnectMessage(new ConnectMessage { Adapter = connector.Adapter, Error = boom }, CancellationToken);

		errors.Count.AssertEqual(1);
		errors[0].AssertSame(boom);
	}

	/// <summary>
	/// A connect message carrying an error for a foreign adapter must surface as the adapter-scoped
	/// <see cref="Connector.ConnectionErrorEx"/> with both the adapter and the exact exception.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ProcessConnectMessage_ForeignAdapterWithError_RaisesConnectionErrorEx()
	{
		var (connector, proc, _, _) = Build();

		var foreign = new ConnectDisconnectAdapter(connector.TransactionIdGenerator);

		var captured = new List<(IMessageAdapter adapter, Exception error)>();
		connector.ConnectionErrorEx += (a, e) => captured.Add((a, e));

		var boom = new InvalidOperationException("connect-fail-ex");
		await proc.ProcessConnectMessage(new ConnectMessage { Adapter = foreign, Error = boom }, CancellationToken);

		captured.Count.AssertEqual(1);
		captured[0].adapter.AssertSame(foreign);
		captured[0].error.AssertSame(boom);
	}

	/// <summary>
	/// <see cref="IConnectorMessageProcessor.ProcessDisconnectMessage"/> for the own adapter with no
	/// error must raise the parameterless <see cref="Connector.Disconnected"/> event.
	/// </summary>
	[TestMethod]
	public void ProcessDisconnectMessage_ForOwnAdapter_RaisesDisconnected()
	{
		var (connector, proc, _, _) = Build();

		var disconnectedCount = 0;
		connector.Disconnected += () => disconnectedCount++;

		proc.ProcessDisconnectMessage(new DisconnectMessage { Adapter = connector.Adapter });

		disconnectedCount.AssertEqual(1);
	}

	/// <summary>
	/// A disconnect ack for a foreign adapter must raise the adapter-scoped
	/// <see cref="Connector.DisconnectedEx"/> event with that adapter.
	/// </summary>
	[TestMethod]
	public void ProcessDisconnectMessage_ForForeignAdapter_RaisesDisconnectedEx()
	{
		var (connector, proc, _, _) = Build();

		var foreign = new ConnectDisconnectAdapter(connector.TransactionIdGenerator);

		var plainCount = 0;
		var exAdapters = new List<IMessageAdapter>();
		connector.Disconnected += () => plainCount++;
		connector.DisconnectedEx += exAdapters.Add;

		proc.ProcessDisconnectMessage(new DisconnectMessage { Adapter = foreign });

		plainCount.AssertEqual(0);
		exAdapters.Count.AssertEqual(1);
		exAdapters[0].AssertSame(foreign);
	}

	/// <summary>
	/// <see cref="IConnectorMessageProcessor.ProcessConnectionLostMessage"/> must raise the façade's
	/// <see cref="Connector.ConnectionLost"/> event for the originating adapter.
	/// </summary>
	[TestMethod]
	public void ProcessConnectionLostMessage_RaisesConnectionLost_WithMessageAdapter()
	{
		var (connector, proc, _, _) = Build();

		var captured = new List<IMessageAdapter>();
		connector.ConnectionLost += captured.Add;

		var adapter = connector.Adapter;
		proc.ProcessConnectionLostMessage(new ConnectionLostMessage { Adapter = adapter });

		captured.Count.AssertEqual(1);
		captured[0].AssertSame(adapter);
	}

	/// <summary>
	/// <see cref="IConnectorMessageProcessor.ProcessConnectionRestoredMessage"/> must raise the façade's
	/// <see cref="Connector.ConnectionRestored"/> event for the originating adapter.
	/// </summary>
	[TestMethod]
	public void ProcessConnectionRestoredMessage_RaisesConnectionRestored_WithMessageAdapter()
	{
		var (connector, proc, _, _) = Build();

		var captured = new List<IMessageAdapter>();
		connector.ConnectionRestored += captured.Add;

		var adapter = connector.Adapter;
		proc.ProcessConnectionRestoredMessage(new ConnectionRestoredMessage { Adapter = adapter });

		captured.Count.AssertEqual(1);
		captured[0].AssertSame(adapter);
	}

	#endregion

	#region Strategy 1 — market-data handlers

	/// <summary>
	/// <see cref="IConnectorMessageProcessor.ProcessQuotesMessage"/> must raise
	/// <see cref="Connector.OrderBookReceived"/> for a message linked to an order-book subscription.
	/// The order book is matched against the connector's own subscription manager, so the subscription
	/// is registered through <see cref="Connector.Subscribe(Subscription)"/> and referenced by id.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ProcessQuotesMessage_RaisesOrderBookReceived()
	{
		var (connector, proc, _, _) = Build();

		var secId = Helper.CreateSecurityId();
		var sub = MarketData(secId, DataType.MarketDepth);
		connector.Subscribe(sub);

		var received = new List<(Subscription sub, IOrderBookMessage book)>();
		connector.OrderBookReceived += (s, b) => received.Add((s, b));

		var book = new QuoteChangeMessage
		{
			SecurityId = secId,
			ServerTime = DateTime.UtcNow,
			Bids = [new QuoteChange(100m, 1m)],
			Asks = [new QuoteChange(101m, 1m)],
		};
		book.SetSubscriptionIds([sub.TransactionId]);

		await proc.ProcessQuotesMessage(book, CancellationToken);

		received.Count.AssertEqual(1);
		received[0].sub.AssertSame(sub);
	}

	/// <summary>
	/// <see cref="IConnectorMessageProcessor.ProcessLevel1ChangeMessage"/> must raise
	/// <see cref="Connector.Level1Received"/> for a message linked to a level1 subscription.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ProcessLevel1ChangeMessage_RaisesLevel1Received()
	{
		var (connector, proc, _, _) = Build();

		var secId = Helper.CreateSecurityId();
		var sub = MarketData(secId, DataType.Level1);
		connector.Subscribe(sub);

		var received = new List<(Subscription sub, Level1ChangeMessage msg)>();
		connector.Level1Received += (s, m) => received.Add((s, m));

		var l1 = new Level1ChangeMessage { SecurityId = secId, ServerTime = DateTime.UtcNow }
			.Add(Level1Fields.LastTradePrice, 100m);
		l1.SetSubscriptionIds([sub.TransactionId]);

		await proc.ProcessLevel1ChangeMessage(l1, CancellationToken);

		received.Count.AssertEqual(1);
		received[0].sub.AssertSame(sub);
	}

	/// <summary>
	/// <see cref="IConnectorMessageProcessor.ProcessNewsMessage"/> must raise
	/// <see cref="Connector.NewsReceived"/> for a message linked to a news subscription.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ProcessNewsMessage_RaisesNewsReceived()
	{
		var (connector, proc, _, _) = Build();

		var sub = new Subscription(new MarketDataMessage { IsSubscribe = true, DataType2 = DataType.News });
		connector.Subscribe(sub);

		var received = new List<(Subscription sub, News news)>();
		connector.NewsReceived += (s, n) => received.Add((s, n));

		var news = new NewsMessage { Id = "n1", Headline = "headline", ServerTime = DateTime.UtcNow };
		news.SetSubscriptionIds([sub.TransactionId]);

		await proc.ProcessNewsMessage(news, CancellationToken);

		received.Count.AssertEqual(1);
		received[0].sub.AssertSame(sub);
	}

	/// <summary>
	/// <see cref="IConnectorMessageProcessor.ProcessBoardMessage"/> resolves the board through the
	/// exchange-info provider and raises <see cref="Connector.BoardReceived"/> against the board-lookup
	/// subscriptions returned by the processor's own subscription manager (the injected collaborator).
	/// </summary>
	[TestMethod]
	public void ProcessBoardMessage_RaisesBoardReceived()
	{
		var (connector, proc, injectedSm, _) = Build();

		// The board lookup response is matched to its pending lookup subscription by subscription id.
		var lookup = new Subscription(new BoardLookupMessage());
		injectedSm.Subscribe(lookup);

		var received = new List<(Subscription sub, ExchangeBoard board)>();
		connector.BoardReceived += (s, b) => received.Add((s, b));

		var msg = new BoardMessage { Code = "TESTBRD", ExchangeCode = "TESTEX" };
		msg.SetSubscriptionIds([lookup.TransactionId]);

		proc.ProcessBoardMessage(msg);

		received.Count.AssertEqual(1);
		received[0].sub.AssertSame(lookup);
	}

	/// <summary>
	/// <see cref="IConnectorMessageProcessor.ProcessBoardStateMessage"/> raises
	/// <see cref="Connector.BoardReceived"/> against the connector's own subscription manager, so a
	/// board-lookup subscription is registered through the façade and referenced by id.
	/// </summary>
	[TestMethod]
	public void ProcessBoardStateMessage_RaisesBoardReceived()
	{
		var (connector, proc, _, _) = Build();

		var sub = new Subscription(new BoardLookupMessage());
		connector.Subscribe(sub);

		var received = new List<(Subscription sub, ExchangeBoard board)>();
		connector.BoardReceived += (s, b) => received.Add((s, b));

		var msg = new BoardStateMessage { BoardCode = "TESTBRD", State = SessionStates.Active };
		msg.SetSubscriptionIds([sub.TransactionId]);

		proc.ProcessBoardStateMessage(msg);

		received.Count.AssertEqual(1);
		received[0].sub.AssertSame(sub);
	}

	/// <summary>
	/// <see cref="IConnectorMessageProcessor.ProcessSecurityMessage"/> raises
	/// <see cref="Connector.SecurityReceived"/> against the security-lookup subscriptions returned by the
	/// processor's own subscription manager (the injected collaborator).
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ProcessSecurityMessage_RaisesSecurityReceived()
	{
		var (connector, proc, injectedSm, _) = Build();

		// The security lookup response is matched to its pending lookup subscription by subscription id.
		var lookup = new Subscription(new SecurityLookupMessage());
		injectedSm.Subscribe(lookup);

		var received = new List<(Subscription sub, Security security)>();
		connector.SecurityReceived += (s, sec) => received.Add((s, sec));

		var msg = new SecurityMessage { SecurityId = Helper.CreateSecurityId() };
		msg.SetSubscriptionIds([lookup.TransactionId]);

		await proc.ProcessSecurityMessage(msg, CancellationToken);

		received.Count.AssertEqual(1);
		received[0].sub.AssertSame(lookup);
	}

	/// <summary>
	/// <see cref="IConnectorMessageProcessor.ProcessDataTypeInfoMessage"/> raises
	/// <see cref="Connector.DataTypeReceived"/> against the connector's own subscription manager, so a
	/// data-type-lookup subscription is registered through the façade and referenced by id.
	/// </summary>
	[TestMethod]
	public void ProcessDataTypeInfoMessage_RaisesDataTypeReceived()
	{
		var (connector, proc, _, _) = Build();

		var sub = new Subscription(new DataTypeLookupMessage());
		connector.Subscribe(sub);

		var received = new List<(Subscription sub, DataType dt)>();
		connector.DataTypeReceived += (s, dt) => received.Add((s, dt));

		var msg = new DataTypeInfoMessage { FileDataType = DataType.Ticks };
		msg.SetSubscriptionIds([sub.TransactionId]);

		proc.ProcessDataTypeInfoMessage(msg);

		received.Count.AssertEqual(1);
		received[0].sub.AssertSame(sub);
	}

	#endregion

	#region Strategy 1 — execution fan-out (5 distinguished sub-handlers)

	/// <summary>
	/// Execution sub-handler #1 — a <see cref="DataType.Ticks"/> execution must be routed to the trade
	/// path and raise <see cref="Connector.TickTradeReceived"/> for a message linked to a ticks subscription.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ProcessExecutionMessage_Ticks_RaisesTickTradeReceived()
	{
		var (connector, proc, _, _) = Build();

		var secId = Helper.CreateSecurityId();
		var sub = MarketData(secId, DataType.Ticks);
		connector.Subscribe(sub);

		var received = new List<(Subscription sub, ITickTradeMessage tick)>();
		connector.TickTradeReceived += (s, t) => received.Add((s, t));

		var tick = new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = secId,
			ServerTime = DateTime.UtcNow,
			TradeId = 1,
			TradePrice = 100m,
			TradeVolume = 1m,
		};
		tick.SetSubscriptionIds([sub.TransactionId]);

		await proc.ProcessExecutionMessage(tick, CancellationToken);

		received.Count.AssertEqual(1);
		received[0].sub.AssertSame(sub);
	}

	/// <summary>
	/// Execution sub-handler #2 — a <see cref="DataType.OrderLog"/> execution must be routed to the
	/// order-log path and raise <see cref="Connector.OrderLogReceived"/> for a message linked to an
	/// order-log subscription.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ProcessExecutionMessage_OrderLog_RaisesOrderLogReceived()
	{
		var (connector, proc, _, _) = Build();

		var secId = Helper.CreateSecurityId();
		var sub = MarketData(secId, DataType.OrderLog);
		connector.Subscribe(sub);

		var received = new List<(Subscription sub, IOrderLogMessage log)>();
		connector.OrderLogReceived += (s, l) => received.Add((s, l));

		var log = new ExecutionMessage
		{
			DataTypeEx = DataType.OrderLog,
			SecurityId = secId,
			ServerTime = DateTime.UtcNow,
			OrderId = 1,
			OrderPrice = 100m,
			OrderVolume = 1m,
			Side = Sides.Buy,
			OrderState = OrderStates.Active,
		};
		log.SetSubscriptionIds([sub.TransactionId]);

		await proc.ProcessExecutionMessage(log, CancellationToken);

		received.Count.AssertEqual(1);
		received[0].sub.AssertSame(sub);
	}

	/// <summary>
	/// Execution sub-handler #3 — a <see cref="DataType.Transactions"/> execution carrying order info is
	/// routed to the transaction path. A freshly registered order transitions to its first known state and
	/// is surfaced through <see cref="Connector.OrderReceived"/> (the modern, non-obsolete order event)
	/// against an order-status subscription, carrying the very same <see cref="Order"/> instance.
	/// Distinguishing it from the trade path, no <see cref="Connector.OwnTradeReceived"/> event fires.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ProcessExecutionMessage_TransactionOrderInfo_RaisesOrderReceived()
	{
		var (connector, proc, _, entityCache) = Build();

		var security = Helper.CreateSecurity();
		var secId = security.ToSecurityId();
		var portfolio = new Portfolio { Name = "PF" };
		var transId = connector.TransactionIdGenerator.GetNextId();

		var order = new Order
		{
			Security = security,
			Portfolio = portfolio,
			TransactionId = transId,
			Type = OrderTypes.Limit,
			Price = 100m,
			Volume = 1m,
			Side = Sides.Buy,
		};
		entityCache.AddOrderByRegistrationId(order);

		var statusSub = new Subscription(new OrderStatusMessage { IsSubscribe = true });
		connector.Subscribe(statusSub);

		var received = new List<(Subscription sub, Order order)>();
		var ownTrades = new List<(Subscription sub, MyTrade trade)>();
		connector.OrderReceived += (s, o) => received.Add((s, o));
		connector.OwnTradeReceived += (s, t) => ownTrades.Add((s, t));

		var msg = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = secId,
			ServerTime = DateTime.UtcNow,
			TransactionId = transId,
			OrderState = OrderStates.Active,
		};
		msg.SetSubscriptionIds([statusSub.TransactionId]);

		await proc.ProcessExecutionMessage(msg, CancellationToken);

		received.Count.AssertEqual(1);
		received[0].order.AssertSame(order);
		ownTrades.Count.AssertEqual(0);
	}

	/// <summary>
	/// Execution sub-handler #4 — a <see cref="DataType.Transactions"/> execution carrying trade info is
	/// routed to the own-trade path and raises <see cref="Connector.OwnTradeReceived"/> (the modern,
	/// non-obsolete own-trade event) against an order-status subscription. Distinguishing it from the
	/// order path, no <see cref="Connector.OrderReceived"/> event fires.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ProcessExecutionMessage_TransactionTradeInfo_RaisesOwnTradeReceived()
	{
		var (connector, proc, _, entityCache) = Build();

		var security = Helper.CreateSecurity();
		var secId = security.ToSecurityId();
		var portfolio = new Portfolio { Name = "PF" };
		var transId = connector.TransactionIdGenerator.GetNextId();

		var order = new Order
		{
			Security = security,
			Portfolio = portfolio,
			TransactionId = transId,
			Type = OrderTypes.Limit,
			Price = 100m,
			Volume = 1m,
			Side = Sides.Buy,
			State = OrderStates.Active,
		};
		entityCache.AddOrderByRegistrationId(order);

		var statusSub = new Subscription(new OrderStatusMessage { IsSubscribe = true });
		connector.Subscribe(statusSub);

		var received = new List<(Subscription sub, Order order)>();
		var ownTrades = new List<(Subscription sub, MyTrade trade)>();
		connector.OrderReceived += (s, o) => received.Add((s, o));
		connector.OwnTradeReceived += (s, t) => ownTrades.Add((s, t));

		var msg = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = secId,
			ServerTime = DateTime.UtcNow,
			TransactionId = transId,
			TradeId = 555,
			TradePrice = 100m,
			TradeVolume = 1m,
		};
		msg.SetSubscriptionIds([statusSub.TransactionId]);

		await proc.ProcessExecutionMessage(msg, CancellationToken);

		ownTrades.Count.AssertEqual(1);
		received.Count.AssertEqual(0);
	}

	/// <summary>
	/// Execution mass-cancellation branch — when the originating transaction id was registered as a mass
	/// cancellation, a successful <see cref="DataType.Transactions"/> execution raises
	/// <see cref="Connector.MassOrderCanceled"/> with that id.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ProcessExecutionMessage_MassCancellation_RaisesMassOrderCanceled()
	{
		var (connector, proc, _, entityCache) = Build();

		var originId = connector.TransactionIdGenerator.GetNextId();
		entityCache.TryAddMassCancelationId(originId);

		var canceled = new List<long>();
		connector.MassOrderCanceled += canceled.Add;

		var msg = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			ServerTime = DateTime.UtcNow,
			OriginalTransactionId = originId,
		};

		await proc.ProcessExecutionMessage(msg, CancellationToken);

		canceled.Count.AssertEqual(1);
		canceled[0].AssertEqual(originId);
	}

	/// <summary>
	/// Execution sub-handler #5 (negative) — an execution whose data type is neither transactions, ticks
	/// nor order-log is unsupported and must throw <see cref="ArgumentOutOfRangeException"/>.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ProcessExecutionMessage_UnsupportedDataType_Throws()
	{
		var (_, proc, _, _) = Build();

		var msg = new ExecutionMessage
		{
			DataTypeEx = DataType.MarketDepth,
			SecurityId = Helper.CreateSecurityId(),
			ServerTime = DateTime.UtcNow,
		};

		await ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () => await proc.ProcessExecutionMessage(msg, CancellationToken));
	}

	#endregion

	#region Strategy 1 — portfolio & position handlers

	/// <summary>
	/// <see cref="IConnectorMessageProcessor.ProcessPortfolioMessage"/> raises
	/// <see cref="Connector.PortfolioReceived"/> against the connector's own subscription manager, so a
	/// portfolio-lookup subscription is registered through the façade and referenced by id.
	/// </summary>
	[TestMethod]
	public void ProcessPortfolioMessage_RaisesPortfolioReceived()
	{
		var (connector, proc, _, _) = Build();

		var sub = new Subscription(new PortfolioLookupMessage { IsSubscribe = true });
		connector.Subscribe(sub);

		var received = new List<(Subscription sub, Portfolio portfolio)>();
		connector.PortfolioReceived += (s, p) => received.Add((s, p));

		var msg = new PortfolioMessage { PortfolioName = "PF" };
		msg.SetSubscriptionIds([sub.TransactionId]);

		proc.ProcessPortfolioMessage(msg);

		received.Count.AssertEqual(1);
		received[0].sub.AssertSame(sub);
	}

	/// <summary>
	/// <see cref="IConnectorMessageProcessor.ProcessPositionChangeMessage"/> for a regular (non-money)
	/// position update resolves the position and surfaces it through <see cref="Connector.PositionReceived"/>
	/// (the modern, non-obsolete position event) against a matching subscription in the connector's own
	/// subscription manager.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ProcessPositionChangeMessage_RaisesPositionReceived()
	{
		var (connector, proc, _, _) = Build();

		var sub = new Subscription(new PortfolioLookupMessage { IsSubscribe = true });
		connector.Subscribe(sub);

		var received = new List<(Subscription sub, Position position)>();
		connector.PositionReceived += (s, p) => received.Add((s, p));

		var msg = new PositionChangeMessage
		{
			SecurityId = Helper.CreateSecurityId(),
			PortfolioName = "PF",
			ServerTime = DateTime.UtcNow,
		}.Add(PositionChangeTypes.CurrentValue, 5m);
		msg.SetSubscriptionIds([sub.TransactionId]);

		await proc.ProcessPositionChangeMessage(msg, CancellationToken);

		received.Count.AssertEqual(1);
	}

	#endregion

	#region Strategy 1 — subscription-lifecycle handlers

	/// <summary>
	/// <see cref="IConnectorMessageProcessor.ProcessSubscriptionResponseMessage"/> for a successful
	/// market-data subscribe response raises <see cref="Connector.SubscriptionStarted"/>. The pending
	/// subscription lives in the processor's own subscription manager (the injected collaborator).
	/// </summary>
	[TestMethod]
	public void ProcessSubscriptionResponseMessage_Success_RaisesSubscriptionStarted()
	{
		var (connector, proc, injectedSm, _) = Build();

		var sub = MarketData(Helper.CreateSecurityId(), DataType.Ticks);
		injectedSm.Subscribe(sub);

		var started = new List<Subscription>();
		connector.SubscriptionStarted += started.Add;

		proc.ProcessSubscriptionResponseMessage(new SubscriptionResponseMessage { OriginalTransactionId = sub.TransactionId });

		started.Count.AssertEqual(1);
		started[0].AssertSame(sub);
	}

	/// <summary>
	/// <see cref="IConnectorMessageProcessor.ProcessSubscriptionFinishedMessage"/> for an active
	/// subscription raises <see cref="Connector.SubscriptionStopped"/> with a null error.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ProcessSubscriptionFinishedMessage_RaisesSubscriptionStopped()
	{
		var (connector, proc, injectedSm, _) = Build();

		var sub = MarketData(Helper.CreateSecurityId(), DataType.Ticks);
		var transId = SubscribeActivate(injectedSm, sub);

		var stopped = new List<(Subscription sub, Exception error)>();
		connector.SubscriptionStopped += (s, e) => stopped.Add((s, e));

		await proc.ProcessSubscriptionFinishedMessage(new SubscriptionFinishedMessage { OriginalTransactionId = transId }, CancellationToken);

		stopped.Count.AssertEqual(1);
		stopped[0].sub.AssertSame(sub);
		stopped[0].error.AssertNull();
	}

	/// <summary>
	/// <see cref="IConnectorMessageProcessor.ProcessSubscriptionOnlineMessage"/> for an active
	/// subscription raises <see cref="Connector.SubscriptionOnline"/>.
	/// </summary>
	[TestMethod]
	public void ProcessSubscriptionOnlineMessage_RaisesSubscriptionOnline()
	{
		var (connector, proc, injectedSm, _) = Build();

		var sub = MarketData(Helper.CreateSecurityId(), DataType.Ticks);
		var transId = SubscribeActivate(injectedSm, sub);

		var online = new List<Subscription>();
		connector.SubscriptionOnline += online.Add;

		proc.ProcessSubscriptionOnlineMessage(new SubscriptionOnlineMessage { OriginalTransactionId = transId });

		online.Count.AssertEqual(1);
		online[0].AssertSame(sub);
	}

	/// <summary>
	/// <see cref="IConnectorMessageProcessor.ProcessCandleMessage"/> raises
	/// <see cref="Connector.CandleReceived"/> for a finished candle linked to an active candle
	/// subscription held by the processor's own subscription manager (the injected collaborator).
	/// </summary>
	[TestMethod]
	public void ProcessCandleMessage_RaisesCandleReceived()
	{
		var (connector, proc, injectedSm, _) = Build();

		var secId = Helper.CreateSecurityId();
		var tf = TimeSpan.FromMinutes(1).TimeFrame();
		var sub = new Subscription(new MarketDataMessage { IsSubscribe = true, SecurityId = secId, DataType2 = tf });
		var transId = SubscribeActivate(injectedSm, sub);

		var received = new List<(Subscription sub, ICandleMessage candle)>();
		connector.CandleReceived += (s, c) => received.Add((s, c));

		var candle = new TimeFrameCandleMessage
		{
			SecurityId = secId,
			OpenTime = DateTime.UtcNow,
			State = CandleStates.Finished,
			DataType = tf,
		};
		candle.SetSubscriptionIds([transId]);

		proc.ProcessCandleMessage(candle);

		received.Count.AssertEqual(1);
		received[0].sub.AssertSame(sub);
	}

	/// <summary>
	/// <see cref="IConnectorMessageProcessor.ProcessSubscriptionMessage"/> is the generic fan-out for any
	/// remaining subscription-bearing message. It fires <see cref="Connector.SubscriptionReceived"/>
	/// <em>twice</em> per matching subscription by design: the core raise invokes the supplied callback —
	/// which for this handler <em>is</em> the subscription-received raiser — and then additionally fires
	/// the subscription-received event itself. This deterministic double-raise is a documented
	/// behavioral characteristic that the decomposition preserves exactly.
	/// </summary>
	[TestMethod]
	public void ProcessSubscriptionMessage_FiresSubscriptionReceivedTwice_ByDesign()
	{
		var (connector, proc, _, _) = Build();

		var secId = Helper.CreateSecurityId();
		var sub = MarketData(secId, DataType.Ticks);
		connector.Subscribe(sub);

		var received = new List<(Subscription sub, object arg)>();
		connector.SubscriptionReceived += (s, a) => received.Add((s, a));

		var msg = new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = secId,
			ServerTime = DateTime.UtcNow,
		};
		msg.SetSubscriptionIds([sub.TransactionId]);

		proc.ProcessSubscriptionMessage(msg);

		// Deterministic dual-raise: one from the invoked callback, one from the explicit raise.
		received.Count.AssertEqual(2);
		received[0].sub.AssertSame(sub);
		received[1].sub.AssertSame(sub);
	}

	#endregion

	#region Strategy 1 — error, security-remove & change-password handlers

	/// <summary>
	/// Robust anchor: <see cref="IConnectorMessageProcessor.ProcessErrorMessage"/> must raise the
	/// façade's <see cref="Connector.Error"/> event carrying exactly the supplied exception.
	/// </summary>
	[TestMethod]
	public void ProcessErrorMessage_RaisesErrorEvent_WithSameException()
	{
		var (connector, proc, _, _) = Build();

		var captured = new List<Exception>();
		connector.Error += captured.Add;

		var boom = new InvalidOperationException("boom");
		proc.ProcessErrorMessage(new ErrorMessage { Error = boom });

		captured.Count.AssertEqual(1);
		captured[0].AssertSame(boom);
	}

	/// <summary>
	/// <see cref="IConnectorMessageProcessor.ProcessSecurityRemoveMessage"/> deletes a stored security and
	/// fires the <see cref="ISecurityProvider.Removed"/> event with that security.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ProcessSecurityRemoveMessage_RaisesRemoved()
	{
		var (connector, proc, _, _) = Build();

		var security = Helper.CreateSecurity();
		await connector.SecurityStorage.SaveAsync(security, false, CancellationToken);

		var removed = new List<Security>();
		((ISecurityProvider)connector).Removed += removed.AddRange;

		await proc.ProcessSecurityRemoveMessage(new SecurityRemoveMessage { SecurityId = security.ToSecurityId() }, CancellationToken);

		removed.Count.AssertEqual(1);
		removed[0].AssertSame(security);
	}

	/// <summary>
	/// <see cref="IConnectorMessageProcessor.ProcessChangePasswordMessage"/> raises
	/// <see cref="Connector.ChangePasswordResult"/> carrying the originating transaction id and the exact error.
	/// </summary>
	[TestMethod]
	public void ProcessChangePasswordMessage_RaisesChangePasswordResult()
	{
		var (connector, proc, _, _) = Build();

		var captured = new List<(long id, Exception error)>();
		connector.ChangePasswordResult += (id, e) => captured.Add((id, e));

		var boom = new InvalidOperationException("pwd");
		proc.ProcessChangePasswordMessage(new ChangePasswordMessage { OriginalTransactionId = 4242, Error = boom });

		captured.Count.AssertEqual(1);
		captured[0].id.AssertEqual(4242L);
		captured[0].error.AssertSame(boom);
	}

	#endregion

	#region Strategy 2 — end-to-end dispatch-order parity

	/// <summary>
	/// Top-priority parity guard. Feeding <c>Connect</c>, <c>Error</c> and <c>Disconnect</c> out
	/// messages through the façade drives the connector's <em>internal</em> processor across the real
	/// <c>OnProcessMessage</c> switch. The recorded tokens must interleave exactly as
	/// <c>Msg:Connect, Connected, Msg:Error, Error, Msg:Disconnect, Disconnected</c>, proving both that
	/// (i) the preamble's per-message event fires before each type-specific handler and (ii) messages
	/// are dispatched in strict FIFO order (the out channel is a single-consumer queue). The comparison
	/// is an ordered, index-by-index equality, so any regression in the switch order or the
	/// preamble/handler order fails the test. This single feed covers the Connect, Error and Disconnect routes.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_PreambleThenHandler_PreservesFifoOrder_PureFeed()
	{
		var connector = NewConnector();

		var recorded = new List<string>();
		var sync = new object();

		void Add(string token)
		{
			lock (sync)
				recorded.Add(token);
		}

		var injectedError = new InvalidOperationException("boom");
		var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		// Preamble hook (fired by RaiseNewMessage for every processed message, before the handler).
		connector.NewOutMessageAsync += (message, token) =>
		{
			if (message.Type is MessageTypes.Connect or MessageTypes.Error or MessageTypes.Disconnect)
				Add("Msg:" + message.Type);

			return default;
		};

		// Handler-produced events.
		connector.Connected += () => Add(nameof(connector.Connected));
		connector.Error += ex =>
		{
			if (ReferenceEquals(ex, injectedError))
				Add(nameof(connector.Error));
		};
		connector.Disconnected += () =>
		{
			Add(nameof(connector.Disconnected));
			done.TrySetResult(true);
		};

		await connector.SendOutMessageAsync(new ConnectMessage { Adapter = connector.Adapter }, CancellationToken);
		await connector.SendOutMessageAsync(new ErrorMessage { Error = injectedError }, CancellationToken);
		await connector.SendOutMessageAsync(new DisconnectMessage { Adapter = connector.Adapter }, CancellationToken);

		// Determinism guard: synchronize on the final expected event before comparing.
		await done.Task.WaitAsync(TimeSpan.FromSeconds(10));

		string actual;
		lock (sync)
			actual = string.Join(",", recorded);

		actual.AssertEqual("Msg:Connect,Connected,Msg:Error,Error,Msg:Disconnect,Disconnected");
	}

	/// <summary>
	/// Complementary parity check driven through a real inbound adapter (mirroring
	/// <c>ConnectorRoutingTests</c>). A genuine connect then disconnect must run the internal processor
	/// so that each connection event is immediately preceded by its preamble token and the connect is
	/// dispatched before the disconnect.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_RealAdapterConnectDisconnect_PreservesDispatchOrder()
	{
		var connector = NewConnector();

		var adapter = new ConnectDisconnectAdapter(connector.TransactionIdGenerator);
		connector.Adapter.InnerAdapters.Add(adapter);

		var recorded = new List<string>();
		var sync = new object();

		void Add(string token)
		{
			lock (sync)
				recorded.Add(token);
		}

		connector.NewOutMessageAsync += (message, token) =>
		{
			if (message.Type is MessageTypes.Connect or MessageTypes.Disconnect)
				Add("Msg:" + message.Type);

			return default;
		};

		connector.Connected += () => Add(nameof(connector.Connected));
		connector.Disconnected += () => Add(nameof(connector.Disconnected));

		await connector.ConnectAsync(CancellationToken);
		await connector.DisconnectAsync(CancellationToken);

		List<string> snapshot;
		lock (sync)
			snapshot = [.. recorded];

		// Each connection event fired exactly once.
		snapshot.Count(t => t == nameof(connector.Connected)).AssertEqual(1);
		snapshot.Count(t => t == nameof(connector.Disconnected)).AssertEqual(1);

		var connectedIndex = snapshot.IndexOf(nameof(connector.Connected));
		var disconnectedIndex = snapshot.IndexOf(nameof(connector.Disconnected));

		// The preamble token is emitted immediately before the corresponding handler event, proving
		// the preamble runs before the type-specific handler even across the real adapter pipeline.
		(connectedIndex > 0).AssertTrue("Connected must be preceded by its preamble token.");
		snapshot[connectedIndex - 1].AssertEqual("Msg:Connect");

		(disconnectedIndex > 0).AssertTrue("Disconnected must be preceded by its preamble token.");
		snapshot[disconnectedIndex - 1].AssertEqual("Msg:Disconnect");

		// Connect is dispatched before Disconnect (FIFO across the lifecycle).
		(connectedIndex < disconnectedIndex).AssertTrue("Connected must be dispatched before Disconnected.");
	}

	/// <summary>Route parity: a <c>ConnectionLost</c> out message reaches <c>ProcessConnectionLostMessage</c>.</summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_Route_ConnectionLost()
	{
		var connector = NewConnector();

		var tcs = Signal();
		connector.ConnectionLost += _ => tcs.TrySetResult(true);

		await connector.SendOutMessageAsync(new ConnectionLostMessage { Adapter = connector.Adapter }, CancellationToken);

		await ShouldSignal(tcs);
	}

	/// <summary>Route parity: a <c>ConnectionRestored</c> out message reaches <c>ProcessConnectionRestoredMessage</c>.</summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_Route_ConnectionRestored()
	{
		var connector = NewConnector();

		var tcs = Signal();
		connector.ConnectionRestored += _ => tcs.TrySetResult(true);

		await connector.SendOutMessageAsync(new ConnectionRestoredMessage { Adapter = connector.Adapter }, CancellationToken);

		await ShouldSignal(tcs);
	}

	/// <summary>Route parity: a <c>QuoteChange</c> out message reaches <c>ProcessQuotesMessage</c>.</summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_Route_QuoteChange()
	{
		var connector = NewConnector();

		var secId = Helper.CreateSecurityId();
		var sub = MarketData(secId, DataType.MarketDepth);
		connector.Subscribe(sub);

		var tcs = Signal();
		connector.OrderBookReceived += (_, _) => tcs.TrySetResult(true);

		var book = new QuoteChangeMessage
		{
			SecurityId = secId,
			ServerTime = DateTime.UtcNow,
			Bids = [new QuoteChange(100m, 1m)],
			Asks = [new QuoteChange(101m, 1m)],
		};
		book.SetSubscriptionIds([sub.TransactionId]);

		await connector.SendOutMessageAsync(book, CancellationToken);

		await ShouldSignal(tcs);
	}

	/// <summary>Route parity: a <c>Board</c> out message reaches <c>ProcessBoardMessage</c>.</summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_Route_Board()
	{
		var connector = NewConnector();

		var lookup = new Subscription(new BoardLookupMessage());
		connector.Subscribe(lookup);

		var tcs = Signal();
		connector.BoardReceived += (_, _) => tcs.TrySetResult(true);

		var msg = new BoardMessage { Code = "TESTBRD", ExchangeCode = "TESTEX" };
		msg.SetSubscriptionIds([lookup.TransactionId]);

		await connector.SendOutMessageAsync(msg, CancellationToken);

		await ShouldSignal(tcs);
	}

	/// <summary>Route parity: a <c>BoardState</c> out message reaches <c>ProcessBoardStateMessage</c>.</summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_Route_BoardState()
	{
		var connector = NewConnector();

		var sub = new Subscription(new BoardLookupMessage());
		connector.Subscribe(sub);

		var tcs = Signal();
		connector.BoardReceived += (_, _) => tcs.TrySetResult(true);

		var msg = new BoardStateMessage { BoardCode = "TESTBRD", State = SessionStates.Active };
		msg.SetSubscriptionIds([sub.TransactionId]);

		await connector.SendOutMessageAsync(msg, CancellationToken);

		await ShouldSignal(tcs);
	}

	/// <summary>Route parity: a <c>Security</c> out message reaches <c>ProcessSecurityMessage</c>.</summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_Route_Security()
	{
		var connector = NewConnector();

		var lookup = new Subscription(new SecurityLookupMessage());
		connector.Subscribe(lookup);

		var tcs = Signal();
		connector.SecurityReceived += (_, _) => tcs.TrySetResult(true);

		var msg = new SecurityMessage { SecurityId = Helper.CreateSecurityId() };
		msg.SetSubscriptionIds([lookup.TransactionId]);

		await connector.SendOutMessageAsync(msg, CancellationToken);

		await ShouldSignal(tcs);
	}

	/// <summary>Route parity: a <c>DataTypeInfo</c> out message reaches <c>ProcessDataTypeInfoMessage</c>.</summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_Route_DataTypeInfo()
	{
		var connector = NewConnector();

		var sub = new Subscription(new DataTypeLookupMessage());
		connector.Subscribe(sub);

		var tcs = Signal();
		connector.DataTypeReceived += (_, _) => tcs.TrySetResult(true);

		var msg = new DataTypeInfoMessage { FileDataType = DataType.Ticks };
		msg.SetSubscriptionIds([sub.TransactionId]);

		await connector.SendOutMessageAsync(msg, CancellationToken);

		await ShouldSignal(tcs);
	}

	/// <summary>Route parity: a <c>Level1Change</c> out message reaches <c>ProcessLevel1ChangeMessage</c>.</summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_Route_Level1Change()
	{
		var connector = NewConnector();

		var secId = Helper.CreateSecurityId();
		var sub = MarketData(secId, DataType.Level1);
		connector.Subscribe(sub);

		var tcs = Signal();
		connector.Level1Received += (_, _) => tcs.TrySetResult(true);

		var l1 = new Level1ChangeMessage { SecurityId = secId, ServerTime = DateTime.UtcNow }
			.Add(Level1Fields.LastTradePrice, 100m);
		l1.SetSubscriptionIds([sub.TransactionId]);

		await connector.SendOutMessageAsync(l1, CancellationToken);

		await ShouldSignal(tcs);
	}

	/// <summary>Route parity: a <c>News</c> out message reaches <c>ProcessNewsMessage</c>.</summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_Route_News()
	{
		var connector = NewConnector();

		var sub = new Subscription(new MarketDataMessage { IsSubscribe = true, DataType2 = DataType.News });
		connector.Subscribe(sub);

		var tcs = Signal();
		connector.NewsReceived += (_, _) => tcs.TrySetResult(true);

		var news = new NewsMessage { Id = "n1", Headline = "headline", ServerTime = DateTime.UtcNow };
		news.SetSubscriptionIds([sub.TransactionId]);

		await connector.SendOutMessageAsync(news, CancellationToken);

		await ShouldSignal(tcs);
	}

	/// <summary>Route parity: an <c>Execution</c> (ticks) out message reaches <c>ProcessExecutionMessage</c>.</summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_Route_Execution()
	{
		var connector = NewConnector();

		var secId = Helper.CreateSecurityId();
		var sub = MarketData(secId, DataType.Ticks);
		connector.Subscribe(sub);

		var tcs = Signal();
		connector.TickTradeReceived += (_, _) => tcs.TrySetResult(true);

		var tick = new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = secId,
			ServerTime = DateTime.UtcNow,
			TradeId = 1,
			TradePrice = 100m,
			TradeVolume = 1m,
		};
		tick.SetSubscriptionIds([sub.TransactionId]);

		await connector.SendOutMessageAsync(tick, CancellationToken);

		await ShouldSignal(tcs);
	}

	/// <summary>Route parity: a <c>Portfolio</c> out message reaches <c>ProcessPortfolioMessage</c>.</summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_Route_Portfolio()
	{
		var connector = NewConnector();

		var sub = new Subscription(new PortfolioLookupMessage { IsSubscribe = true });
		connector.Subscribe(sub);

		var tcs = Signal();
		connector.PortfolioReceived += (_, _) => tcs.TrySetResult(true);

		var msg = new PortfolioMessage { PortfolioName = "PF" };
		msg.SetSubscriptionIds([sub.TransactionId]);

		await connector.SendOutMessageAsync(msg, CancellationToken);

		await ShouldSignal(tcs);
	}

	/// <summary>Route parity: a <c>PositionChange</c> out message reaches <c>ProcessPositionChangeMessage</c>.</summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_Route_PositionChange()
	{
		var connector = NewConnector();

		var sub = new Subscription(new PortfolioLookupMessage { IsSubscribe = true });
		connector.Subscribe(sub);

		var tcs = Signal();
		connector.PositionReceived += (_, _) => tcs.TrySetResult(true);

		var msg = new PositionChangeMessage
		{
			SecurityId = Helper.CreateSecurityId(),
			PortfolioName = "PF",
			ServerTime = DateTime.UtcNow,
		}.Add(PositionChangeTypes.CurrentValue, 5m);
		msg.SetSubscriptionIds([sub.TransactionId]);

		await connector.SendOutMessageAsync(msg, CancellationToken);

		await ShouldSignal(tcs);
	}

	/// <summary>Route parity: a <c>SubscriptionResponse</c> out message reaches <c>ProcessSubscriptionResponseMessage</c>.</summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_Route_SubscriptionResponse()
	{
		var connector = NewConnector();

		var sub = MarketData(Helper.CreateSecurityId(), DataType.Ticks);
		connector.Subscribe(sub);

		var tcs = Signal();
		connector.SubscriptionStarted += _ => tcs.TrySetResult(true);

		await connector.SendOutMessageAsync(new SubscriptionResponseMessage { OriginalTransactionId = sub.TransactionId }, CancellationToken);

		await ShouldSignal(tcs);
	}

	/// <summary>Route parity: a <c>SubscriptionFinished</c> out message reaches <c>ProcessSubscriptionFinishedMessage</c>.</summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_Route_SubscriptionFinished()
	{
		var connector = NewConnector();

		var sub = MarketData(Helper.CreateSecurityId(), DataType.Ticks);
		connector.Subscribe(sub);
		var transId = sub.TransactionId;

		var tcs = Signal();
		connector.SubscriptionStopped += (_, _) => tcs.TrySetResult(true);

		// Activate first (FIFO guarantees the response is processed before the finished message).
		await connector.SendOutMessageAsync(new SubscriptionResponseMessage { OriginalTransactionId = transId }, CancellationToken);
		await connector.SendOutMessageAsync(new SubscriptionFinishedMessage { OriginalTransactionId = transId }, CancellationToken);

		await ShouldSignal(tcs);
	}

	/// <summary>Route parity: a <c>SubscriptionOnline</c> out message reaches <c>ProcessSubscriptionOnlineMessage</c>.</summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_Route_SubscriptionOnline()
	{
		var connector = NewConnector();

		var sub = MarketData(Helper.CreateSecurityId(), DataType.Ticks);
		connector.Subscribe(sub);
		var transId = sub.TransactionId;

		var tcs = Signal();
		connector.SubscriptionOnline += _ => tcs.TrySetResult(true);

		await connector.SendOutMessageAsync(new SubscriptionResponseMessage { OriginalTransactionId = transId }, CancellationToken);
		await connector.SendOutMessageAsync(new SubscriptionOnlineMessage { OriginalTransactionId = transId }, CancellationToken);

		await ShouldSignal(tcs);
	}

	/// <summary>Route parity: a <c>RemoveSecurity</c> (extended type) out message reaches <c>ProcessSecurityRemoveMessage</c>.</summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_Route_RemoveSecurity()
	{
		var connector = NewConnector();

		var security = Helper.CreateSecurity();
		await connector.SecurityStorage.SaveAsync(security, false, CancellationToken);

		var tcs = Signal();
		((ISecurityProvider)connector).Removed += _ => tcs.TrySetResult(true);

		await connector.SendOutMessageAsync(new SecurityRemoveMessage { SecurityId = security.ToSecurityId() }, CancellationToken);

		await ShouldSignal(tcs);
	}

	/// <summary>Route parity: a <c>ChangePassword</c> out message reaches <c>ProcessChangePasswordMessage</c>.</summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_Route_ChangePassword()
	{
		var connector = NewConnector();

		var tcs = Signal();
		connector.ChangePasswordResult += (_, _) => tcs.TrySetResult(true);

		await connector.SendOutMessageAsync(new ChangePasswordMessage { OriginalTransactionId = 4242, Error = new InvalidOperationException("pwd") }, CancellationToken);

		await ShouldSignal(tcs);
	}

	/// <summary>
	/// Route parity for the <c>default</c> branch's candle path: a <see cref="CandleMessage"/> that matches
	/// no explicit switch case is routed to <c>ProcessCandleMessage</c>.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_Route_DefaultCandle()
	{
		var connector = NewConnector();

		var secId = Helper.CreateSecurityId();
		var tf = TimeSpan.FromMinutes(1).TimeFrame();
		var sub = new Subscription(new MarketDataMessage { IsSubscribe = true, SecurityId = secId, DataType2 = tf });
		connector.Subscribe(sub);
		var transId = sub.TransactionId;

		var tcs = Signal();
		connector.CandleReceived += (_, _) => tcs.TrySetResult(true);

		// Activate the candle subscription so the connector's manager yields it on the candle update.
		await connector.SendOutMessageAsync(new SubscriptionResponseMessage { OriginalTransactionId = transId }, CancellationToken);

		var candle = new TimeFrameCandleMessage
		{
			SecurityId = secId,
			OpenTime = DateTime.UtcNow,
			State = CandleStates.Finished,
			DataType = tf,
		};
		candle.SetSubscriptionIds([transId]);

		await connector.SendOutMessageAsync(candle, CancellationToken);

		await ShouldSignal(tcs);
	}

	/// <summary>
	/// Route parity for the <c>default</c> branch's generic path: a subscription-bearing message that is
	/// neither one of the explicit switch cases nor a <see cref="CandleMessage"/> (here a
	/// <see cref="SecurityLegsInfoMessage"/>) is routed to the generic <c>ProcessSubscriptionMessage</c>,
	/// which fires <see cref="Connector.SubscriptionReceived"/>.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_Route_DefaultGenericSubscription()
	{
		var connector = NewConnector();

		// Any registered subscription id makes the generic message resolvable; matching is id-based.
		var sub = MarketData(Helper.CreateSecurityId(), DataType.Ticks);
		connector.Subscribe(sub);

		var tcs = Signal();
		connector.SubscriptionReceived += (_, _) => tcs.TrySetResult(true);

		var msg = new SecurityLegsInfoMessage();
		msg.SetSubscriptionIds([sub.TransactionId]);

		await connector.SendOutMessageAsync(msg, CancellationToken);

		await ShouldSignal(tcs);
	}

	#endregion
}
