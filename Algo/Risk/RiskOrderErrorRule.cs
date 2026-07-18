namespace StockSharp.Algo.Risk;

/// <summary>
/// Risk-rule, tracking orders error count.
/// </summary>
/// <remarks>
/// A consecutive-failure risk rule (see <see cref="RiskRule"/> for the shared contract): it filters
/// <see cref="MessageTypes.Execution"/> messages and tracks the number of consecutive failed execution
/// messages rather than a cumulative total. An error-free execution report (<c>IsOk()</c>) that also carries
/// order information (<c>HasOrderInfo()</c>) and reports the <see cref="OrderStates.Active"/> state clears the
/// running streak; any other error-free execution leaves the streak unchanged. Every execution message that
/// is not error-free increments the streak, and the rule activates once the streak reaches or exceeds
/// <see cref="Count"/> (<c>current &gt;= Count</c>). On activation the configured
/// <see cref="RiskRule.Action"/> is enforced.
/// </remarks>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.OrderErrorKey,
	Description = LocalizedStrings.RiskOrderErrorKey,
	GroupName = LocalizedStrings.OrdersKey)]
public class RiskOrderErrorRule : RiskRule
{
	private int _current;

	private int _count;

	/// <summary>
	/// Error count.
	/// </summary>
	/// <remarks>
	/// The consecutive-failure threshold: the number of back-to-back failed executions required before the
	/// rule activates. Must be non-negative; the setter rejects negative values. Changing it refreshes the
	/// rule <see cref="RiskRule.Title"/> via <c>UpdateTitle()</c>. Because the running counter is
	/// pre-incremented before the comparison, a threshold of <c>0</c> or <c>1</c> activates on the first failure.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ErrorsKey,
		Description = LocalizedStrings.ErrorsCountKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 0)]
	public int Count
	{
		get => _count;
		set
		{
			if (_count == value)
				return;

			if (value < 0)
				throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue);

			_count = value;
			UpdateTitle();
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// Produces the short display label for the rule, which is the configured <see cref="Count"/> threshold
	/// rendered as text.
	/// </remarks>
	protected override string GetTitle() => Count.To<string>();

	/// <inheritdoc />
	/// <remarks>
	/// Calls the base reset and then clears only the runtime consecutive-failure counter, so streak tracking
	/// restarts from zero. The configured <see cref="Count"/> threshold and <see cref="RiskRule.Action"/> are retained.
	/// </remarks>
	public override void Reset()
	{
		base.Reset();

		_current = 0;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Only <see cref="MessageTypes.Execution"/> messages are considered; every other message type returns
	/// <see langword="false"/>. The message is treated as an <c>ExecutionMessage</c>. For an error-free result
	/// (<c>IsOk()</c>) that also carries order information (<c>HasOrderInfo()</c>) and reports the
	/// <see cref="OrderStates.Active"/> state, the consecutive-failure counter is reset to zero and the method
	/// returns <see langword="false"/>; any other error-free execution returns <see langword="false"/> without
	/// changing the counter. An execution that reports an error increments the counter and returns
	/// <see langword="true"/> once the running count reaches or exceeds <see cref="Count"/>
	/// (<c>current &gt;= Count</c>), activating the configured <see cref="RiskRule.Action"/>.
	/// </remarks>
	public override bool ProcessMessage(Message message)
	{
		if (message.Type != MessageTypes.Execution)
			return false;

		var execMsg = (ExecutionMessage)message;

		if (execMsg.IsOk())
		{
			if (execMsg.HasOrderInfo() && execMsg.OrderState == OrderStates.Active)
				_current = 0;

			return false;
		}

		return ++_current >= Count;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Persists the base settings and then stores the <see cref="Count"/> threshold under its own key.
	/// </remarks>
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(Count), Count);
	}

	/// <inheritdoc />
	/// <remarks>
	/// Loads the base settings and then restores the <see cref="Count"/> threshold from storage.
	/// </remarks>
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Count = storage.GetValue<int>(nameof(Count));
	}
}
