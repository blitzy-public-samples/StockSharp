namespace StockSharp.Tests;

/// <summary>
/// Direct component tests for the event-raising seam extracted from the <see cref="Connector"/> god object:
/// <see cref="ConnectorEventDispatcher"/> and its abstraction <see cref="IConnectorEventDispatcher"/>.
/// </summary>
/// <remarks>
/// The event-firing logic that previously lived as private <c>Raise*</c> helpers inside the
/// <see cref="Connector"/> partial classes now lives in <see cref="ConnectorEventDispatcher"/>. The public
/// event <em>declarations</em> remain on the <see cref="Connector"/> facade because they are part of the
/// <see cref="IConnector"/> surface and a C# event can only be raised from within its declaring type; the
/// dispatcher fires them through the facade's internal invoker hooks.
///
/// Because the dispatcher fires the events declared on the connector, every test constructs a bare
/// <see cref="Connector"/>, a <em>separate</em> <see cref="ConnectorEventDispatcher"/> bound to it,
/// subscribes to the connector's public events, invokes a <c>Raise*</c> method on the dispatcher and
/// observes the connector's event fire. These are pure in-memory tests: no adapter, connection, storage
/// or channel round-trip is required, so they are synchronous and need no timeout.
///
/// The suite has two goals:
/// <list type="number">
/// <item><description>each representative <c>Raise*</c> method fires the matching facade event with the correct payload; and</description></item>
/// <item><description>a sequence of <c>Raise*</c> calls fires the corresponding events in the <em>exact same order</em>
/// (behavioral parity — downstream strategies depend on event ordering).</description></item>
/// </list>
/// </remarks>
[TestClass]
public class ConnectorEventDispatcherTests : BaseTestClass
{
	// ===== Fixture =====

	/// <summary>
	/// Tracks every <see cref="Connector"/> constructed by the fixture so <see cref="DisposeConnectors"/>
	/// can release each one after the test, ensuring the disposable façade under test never leaks.
	/// </summary>
	private readonly List<Connector> _connectors = [];

	/// <summary>
	/// Creates a bare <see cref="Connector"/> together with a standalone <see cref="ConnectorEventDispatcher"/>
	/// bound to it, returning the dispatcher typed as the segregated <see cref="IConnectorEventDispatcher"/>
	/// abstraction so the tests drive events exclusively through the interface surface under test.
	/// </summary>
	/// <returns>
	/// A tuple of the freshly constructed connector and a dispatcher that fires that connector's events.
	/// </returns>
	/// <remarks>
	/// The dispatcher's constructor requires an <see cref="IConnectorSubscriptionManager"/>, which the real
	/// facade injects from its own composition. That collaborator is used only by the message-based
	/// <c>RaiseReceived</c> overload (to resolve the subscriptions a message belongs to); none of these tests
	/// exercise that path, so a loose <see cref="Mock{T}"/> is a safe, never-invoked stand-in that keeps the
	/// fixture free of adapters, storages and any data tier.
	/// </remarks>
	private (Connector connector, IConnectorEventDispatcher dispatcher) Create()
		=> Create(new Mock<IConnectorSubscriptionManager>().Object);

	/// <summary>
	/// Overload of <see cref="Create()"/> that binds the dispatcher to a caller-supplied
	/// <see cref="IConnectorSubscriptionManager"/>. Used by the message-based <c>RaiseReceived</c> tests,
	/// which must control the subscriptions a message resolves to via the injected seam.
	/// </summary>
	/// <param name="subscriptionManager">The subscription-manager seam the dispatcher resolves messages through.</param>
	/// <returns>A tuple of the freshly constructed (and tracked) connector and its bound dispatcher.</returns>
	private (Connector connector, IConnectorEventDispatcher dispatcher) Create(IConnectorSubscriptionManager subscriptionManager)
	{
		var connector = new Connector();
		_connectors.Add(connector);

		IConnectorEventDispatcher dispatcher = new ConnectorEventDispatcher(connector, subscriptionManager);

		return (connector, dispatcher);
	}

	/// <summary>
	/// Disposes every <see cref="Connector"/> the fixture created for the just-finished test. The façade is
	/// an <see cref="IDisposable"/> <c>BaseLogReceiver</c>; disposing it releases its in/out message channels
	/// and timers. Every tracked façade is disposed even if an earlier one faults, and any disposal faults are
	/// collected and re-thrown as an <see cref="AggregateException"/> so a teardown, channel or resource failure
	/// stays visible instead of silently passing the test. MSTest aggregates this cleanup fault with any exception
	/// the test body already threw, so a prior assertion failure is surfaced alongside it and is never masked.
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
	/// Builds a minimal ticks <see cref="Subscription"/> whose <see cref="Subscription.SubscriptionMessage"/>
	/// is a non-null market-data message, suitable for the subscription-carrying <c>Raise*</c> methods.
	/// </summary>
	/// <returns>A new, self-contained <see cref="Subscription"/> instance.</returns>
	private static Subscription CreateSubscription()
		=> new(DataType.Ticks);

