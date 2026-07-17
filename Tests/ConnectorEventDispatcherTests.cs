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
	private static (Connector connector, IConnectorEventDispatcher dispatcher) Create()
	{
		var connector = new Connector();

		IConnectorEventDispatcher dispatcher = new ConnectorEventDispatcher(connector, new Mock<IConnectorSubscriptionManager>().Object);

		return (connector, dispatcher);
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
}
