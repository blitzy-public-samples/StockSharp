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
	//   21. default              -> CandleMessage    ? ProcessCandleMessage
	//                               ISubscriptionIdMessage ? ProcessSubscriptionMessage
	// -----------------------------------------------------------------------------------------

	#region Mock adapter

	/// <summary>
	/// Minimal inbound adapter modelled on <c>ConnectorRoutingTests.LiveFeedCryptoAdapter</c> that
	/// echoes the connection lifecycle: it replies to <see cref="MessageTypes.Reset"/>,
	/// <see cref="MessageTypes.Connect"/> and <see cref="MessageTypes.Disconnect"/> with the matching
	/// out messages so that a real connect/disconnect drives the façade's internal processor.
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

	#region Helpers

	/// <summary>
	/// Builds a standalone <see cref="IConnectorMessageProcessor"/> over the supplied façade.
	/// The real <see cref="ConnectorMessageProcessor"/> takes focused collaborator dependencies
	/// (entity cache and subscription manager) in addition to the façade; those are constructed here
	/// exclusively from the connector's public surface so the test never reaches into façade internals.
	/// The freshly built subscription manager intentionally starts with an empty
	/// <c>SubscriptionsOnConnect</c> set, so <c>ProcessConnectMessage</c> reduces to a clean
	/// connected-event raise with no re-subscription side effects.
	/// </summary>
	private static IConnectorMessageProcessor CreateProcessor(Connector connector)
	{
		var entityCache = new EntityCache(connector, _ => null, connector.ExchangeInfoProvider, connector);
		var subscriptionManager = new ConnectorSubscriptionManager(connector, connector.TransactionIdGenerator, false);

		return new ConnectorMessageProcessor(connector, entityCache, subscriptionManager);
	}

	#endregion

	#region Strategy 1 — interface-level direct handler calls

	/// <summary>
	/// Robust anchor: <see cref="IConnectorMessageProcessor.ProcessErrorMessage"/> must raise the
	/// façade's <see cref="Connector.Error"/> event carrying exactly the supplied exception.
	/// </summary>
	[TestMethod]
	public void ProcessErrorMessage_RaisesErrorEvent_WithSameException()
	{
		var connector = new Connector();
		var proc = CreateProcessor(connector);

		var captured = new List<Exception>();
		connector.Error += captured.Add;

		var boom = new InvalidOperationException("boom");
		proc.ProcessErrorMessage(new ErrorMessage { Error = boom });

		captured.Count.AssertEqual(1);
		captured[0].AssertSame(boom);
	}

	/// <summary>
	/// <see cref="IConnectorMessageProcessor.ProcessConnectionLostMessage"/> must raise the façade's
	/// <see cref="Connector.ConnectionLost"/> event for the originating adapter.
	/// </summary>
	[TestMethod]
	public void ProcessConnectionLostMessage_RaisesConnectionLost_WithMessageAdapter()
	{
		var connector = new Connector();
		var proc = CreateProcessor(connector);

		var captured = new List<IMessageAdapter>();
		connector.ConnectionLost += captured.Add;

		var adapter = connector.Adapter;
		proc.ProcessConnectionLostMessage(new ConnectionLostMessage { Adapter = adapter });

		captured.Count.AssertEqual(1);
		captured[0].AssertSame(adapter);
	}

	/// <summary>
	/// Async surface coverage: <see cref="IConnectorMessageProcessor.ProcessConnectMessage"/> for the
	/// façade's own adapter must raise the parameterless <see cref="Connector.Connected"/> event.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ProcessConnectMessage_ForOwnAdapter_RaisesConnected()
	{
		var connector = new Connector();
		var proc = CreateProcessor(connector);

		var connectedCount = 0;
		connector.Connected += () => connectedCount++;

		await proc.ProcessConnectMessage(new ConnectMessage { Adapter = connector.Adapter }, CancellationToken);

		connectedCount.AssertEqual(1);
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
	/// preamble/handler order fails the test.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OnProcessMessage_PreambleThenHandler_PreservesFifoOrder_PureFeed()
	{
		var connector = new Connector();

		// Remove the default connect-time lookups so the Connect handler reduces to a clean
		// RaiseConnected() with no re-subscription traffic polluting the recorded sequence.
		connector.SubscriptionsOnConnect.Clear();

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

		try
		{
			await connector.SendOutMessageAsync(new ConnectMessage { Adapter = connector.Adapter }, CancellationToken);
			await connector.SendOutMessageAsync(new ErrorMessage { Error = injectedError }, CancellationToken);
			await connector.SendOutMessageAsync(new DisconnectMessage { Adapter = connector.Adapter }, CancellationToken);

			// Determinism guard: synchronize on the final expected event before comparing.
			await done.Task;

			string actual;
			lock (sync)
				actual = string.Join(",", recorded);

			actual.AssertEqual("Msg:Connect,Connected,Msg:Error,Error,Msg:Disconnect,Disconnected");
		}
		finally
		{
			connector.Dispose();
		}
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
		var connector = new Connector();

		// Keep the connect handshake to the connection lifecycle only (no lookup traffic).
		connector.SubscriptionsOnConnect.Clear();

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

	#endregion
}
