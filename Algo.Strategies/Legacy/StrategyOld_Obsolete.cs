namespace StockSharp.Algo.Strategies;

// This file is the obsolete-surface fragment of the legacy StrategyOld engine: it
// gathers the members kept only for backward compatibility, whose old-to-new migration
// map is encoded in the [Obsolete] attribute messages below (and restated in plain
// language in each member's XML <remarks>). Every member here is superseded by the
// modern Strategy engine and must not be used by new code. This is a documentation-only
// fragment; the canonical type <summary> for StrategyOld lives in StrategyOld.cs.
partial class StrategyOld
{
	/// <summary>
	/// Subsidiary trade strategies.
	/// </summary>
	/// <remarks>
	/// Legacy surface retained only for binary/source compatibility. Child strategies are
	/// no longer supported by the modern <see cref="Strategy"/> engine; the property is
	/// non-browsable and must not be used by new code.
	/// </remarks>
	[Browsable(false)]
	[Obsolete("Child strategies no longer supported.")]
	public INotifyList<StrategyOld> ChildStrategies { get; } = new SynchronizedList<StrategyOld>();

	/// <summary>
	/// The event of order successful registration.
	/// </summary>
	/// <remarks>
	/// Legacy event retained for backward compatibility; superseded by the <c>OrderReceived</c>
	/// event on the modern <see cref="Strategy"/> engine.
	/// </remarks>
	[Obsolete("Use OrderReceived event.")]
	public event Action<Order> OrderRegistered;

	/// <inheritdoc />
	/// <remarks>
	/// Legacy event retained for backward compatibility; superseded by the <c>OrderRegisterFailReceived</c>
	/// event on the modern <see cref="Strategy"/> engine.
	/// </remarks>
	[Obsolete("Use OrderRegisterFailReceived event.")]
	public event Action<OrderFail> OrderRegisterFailed;

	/// <inheritdoc />
	/// <remarks>
	/// Legacy event retained for backward compatibility; superseded by the <c>OrderCancelFailReceived</c>
	/// event on the modern <see cref="Strategy"/> engine.
	/// </remarks>
	[Obsolete("Use OrderCancelFailReceived event.")]
	public event Action<OrderFail> OrderCancelFailed;

	/// <inheritdoc />
	/// <remarks>
	/// Legacy event retained for backward compatibility; superseded by the <c>OrderReceived</c>
	/// event on the modern <see cref="Strategy"/> engine.
	/// </remarks>
	[Obsolete("Use OrderReceived event.")]
	public event Action<Order> OrderChanged;

	/// <inheritdoc />
	/// <remarks>
	/// Legacy event retained for backward compatibility; superseded by the <c>OrderReceived</c>
	/// event on the modern <see cref="Strategy"/> engine.
	/// </remarks>
	[Obsolete("Use OrderReceived event.")]
	public event Action<long, Order> OrderEdited;

	/// <inheritdoc />
	/// <remarks>
	/// Legacy event retained for backward compatibility; superseded by the <c>OrderEditFailReceived</c>
	/// event on the modern <see cref="Strategy"/> engine.
	/// </remarks>
	[Obsolete("Use OrderEditFailReceived event.")]
	public event Action<long, OrderFail> OrderEditFailed;

	/// <inheritdoc />
	/// <remarks>
	/// Legacy event retained for backward compatibility; superseded by the <c>OwnTradeReceived</c>
	/// event on the modern <see cref="Strategy"/> engine.
	/// </remarks>
	[Obsolete("Use OwnTradeReceived event.")]
	public event Action<MyTrade> NewMyTrade;

	/// <summary>
	/// <see cref="PnL"/> change event.
	/// </summary>
	/// <remarks>
	/// Legacy event retained for backward compatibility; superseded by the <c>PnLReceived2</c>
	/// event on the modern <see cref="Strategy"/> engine.
	/// </remarks>
	[Obsolete("Use PnLReceived2 event.")]
	public event Action<Subscription> PnLReceived;

	/// <summary>
	/// The method is called when the <see cref="Start()"/> method has been called and the <see cref="ProcessState"/> state has been taken the <see cref="ProcessStates.Started"/> value.
	/// </summary>
	/// <remarks>
	/// Legacy hook retained for backward compatibility; it simply forwards to the
	/// time-parameterized overload by calling <c>OnStarted2(CurrentTime)</c>. New code should
	/// override the overload that accepts the start time instead of this parameterless method.
	/// </remarks>
	[Obsolete("Use overload with time param.")]
	protected virtual void OnStarted()
	{
		OnStarted2(CurrentTime);
	}
}