	// ===== Firing tests: one "fired + payload" assertion per representative event =====

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseConnected"/> must fire <see cref="Connector.Connected"/>
	/// exactly once and leave the connector in the <see cref="ConnectionStates.Connected"/> state (the
	/// state transition happens before the event is fired).
	/// </summary>
	[TestMethod]
	public void RaiseConnected_FiresConnectedAndSetsState()
	{
		var (connector, dispatcher) = Create();

		var count = 0;
		connector.Connected += () => count++;

		dispatcher.RaiseConnected();

		count.AssertEqual(1);
		connector.ConnectionState.AssertEqual(ConnectionStates.Connected);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseDisconnected"/> must fire <see cref="Connector.Disconnected"/>
	/// exactly once and leave the connector in the <see cref="ConnectionStates.Disconnected"/> state.
	/// </summary>
	[TestMethod]
	public void RaiseDisconnected_FiresDisconnectedAndSetsState()
	{
		var (connector, dispatcher) = Create();

		var count = 0;
		connector.Disconnected += () => count++;

		dispatcher.RaiseDisconnected();

		count.AssertEqual(1);
		connector.ConnectionState.AssertEqual(ConnectionStates.Disconnected);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseConnectionError"/> must fire
	/// <see cref="Connector.ConnectionError"/> with the very same <see cref="Exception"/> reference.
	/// </summary>
	[TestMethod]
	public void RaiseConnectionError_FiresWithSameException()
	{
		var (connector, dispatcher) = Create();

		var count = 0;
		Exception captured = null;
		connector.ConnectionError += ex => { captured = ex; count++; };

		var error = new InvalidOperationException("connection failed");
		dispatcher.RaiseConnectionError(error);

		count.AssertEqual(1);
		captured.AssertSame(error);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseConnectedEx"/> must fire <see cref="Connector.ConnectedEx"/>
	/// with the same adapter instance.
	/// </summary>
	[TestMethod]
	public void RaiseConnectedEx_FiresWithSameAdapter()
	{
		var (connector, dispatcher) = Create();

		var adapter = new Mock<IMessageAdapter>().Object;
		IMessageAdapter captured = null;
		connector.ConnectedEx += a => captured = a;

		dispatcher.RaiseConnectedEx(adapter);

		captured.AssertSame(adapter);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseDisconnectedEx"/> must fire
	/// <see cref="Connector.DisconnectedEx"/> with the same adapter instance.
	/// </summary>
	[TestMethod]
	public void RaiseDisconnectedEx_FiresWithSameAdapter()
	{
		var (connector, dispatcher) = Create();

		var adapter = new Mock<IMessageAdapter>().Object;
		IMessageAdapter captured = null;
		connector.DisconnectedEx += a => captured = a;

		dispatcher.RaiseDisconnectedEx(adapter);

		captured.AssertSame(adapter);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseConnectionErrorEx"/> must fire
	/// <see cref="Connector.ConnectionErrorEx"/> with the same adapter and exception references.
	/// </summary>
	[TestMethod]
	public void RaiseConnectionErrorEx_FiresWithSameAdapterAndException()
	{
		var (connector, dispatcher) = Create();

		var adapter = new Mock<IMessageAdapter>().Object;
		var error = new InvalidOperationException("adapter connection failed");

		IMessageAdapter capturedAdapter = null;
		Exception capturedError = null;
		connector.ConnectionErrorEx += (a, e) => { capturedAdapter = a; capturedError = e; };

		dispatcher.RaiseConnectionErrorEx(adapter, error);

		capturedAdapter.AssertSame(adapter);
		capturedError.AssertSame(error);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseConnectionLost"/> must fire
	/// <see cref="Connector.ConnectionLost"/> with the same adapter instance.
	/// </summary>
	[TestMethod]
	public void RaiseConnectionLost_FiresWithSameAdapter()
	{
		var (connector, dispatcher) = Create();

		var adapter = new Mock<IMessageAdapter>().Object;
		IMessageAdapter captured = null;
		connector.ConnectionLost += a => captured = a;

		dispatcher.RaiseConnectionLost(adapter);

		captured.AssertSame(adapter);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseConnectionRestored"/> must fire
	/// <see cref="Connector.ConnectionRestored"/> with the same adapter instance.
	/// </summary>
	[TestMethod]
	public void RaiseConnectionRestored_FiresWithSameAdapter()
	{
		var (connector, dispatcher) = Create();

		var adapter = new Mock<IMessageAdapter>().Object;
		IMessageAdapter captured = null;
		connector.ConnectionRestored += a => captured = a;

		dispatcher.RaiseConnectionRestored(adapter);

		captured.AssertSame(adapter);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseCurrentTimeChanged"/> must fire
	/// <see cref="Connector.CurrentTimeChanged"/> exactly once with the supplied <see cref="TimeSpan"/>.
	/// </summary>
	[TestMethod]
	public void RaiseCurrentTimeChanged_FiresWithSameDiff()
	{
		var (connector, dispatcher) = Create();

		var count = 0;
		var captured = TimeSpan.MinValue;
		connector.CurrentTimeChanged += diff => { captured = diff; count++; };

		var expected = TimeSpan.FromSeconds(5);
		dispatcher.RaiseCurrentTimeChanged(expected);

		count.AssertEqual(1);
		captured.AssertEqual(expected);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseChangePassword"/> must fire
	/// <see cref="Connector.ChangePasswordResult"/> with the same transaction id and error reference.
	/// </summary>
	[TestMethod]
	public void RaiseChangePassword_FiresWithTransactionAndError()
	{
		var (connector, dispatcher) = Create();

		long capturedId = 0;
		Exception capturedError = null;
		connector.ChangePasswordResult += (id, error) => { capturedId = id; capturedError = error; };

		var pwdError = new InvalidOperationException("password rejected");
		dispatcher.RaiseChangePassword(42L, pwdError);

		capturedId.AssertEqual(42L);
		capturedError.AssertSame(pwdError);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseSubscriptionStarted"/> must fire
	/// <see cref="Connector.SubscriptionStarted"/> with the same <see cref="Subscription"/> instance.
	/// </summary>
	[TestMethod]
	public void RaiseSubscriptionStarted_FiresWithSameSubscription()
	{
		var (connector, dispatcher) = Create();
		var sub = CreateSubscription();

		Subscription captured = null;
		connector.SubscriptionStarted += s => captured = s;

		dispatcher.RaiseSubscriptionStarted(sub);

		captured.AssertSame(sub);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseSubscriptionOnline"/> must fire
	/// <see cref="Connector.SubscriptionOnline"/> with the same <see cref="Subscription"/> instance.
	/// </summary>
	[TestMethod]
	public void RaiseSubscriptionOnline_FiresWithSameSubscription()
	{
		var (connector, dispatcher) = Create();
		var sub = CreateSubscription();

		Subscription captured = null;
		connector.SubscriptionOnline += s => captured = s;

		dispatcher.RaiseSubscriptionOnline(sub);

		captured.AssertSame(sub);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseSubscriptionStopped"/> must fire
	/// <see cref="Connector.SubscriptionStopped"/> with the same subscription and error references.
	/// </summary>
	[TestMethod]
	public void RaiseSubscriptionStopped_FiresWithSubscriptionAndError()
	{
		var (connector, dispatcher) = Create();
		var sub = CreateSubscription();
		var error = new InvalidOperationException("subscription stopped");

		Subscription capturedSub = null;
		Exception capturedError = null;
		connector.SubscriptionStopped += (s, e) => { capturedSub = s; capturedError = e; };

		dispatcher.RaiseSubscriptionStopped(sub, error);

		capturedSub.AssertSame(sub);
		capturedError.AssertSame(error);
	}

	/// <summary>
	/// The compound <see cref="IConnectorEventDispatcher.RaiseMarketDataSubscriptionSucceeded"/> must, after
	/// logging the outcome, delegate to <c>RaiseSubscriptionStarted</c> and therefore fire
	/// <see cref="Connector.SubscriptionStarted"/> with the same <see cref="Subscription"/> instance.
	/// </summary>
	[TestMethod]
	public void RaiseMarketDataSubscriptionSucceeded_FiresSubscriptionStarted()
	{
		var (connector, dispatcher) = Create();
		var sub = CreateSubscription();

		Subscription captured = null;
		connector.SubscriptionStarted += s => captured = s;

		var message = new MarketDataMessage { DataType2 = DataType.Ticks };
		dispatcher.RaiseMarketDataSubscriptionSucceeded(message, sub);

		captured.AssertSame(sub);
	}

	/// <summary>
	/// Every façade event is fired through a null-conditional invoker hook, so invoking a representative
	/// range of <c>Raise*</c> methods with no subscribers attached must be a safe no-op that does not throw,
	/// while the connection-state side effects still apply.
	/// </summary>
	[TestMethod]
	public void Raise_NoSubscribers_DoesNotThrow()
	{
		var (connector, dispatcher) = Create();
		var sub = CreateSubscription();

		// No event handlers are attached. Each Raise* must complete without throwing.
		dispatcher.RaiseConnected();
		dispatcher.RaiseDisconnected();
		dispatcher.RaiseConnectionError(new InvalidOperationException("no-op"));
		dispatcher.RaiseConnectedEx(new Mock<IMessageAdapter>().Object);
		dispatcher.RaiseCurrentTimeChanged(TimeSpan.FromSeconds(1));
		dispatcher.RaiseSubscriptionStarted(sub);
		dispatcher.RaiseSubscriptionStopped(sub, null);
		dispatcher.RaiseChangePassword(1L, null);

		// Reaching this point proves the no-subscriber no-op contract. The last connection-state
		// transition (RaiseConnectionError) leaves the connector in the Failed state.
		connector.ConnectionState.AssertEqual(ConnectionStates.Failed);
	}

	/// <summary>
	/// A representative obsolete event (<see cref="Connector.NewOrder"/>) must still be fired by its
	/// <c>Raise*</c> method. The subscription to the obsolete event is wrapped in a pragma to suppress the
	/// expected obsolete-usage warning without affecting the rest of the file.
	/// </summary>
	[TestMethod]
	public void RaiseNewOrder_Obsolete_FiresNewOrder()
	{
		var (connector, dispatcher) = Create();

		var order = new Order();
		Order captured = null;

#pragma warning disable CS0618 // NewOrder is obsolete; superseded by OrderReceived. Exercised here for parity.
		connector.NewOrder += o => captured = o;
#pragma warning restore CS0618

		dispatcher.RaiseNewOrder(order);

		captured.AssertSame(order);
	}

	// ===== Ordering tests: the behavioral-parity assertion =====

	/// <summary>
	/// The behavioral-parity guard: when a fixed sequence of <c>Raise*</c> methods is invoked, the
	/// corresponding façade events must fire synchronously in the <em>exact same order</em> as the calls,
	/// with no reordering, batching or deferral. Downstream strategies depend on this ordering.
	/// </summary>
	/// <remarks>
	/// Each handler appends a distinct token to a shared list; the assertions compare that list against the
	/// expected sequence element-by-element (an ordered equality, not a set/contains check), so the test
	/// fails if the dispatch order ever changes.
	/// </remarks>
	[TestMethod]
	public void RaiseSequence_FiresEventsInExactOrder()
	{
		var (connector, dispatcher) = Create();
		var sub = CreateSubscription();

		var order = new List<string>();

		connector.Connected += () => order.Add("Connected");
		connector.CurrentTimeChanged += _ => order.Add("CurrentTimeChanged");
		connector.SubscriptionStarted += _ => order.Add("SubscriptionStarted");
		connector.Disconnected += () => order.Add("Disconnected");

		dispatcher.RaiseConnected();
		dispatcher.RaiseCurrentTimeChanged(TimeSpan.FromSeconds(1));
		dispatcher.RaiseSubscriptionStarted(sub);
		dispatcher.RaiseDisconnected();

		var expected = new[] { "Connected", "CurrentTimeChanged", "SubscriptionStarted", "Disconnected" };

		// Ordered, element-by-element equality — the parity guard.
		order.Count.AssertEqual(expected.Length);
		order[0].AssertEqual(expected[0]);
		order[1].AssertEqual(expected[1]);
		order[2].AssertEqual(expected[2]);
		order[3].AssertEqual(expected[3]);
		order.SequenceEqual(expected).AssertTrue();
	}

	/// <summary>
	/// The intra-method ordering guard for the compound <c>RaiseReceived</c> path: for each subscription the
	/// dispatcher invokes the supplied typed callback <em>before</em> firing
	/// <see cref="Connector.SubscriptionReceived"/>. This asserts the typed token is recorded before the
	/// generic subscription-received token, proving the firing order inside the method is preserved.
	/// </summary>
	[TestMethod]
	public void RaiseReceived_FiresTypedCallbackBeforeSubscriptionReceived()
	{
		var (connector, dispatcher) = Create();
		var sub = CreateSubscription();

		var order = new List<string>();

		connector.SubscriptionReceived += (s, arg) => order.Add("SubscriptionReceived");

		dispatcher.RaiseReceived<string>("payload", new[] { sub }, (s, entity) => order.Add("Typed"));

		var expected = new[] { "Typed", "SubscriptionReceived" };

		order.Count.AssertEqual(expected.Length);
		order[0].AssertEqual(expected[0]);
		order[1].AssertEqual(expected[1]);
		order.SequenceEqual(expected).AssertTrue();
	}

	// ===== Order-entity firing tests (legacy [Obsolete] events, exercised for parity) =====

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseNewMyTrade"/> must fire <see cref="Connector.NewMyTrade"/>
	/// exactly once with the very same <see cref="MyTrade"/> reference.
	/// </summary>
	[TestMethod]
	public void RaiseNewMyTrade_Obsolete_FiresNewMyTrade()
	{
		var (connector, dispatcher) = Create();

		var trade = new MyTrade();
		var count = 0;
		MyTrade captured = null;

#pragma warning disable CS0618 // NewMyTrade is obsolete; superseded by OwnTradeReceived. Exercised here for parity.
		connector.NewMyTrade += t => { captured = t; count++; };
#pragma warning restore CS0618

		dispatcher.RaiseNewMyTrade(trade);

		count.AssertEqual(1);
		captured.AssertSame(trade);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseOrderChanged"/> must fire <see cref="Connector.OrderChanged"/>
	/// exactly once with the very same <see cref="Order"/> reference.
	/// </summary>
	[TestMethod]
	public void RaiseOrderChanged_Obsolete_FiresOrderChanged()
	{
		var (connector, dispatcher) = Create();

		var order = new Order();
		var count = 0;
		Order captured = null;

#pragma warning disable CS0618 // OrderChanged is obsolete; superseded by OrderReceived. Exercised here for parity.
		connector.OrderChanged += o => { captured = o; count++; };
#pragma warning restore CS0618

		dispatcher.RaiseOrderChanged(order);

		count.AssertEqual(1);
		captured.AssertSame(order);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseOrderEdited"/> must fire <see cref="Connector.OrderEdited"/>
	/// with the same transaction id and the very same <see cref="Order"/> reference.
	/// </summary>
	[TestMethod]
	public void RaiseOrderEdited_Obsolete_FiresOrderEditedWithTransaction()
	{
		var (connector, dispatcher) = Create();

		var order = new Order();
		var count = 0;
		long capturedId = 0;
		Order captured = null;

#pragma warning disable CS0618 // OrderEdited is obsolete; superseded by OrderReceived. Exercised here for parity.
		connector.OrderEdited += (id, o) => { capturedId = id; captured = o; count++; };
#pragma warning restore CS0618

		dispatcher.RaiseOrderEdited(99L, order);

		count.AssertEqual(1);
		capturedId.AssertEqual(99L);
		captured.AssertSame(order);
	}

	// ===== Order-failure firing tests =====

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseOrderRegisterFailed"/> must fire
	/// <see cref="Connector.OrderRegisterFailed"/> with the very same <see cref="OrderFail"/> reference.
	/// </summary>
	[TestMethod]
	public void RaiseOrderRegisterFailed_Obsolete_FiresOrderRegisterFailed()
	{
		var (connector, dispatcher) = Create();

		var fail = new OrderFail { Order = new Order(), Error = new InvalidOperationException("register failed") };
		var count = 0;
		OrderFail captured = null;

#pragma warning disable CS0618 // OrderRegisterFailed is obsolete; superseded by OrderRegisterFailReceived. Exercised here for parity.
		connector.OrderRegisterFailed += f => { captured = f; count++; };
#pragma warning restore CS0618

		dispatcher.RaiseOrderRegisterFailed(1L, fail);

		count.AssertEqual(1);
		captured.AssertSame(fail);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseOrderCancelFailed"/> must fire
	/// <see cref="Connector.OrderCancelFailed"/> with the very same <see cref="OrderFail"/> reference.
	/// </summary>
	[TestMethod]
	public void RaiseOrderCancelFailed_Obsolete_FiresOrderCancelFailed()
	{
		var (connector, dispatcher) = Create();

		var fail = new OrderFail { Order = new Order(), Error = new InvalidOperationException("cancel failed") };
		var count = 0;
		OrderFail captured = null;

#pragma warning disable CS0618 // OrderCancelFailed is obsolete; superseded by OrderCancelFailReceived. Exercised here for parity.
		connector.OrderCancelFailed += f => { captured = f; count++; };
#pragma warning restore CS0618

		dispatcher.RaiseOrderCancelFailed(2L, fail);

		count.AssertEqual(1);
		captured.AssertSame(fail);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseOrderEditFailed"/> must fire
	/// <see cref="Connector.OrderEditFailed"/> with the same transaction id and <see cref="OrderFail"/> reference.
	/// </summary>
	[TestMethod]
	public void RaiseOrderEditFailed_Obsolete_FiresOrderEditFailedWithTransaction()
	{
		var (connector, dispatcher) = Create();

		var fail = new OrderFail { Order = new Order(), Error = new InvalidOperationException("edit failed") };
		var count = 0;
		long capturedId = 0;
		OrderFail captured = null;

#pragma warning disable CS0618 // OrderEditFailed is obsolete; superseded by OrderEditFailReceived. Exercised here for parity.
		connector.OrderEditFailed += (id, f) => { capturedId = id; captured = f; count++; };
#pragma warning restore CS0618

		dispatcher.RaiseOrderEditFailed(3L, fail);

		count.AssertEqual(1);
		capturedId.AssertEqual(3L);
		captured.AssertSame(fail);
	}

	/// <summary>
	/// The base <see cref="IConnectorEventDispatcher.RaiseOrderFailed"/> helper — to which the three typed
	/// order-failure raisers delegate — must invoke the supplied <c>failed</c> callback exactly once with the
	/// same transaction id and <see cref="OrderFail"/> reference (after logging the failure).
	/// </summary>
	[TestMethod]
	public void RaiseOrderFailed_InvokesSuppliedFailedCallback()
	{
		var (_, dispatcher) = Create();

		var fail = new OrderFail { Order = new Order(), Error = new InvalidOperationException("generic failure") };
		var count = 0;
		long capturedId = 0;
		OrderFail captured = null;

		dispatcher.RaiseOrderFailed("UnitTestFailure", 55L, fail, (id, f) => { capturedId = id; captured = f; count++; });

		count.AssertEqual(1);
		capturedId.AssertEqual(55L);
		captured.AssertSame(fail);
	}

	// ===== Mass-cancellation firing tests (each raiser fires BOTH its legacy and its timestamped event) =====

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseMassOrderCanceled"/> must fire <em>both</em>
	/// <see cref="Connector.MassOrderCanceled"/> and <see cref="Connector.MassOrderCanceled2"/>, in that order,
	/// each once, with the same transaction id and (for the timestamped variant) the same time.
	/// </summary>
	[TestMethod]
	public void RaiseMassOrderCanceled_FiresBothCanceledEventsInOrder()
	{
		var (connector, dispatcher) = Create();

		var fired = new List<string>();
		long id1 = 0, id2 = 0;
		var capturedTime = default(DateTime);

		connector.MassOrderCanceled += id => { fired.Add("MassOrderCanceled"); id1 = id; };
		connector.MassOrderCanceled2 += (id, time) => { fired.Add("MassOrderCanceled2"); id2 = id; capturedTime = time; };

		var when = DateTime.UtcNow;
		dispatcher.RaiseMassOrderCanceled(77L, when);

		id1.AssertEqual(77L);
		id2.AssertEqual(77L);
		capturedTime.AssertEqual(when);
		fired.SequenceEqual(["MassOrderCanceled", "MassOrderCanceled2"]).AssertTrue();
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseMassOrderCancelFailed"/> must fire <em>both</em>
	/// <see cref="Connector.MassOrderCancelFailed"/> and <see cref="Connector.MassOrderCancelFailed2"/>, in that
	/// order, with the same transaction id, the same <see cref="Exception"/> reference, and the same time.
	/// </summary>
	[TestMethod]
	public void RaiseMassOrderCancelFailed_FiresBothFailedEventsInOrder()
	{
		var (connector, dispatcher) = Create();

		var fired = new List<string>();
		long id1 = 0, id2 = 0;
		Exception err1 = null, err2 = null;
		var capturedTime = default(DateTime);

		connector.MassOrderCancelFailed += (id, error) => { fired.Add("MassOrderCancelFailed"); id1 = id; err1 = error; };
		connector.MassOrderCancelFailed2 += (id, error, time) => { fired.Add("MassOrderCancelFailed2"); id2 = id; err2 = error; capturedTime = time; };

		var failure = new InvalidOperationException("mass cancel failed");
		var when = DateTime.UtcNow;
		dispatcher.RaiseMassOrderCancelFailed(88L, failure, when);

		id1.AssertEqual(88L);
		id2.AssertEqual(88L);
		err1.AssertSame(failure);
		err2.AssertSame(failure);
		capturedTime.AssertEqual(when);
		fired.SequenceEqual(["MassOrderCancelFailed", "MassOrderCancelFailed2"]).AssertTrue();
	}

	// ===== Portfolio / position firing tests (legacy [Obsolete] events) =====

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseNewPortfolio"/> must fire <see cref="Connector.NewPortfolio"/>
	/// exactly once with the very same <see cref="Portfolio"/> reference.
	/// </summary>
	[TestMethod]
	public void RaiseNewPortfolio_Obsolete_FiresNewPortfolio()
	{
		var (connector, dispatcher) = Create();

		var portfolio = new Portfolio();
		var count = 0;
		Portfolio captured = null;

#pragma warning disable CS0618 // NewPortfolio is obsolete; superseded by PortfolioReceived. Exercised here for parity.
		connector.NewPortfolio += p => { captured = p; count++; };
#pragma warning restore CS0618

		dispatcher.RaiseNewPortfolio(portfolio);

		count.AssertEqual(1);
		captured.AssertSame(portfolio);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaisePortfolioChanged"/> must fire
	/// <see cref="Connector.PortfolioChanged"/> exactly once with the very same <see cref="Portfolio"/> reference.
	/// </summary>
	[TestMethod]
	public void RaisePortfolioChanged_Obsolete_FiresPortfolioChanged()
	{
		var (connector, dispatcher) = Create();

		var portfolio = new Portfolio();
		var count = 0;
		Portfolio captured = null;

#pragma warning disable CS0618 // PortfolioChanged is obsolete; superseded by PortfolioReceived. Exercised here for parity.
		connector.PortfolioChanged += p => { captured = p; count++; };
#pragma warning restore CS0618

		dispatcher.RaisePortfolioChanged(portfolio);

		count.AssertEqual(1);
		captured.AssertSame(portfolio);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseNewPosition"/> must fire <see cref="Connector.NewPosition"/>
	/// exactly once with the very same <see cref="Position"/> reference.
	/// </summary>
	[TestMethod]
	public void RaiseNewPosition_Obsolete_FiresNewPosition()
	{
		var (connector, dispatcher) = Create();

		var position = new Position();
		var count = 0;
		Position captured = null;

#pragma warning disable CS0618 // NewPosition is obsolete; superseded by PositionReceived. Exercised here for parity.
		connector.NewPosition += p => { captured = p; count++; };
#pragma warning restore CS0618

		dispatcher.RaiseNewPosition(position);

		count.AssertEqual(1);
		captured.AssertSame(position);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaisePositionChanged"/> must fire
	/// <see cref="Connector.PositionChanged"/> exactly once with the very same <see cref="Position"/> reference.
	/// </summary>
	[TestMethod]
	public void RaisePositionChanged_Obsolete_FiresPositionChanged()
	{
		var (connector, dispatcher) = Create();

		var position = new Position();
		var count = 0;
		Position captured = null;

#pragma warning disable CS0618 // PositionChanged is obsolete; superseded by PositionReceived. Exercised here for parity.
		connector.PositionChanged += p => { captured = p; count++; };
#pragma warning restore CS0618

		dispatcher.RaisePositionChanged(position);

		count.AssertEqual(1);
		captured.AssertSame(position);
	}

	// ===== Lookup-result firing tests (each raiser fires BOTH its legacy and its "2" event) =====

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseLookupSecuritiesResult"/> must fire <em>both</em>
	/// <see cref="Connector.LookupSecuritiesResult"/> and <see cref="Connector.LookupSecuritiesResult2"/>, in
	/// that order, propagating the same message, securities and error references; the "2" event receives an
	/// empty "already online" collection.
	/// </summary>
	[TestMethod]
	public void RaiseLookupSecuritiesResult_FiresBothResultEventsInOrder()
	{
		var (connector, dispatcher) = Create();

		var message = new SecurityLookupMessage();
		var securities = new[] { new Security() };
		var error = new InvalidOperationException("lookup failed");

		var fired = new List<string>();
		SecurityLookupMessage msg1 = null, msg2 = null;
		IEnumerable<Security> newSecs1 = null, newSecs2 = null, online2 = null;
		Exception err1 = null, err2 = null;

#pragma warning disable CS0618 // LookupSecuritiesResult(2) are obsolete; superseded by SecurityReceived/SubscriptionStopped. Exercised for parity.
		connector.LookupSecuritiesResult += (m, secs, e) => { fired.Add("LookupSecuritiesResult"); msg1 = m; newSecs1 = secs; err1 = e; };
		connector.LookupSecuritiesResult2 += (m, online, secs, e) => { fired.Add("LookupSecuritiesResult2"); msg2 = m; online2 = online; newSecs2 = secs; err2 = e; };
#pragma warning restore CS0618

		dispatcher.RaiseLookupSecuritiesResult(message, error, securities);

		msg1.AssertSame(message);
		msg2.AssertSame(message);
		newSecs1.AssertSame(securities);
		newSecs2.AssertSame(securities);
		err1.AssertSame(error);
		err2.AssertSame(error);
		online2.Count().AssertEqual(0);
		fired.SequenceEqual(["LookupSecuritiesResult", "LookupSecuritiesResult2"]).AssertTrue();
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseLookupPortfoliosResult"/> must fire <em>both</em>
	/// <see cref="Connector.LookupPortfoliosResult"/> and <see cref="Connector.LookupPortfoliosResult2"/>, in
	/// that order, propagating the same message, portfolios and error references; the "2" event receives an
	/// empty "already online" collection.
	/// </summary>
	[TestMethod]
	public void RaiseLookupPortfoliosResult_FiresBothResultEventsInOrder()
	{
		var (connector, dispatcher) = Create();

		var message = new PortfolioLookupMessage();
		var portfolios = new[] { new Portfolio() };
		var error = new InvalidOperationException("lookup failed");

		var fired = new List<string>();
		PortfolioLookupMessage msg1 = null, msg2 = null;
		IEnumerable<Portfolio> newPfs1 = null, newPfs2 = null, online2 = null;
		Exception err1 = null, err2 = null;

#pragma warning disable CS0618 // LookupPortfoliosResult(2) are obsolete; superseded by PortfolioReceived/SubscriptionStopped. Exercised for parity.
		connector.LookupPortfoliosResult += (m, pfs, e) => { fired.Add("LookupPortfoliosResult"); msg1 = m; newPfs1 = pfs; err1 = e; };
		connector.LookupPortfoliosResult2 += (m, online, pfs, e) => { fired.Add("LookupPortfoliosResult2"); msg2 = m; online2 = online; newPfs2 = pfs; err2 = e; };
#pragma warning restore CS0618

		dispatcher.RaiseLookupPortfoliosResult(message, error, portfolios);

		msg1.AssertSame(message);
		msg2.AssertSame(message);
		newPfs1.AssertSame(portfolios);
		newPfs2.AssertSame(portfolios);
		err1.AssertSame(error);
		err2.AssertSame(error);
		online2.Count().AssertEqual(0);
		fired.SequenceEqual(["LookupPortfoliosResult", "LookupPortfoliosResult2"]).AssertTrue();
	}

	// ===== Level1 values firing test =====

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseValuesChanged"/> must fire <see cref="Connector.ValuesChanged"/>
	/// exactly once, propagating the same security, changes collection and both timestamps by reference/value.
	/// </summary>
	[TestMethod]
	public void RaiseValuesChanged_FiresWithSecurityChangesAndTimes()
	{
		var (connector, dispatcher) = Create();

		var security = new Security();
		var changes = new[] { new KeyValuePair<Level1Fields, object>(Level1Fields.LastTradePrice, 100m) };
		var serverTime = DateTime.UtcNow;
		var localTime = serverTime.AddSeconds(1);

		var count = 0;
		Security capturedSec = null;
		IEnumerable<KeyValuePair<Level1Fields, object>> capturedChanges = null;
		var capturedServer = default(DateTime);
		var capturedLocal = default(DateTime);

		connector.ValuesChanged += (sec, ch, s, l) => { capturedSec = sec; capturedChanges = ch; capturedServer = s; capturedLocal = l; count++; };

		dispatcher.RaiseValuesChanged(security, changes, serverTime, localTime);

		count.AssertEqual(1);
		capturedSec.AssertSame(security);
		capturedChanges.AssertSame(changes);
		capturedServer.AssertEqual(serverTime);
		capturedLocal.AssertEqual(localTime);
	}

	// ===== Market-data lifecycle firing tests (compound raisers that delegate to Subscription* events) =====

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseMarketDataSubscriptionFailed"/> must, after logging, mark the
	/// subscription as failed on the <em>subscribe</em> side — firing <see cref="Connector.SubscriptionFailed"/>
	/// with the same subscription, the reply's error reference, and <c>isSubscribe == true</c>.
	/// </summary>
	[TestMethod]
	public void RaiseMarketDataSubscriptionFailed_FiresSubscriptionFailedForSubscribe()
	{
		var (connector, dispatcher) = Create();
		var sub = CreateSubscription();

		var error = new InvalidOperationException("subscribe rejected");
		var origin = new MarketDataMessage { DataType2 = DataType.Ticks };
		var reply = new SubscriptionResponseMessage { Error = error };

		var count = 0;
		Subscription capturedSub = null;
		Exception capturedError = null;
		var capturedIsSubscribe = false;
		connector.SubscriptionFailed += (s, e, isSub) => { capturedSub = s; capturedError = e; capturedIsSubscribe = isSub; count++; };

		dispatcher.RaiseMarketDataSubscriptionFailed(origin, reply, sub);

		count.AssertEqual(1);
		capturedSub.AssertSame(sub);
		capturedError.AssertSame(error);
		capturedIsSubscribe.AssertTrue();
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseMarketDataUnSubscriptionSucceeded"/> must, after logging,
	/// delegate to <c>RaiseSubscriptionStopped</c> and therefore fire <see cref="Connector.SubscriptionStopped"/>
	/// with the same subscription and a <see langword="null"/> error.
	/// </summary>
	[TestMethod]
	public void RaiseMarketDataUnSubscriptionSucceeded_FiresSubscriptionStopped()
	{
		var (connector, dispatcher) = Create();
		var sub = CreateSubscription();

		var count = 0;
		Subscription capturedSub = null;
		Exception capturedError = new InvalidOperationException("sentinel");
		connector.SubscriptionStopped += (s, e) => { capturedSub = s; capturedError = e; count++; };

		var message = new MarketDataMessage { DataType2 = DataType.Ticks };
		dispatcher.RaiseMarketDataUnSubscriptionSucceeded(message, sub);

		count.AssertEqual(1);
		capturedSub.AssertSame(sub);
		capturedError.AssertNull();
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseMarketDataUnSubscriptionFailed"/> must, after logging, mark the
	/// subscription as failed on the <em>unsubscribe</em> side — firing <see cref="Connector.SubscriptionFailed"/>
	/// with the same subscription, the reply's error reference, and <c>isSubscribe == false</c>.
	/// </summary>
	[TestMethod]
	public void RaiseMarketDataUnSubscriptionFailed_FiresSubscriptionFailedForUnSubscribe()
	{
		var (connector, dispatcher) = Create();
		var sub = CreateSubscription();

		var error = new InvalidOperationException("unsubscribe rejected");
		var origin = new MarketDataMessage { DataType2 = DataType.Ticks };
		var reply = new SubscriptionResponseMessage { Error = error };

		var count = 0;
		Subscription capturedSub = null;
		Exception capturedError = null;
		var capturedIsSubscribe = true;
		connector.SubscriptionFailed += (s, e, isSub) => { capturedSub = s; capturedError = e; capturedIsSubscribe = isSub; count++; };

		dispatcher.RaiseMarketDataUnSubscriptionFailed(origin, reply, sub);

		count.AssertEqual(1);
		capturedSub.AssertSame(sub);
		capturedError.AssertSame(error);
		capturedIsSubscribe.AssertFalse();
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseMarketDataSubscriptionFinished"/> must, after logging, delegate
	/// to <c>RaiseSubscriptionStopped</c> and therefore fire <see cref="Connector.SubscriptionStopped"/> with the
	/// same subscription and a <see langword="null"/> error (a clean, expected end of stream).
	/// </summary>
	[TestMethod]
	public void RaiseMarketDataSubscriptionFinished_FiresSubscriptionStopped()
	{
		var (connector, dispatcher) = Create();
		var sub = CreateSubscription();

		var count = 0;
		Subscription capturedSub = null;
		Exception capturedError = new InvalidOperationException("sentinel");
		connector.SubscriptionStopped += (s, e) => { capturedSub = s; capturedError = e; count++; };

		dispatcher.RaiseMarketDataSubscriptionFinished(new SubscriptionFinishedMessage(), sub);

		count.AssertEqual(1);
		capturedSub.AssertSame(sub);
		capturedError.AssertNull();
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseMarketDataUnexpectedCancelled"/> must, after logging, delegate
	/// to <c>RaiseSubscriptionStopped</c> and therefore fire <see cref="Connector.SubscriptionStopped"/> with the
	/// same subscription and the same error reference (an unexpected, error-carrying end of stream).
	/// </summary>
	[TestMethod]
	public void RaiseMarketDataUnexpectedCancelled_FiresSubscriptionStoppedWithError()
	{
		var (connector, dispatcher) = Create();
		var sub = CreateSubscription();

		var error = new InvalidOperationException("unexpectedly cancelled");
		var message = new MarketDataMessage { DataType2 = DataType.Ticks };

		var count = 0;
		Subscription capturedSub = null;
		Exception capturedError = null;
		connector.SubscriptionStopped += (s, e) => { capturedSub = s; capturedError = e; count++; };

		dispatcher.RaiseMarketDataUnexpectedCancelled(message, error, sub);

		count.AssertEqual(1);
		capturedSub.AssertSame(sub);
		capturedError.AssertSame(error);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseMarketDataSubscriptionOnline"/> must, after logging, delegate
	/// to <c>RaiseSubscriptionOnline</c> and therefore fire <see cref="Connector.SubscriptionOnline"/> with the
	/// same subscription instance.
	/// </summary>
	[TestMethod]
	public void RaiseMarketDataSubscriptionOnline_FiresSubscriptionOnline()
	{
		var (connector, dispatcher) = Create();
		var sub = CreateSubscription();

		var count = 0;
		Subscription captured = null;
		connector.SubscriptionOnline += s => { captured = s; count++; };

		dispatcher.RaiseMarketDataSubscriptionOnline(sub);

		count.AssertEqual(1);
		captured.AssertSame(sub);
	}

	// ===== Message / received firing tests =====

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseNewMessage"/> must fire the synchronous
	/// <see cref="Connector.NewMessage"/> event <em>and</em> invoke the asynchronous
	/// <see cref="Connector.NewOutMessageAsync"/> handler, both with the very same <see cref="Message"/> reference,
	/// and complete the returned <see cref="ValueTask"/>.
	/// </summary>
	[TestMethod]
	public async Task RaiseNewMessage_FiresNewMessageAndNewOutMessageAsync()
	{
		var (connector, dispatcher) = Create();

		var message = new Level1ChangeMessage();
		var syncCount = 0;
		var asyncCount = 0;
		Message syncCaptured = null;
		Message asyncCaptured = null;

#pragma warning disable CS0618 // NewMessage is obsolete; superseded by the async out-message pipeline. Exercised here for parity.
		connector.NewMessage += m => { syncCaptured = m; syncCount++; };
#pragma warning restore CS0618
		connector.NewOutMessageAsync += (m, ct) => { asyncCaptured = m; asyncCount++; return default; };

		await dispatcher.RaiseNewMessage(message, CancellationToken);

		syncCount.AssertEqual(1);
		asyncCount.AssertEqual(1);
		syncCaptured.AssertSame(message);
		asyncCaptured.AssertSame(message);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseSubscriptionReceived"/> must fire
	/// <see cref="Connector.SubscriptionReceived"/> exactly once with the same subscription and payload references.
	/// </summary>
	[TestMethod]
	public void RaiseSubscriptionReceived_FiresWithSameSubscriptionAndArg()
	{
		var (connector, dispatcher) = Create();
		var sub = CreateSubscription();
		var payload = new object();

		var count = 0;
		Subscription capturedSub = null;
		object capturedArg = null;
		connector.SubscriptionReceived += (s, arg) => { capturedSub = s; capturedArg = arg; count++; };

		dispatcher.RaiseSubscriptionReceived(sub, payload);

		count.AssertEqual(1);
		capturedSub.AssertSame(sub);
		capturedArg.AssertSame(payload);
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseLevel1Received"/> must fire <see cref="Connector.Level1Received"/>
	/// exactly once with the same subscription and <see cref="Level1ChangeMessage"/> references.
	/// </summary>
	[TestMethod]
	public void RaiseLevel1Received_FiresWithSameSubscriptionAndMessage()
	{
		var (connector, dispatcher) = Create();
		var sub = CreateSubscription();
		var message = new Level1ChangeMessage();

		var count = 0;
		Subscription capturedSub = null;
		Level1ChangeMessage capturedMsg = null;
		connector.Level1Received += (s, m) => { capturedSub = s; capturedMsg = m; count++; };

		dispatcher.RaiseLevel1Received(sub, message);

		count.AssertEqual(1);
		capturedSub.AssertSame(sub);
		capturedMsg.AssertSame(message);
	}

	// ===== RaiseReceived overloads: message-resolved and any-can-online reporting =====

	/// <summary>
	/// The message-based <see cref="IConnectorEventDispatcher.RaiseReceived{TEntity}(TEntity, ISubscriptionIdMessage, Action{Subscription, TEntity})"/>
	/// overload must resolve the target subscriptions through the injected
	/// <see cref="IConnectorSubscriptionManager"/> and, for each, invoke the typed callback and fire
	/// <see cref="Connector.SubscriptionReceived"/>.
	/// </summary>
	[TestMethod]
	public void RaiseReceived_MessageBased_ResolvesSubscriptionsViaManager()
	{
		var sub = CreateSubscription();
		var smMock = new Mock<IConnectorSubscriptionManager>();
		smMock.Setup(m => m.GetSubscriptions(It.IsAny<ISubscriptionIdMessage>())).Returns([sub]);

		var (connector, dispatcher) = Create(smMock.Object);

		var typedCount = 0;
		var receivedCount = 0;
		Subscription typedSub = null;
		string typedEntity = null;
		connector.SubscriptionReceived += (s, arg) => receivedCount++;

		dispatcher.RaiseReceived("payload", (ISubscriptionIdMessage)new NewsMessage(), (s, e) => { typedSub = s; typedEntity = e; typedCount++; });

		typedCount.AssertEqual(1);
		receivedCount.AssertEqual(1);
		typedSub.AssertSame(sub);
		typedEntity.AssertEqual("payload");
		smMock.Verify(m => m.GetSubscriptions(It.IsAny<ISubscriptionIdMessage>()), Times.Once);
	}

	/// <summary>
	/// The message-based <c>RaiseReceived</c> overload that exposes <c>anyCanOnline</c> must report
	/// <see langword="true"/> when a resolved subscription is <see cref="SubscriptionStates.Active"/> and not
	/// history-only, while still invoking the typed callback and firing <see cref="Connector.SubscriptionReceived"/>.
	/// </summary>
	[TestMethod]
	public void RaiseReceived_MessageBased_ReportsAnyCanOnlineForActiveSubscription()
	{
		var sub = CreateSubscription();
		sub.State = SubscriptionStates.Active;

		var smMock = new Mock<IConnectorSubscriptionManager>();
		smMock.Setup(m => m.GetSubscriptions(It.IsAny<ISubscriptionIdMessage>())).Returns([sub]);

		var (connector, dispatcher) = Create(smMock.Object);

		var typedCount = 0;
		var receivedCount = 0;
		connector.SubscriptionReceived += (s, arg) => receivedCount++;

		dispatcher.RaiseReceived("payload", (ISubscriptionIdMessage)new NewsMessage(), (s, e) => typedCount++, out var anyCanOnline);

		typedCount.AssertEqual(1);
		receivedCount.AssertEqual(1);
		anyCanOnline.AssertEqual(true);
	}

	/// <summary>
	/// The subscriptions-based <c>RaiseReceived</c> overload that returns the aggregate must report
	/// <see langword="true"/> when at least one supplied subscription is already
	/// <see cref="SubscriptionStates.Online"/>, while still invoking the typed callback and firing
	/// <see cref="Connector.SubscriptionReceived"/> for each subscription.
	/// </summary>
	[TestMethod]
	public void RaiseReceived_SubscriptionsBased_ReturnsAnyOnlineForOnlineSubscription()
	{
		var (connector, dispatcher) = Create();
		var sub = CreateSubscription();
		sub.State = SubscriptionStates.Online;

		var typedCount = 0;
		var receivedCount = 0;
		connector.SubscriptionReceived += (s, arg) => receivedCount++;

		var anyOnline = dispatcher.RaiseReceived("payload", new[] { sub }, (s, e) => typedCount++, out var anyCanOnline);

		typedCount.AssertEqual(1);
		receivedCount.AssertEqual(1);
		anyOnline.AssertEqual(true);
	}

	// ===== Guard-clause tests: documented ArgumentNullException contracts =====

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseConnectionError"/> rejects a <see langword="null"/> exception
	/// with <see cref="ArgumentNullException"/> (the error is a required payload).
	/// </summary>
	[TestMethod]
	public void RaiseConnectionError_NullException_Throws()
	{
		var (_, dispatcher) = Create();

		ThrowsExactly<ArgumentNullException>(() => dispatcher.RaiseConnectionError(null));
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseSubscriptionStarted"/> rejects a <see langword="null"/>
	/// subscription with <see cref="ArgumentNullException"/>.
	/// </summary>
	[TestMethod]
	public void RaiseSubscriptionStarted_NullSubscription_Throws()
	{
		var (_, dispatcher) = Create();

		ThrowsExactly<ArgumentNullException>(() => dispatcher.RaiseSubscriptionStarted(null));
	}

	/// <summary>
	/// The subscriptions-based <c>RaiseReceived</c> overload rejects a <see langword="null"/> subscriptions
	/// sequence with <see cref="ArgumentNullException"/>.
	/// </summary>
	[TestMethod]
	public void RaiseReceived_NullSubscriptions_Throws()
	{
		var (_, dispatcher) = Create();

		ThrowsExactly<ArgumentNullException>(() => dispatcher.RaiseReceived<string>("payload", (IEnumerable<Subscription>)null, (s, e) => { }));
	}

	/// <summary>
	/// <see cref="IConnectorEventDispatcher.RaiseMarketDataSubscriptionFailed"/> rejects a <see langword="null"/>
	/// reply with <see cref="ArgumentNullException"/> (the reply is a required payload).
	/// </summary>
	[TestMethod]
	public void RaiseMarketDataSubscriptionFailed_NullReply_Throws()
	{
		var (_, dispatcher) = Create();
		var sub = CreateSubscription();
		var origin = new MarketDataMessage { DataType2 = DataType.Ticks };

		ThrowsExactly<ArgumentNullException>(() => dispatcher.RaiseMarketDataSubscriptionFailed(origin, null, sub));
	}

}
