namespace StockSharp.Algo.Risk;

/// <summary>
/// Risk-rule, tracking orders placing frequency.
/// </summary>
/// <remarks>
/// A frequency-based risk rule: it filters <see cref="MessageTypes.OrderRegister"/> and
/// <see cref="MessageTypes.OrderReplace"/> messages and counts them within a first-event-anchored fixed
/// <see cref="Interval"/> window. The window is opened by the first order and anchored to that order's
/// <see cref="Message.LocalTime"/>; its end is fixed at <c>time + Interval</c> and does not slide forward as
/// further orders arrive. The rule activates once the running count reaches the configured <see cref="Count"/>
/// (default 10) while still inside the window (<c>count &gt;= Count</c>), at which point the window is reset. An
/// order that arrives at or after the window end (<c>time &gt;= endTime</c>) starts a fresh window instead of
/// triggering. On activation the configured <see cref="RiskRule.Action"/> is reported by the risk manager and
/// enforced downstream. The trigger is purely the count reaching the limit inside the fixed window; there is no
/// sign-based comparison direction.
/// </remarks>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.OrderFreqKey,
	Description = LocalizedStrings.RiskOrderFreqKey,
	GroupName = LocalizedStrings.OrdersKey)]
public class RiskOrderFreqRule : RiskRule
{
	private DateTime? _endTime;
	private int _current;

	/// <inheritdoc />
	/// <remarks>
	/// Produces the display label by combining the configured <see cref="Count"/> and <see cref="Interval"/>
	/// (rendered as "<c>Count -&gt; Interval</c>"), so the property grid reflects the current limit and window.
	/// </remarks>
	protected override string GetTitle() => Count + " -> " + Interval;

	private int _count = 10;

	/// <summary>
	/// Order count.
	/// </summary>
	/// <remarks>
	/// The maximum number of orders permitted within a single <see cref="Interval"/> window; the rule activates
	/// when the running count reaches this value (<c>count &gt;= Count</c>). Must be at least 1 (the setter rejects
	/// smaller values); the default is 10.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.OrdersCountKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 0)]
	public int Count
	{
		get => _count;
		set
		{
			if (_count == value)
				return;

			if (value < 1)
				throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue);

			_count = value;
			UpdateTitle();
		}
	}

	private TimeSpan _interval;


	/// <summary>
	/// Interval, during which orders quantity will be monitored.
	/// </summary>
	/// <remarks>
	/// The length of the first-event-anchored fixed time window over which orders are counted, measured from the
	/// <see cref="Message.LocalTime"/> of the order that opened the window. Must be non-negative
	/// (the setter rejects values below <see cref="TimeSpan.Zero"/>).
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.RiskIntervalDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 1)]
	public TimeSpan Interval
	{
		get => _interval;
		set
		{
			if (_interval == value)
				return;

			if (value < TimeSpan.Zero)
				throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue);

			_interval = value;
			UpdateTitle();
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// Calls the base reset and then clears the rule's running state: the accumulated order count is set back to
	/// zero and the open window is discarded, so the next qualifying order opens a brand-new window. The configured
	/// <see cref="Count"/> and <see cref="Interval"/> are left unchanged.
	/// </remarks>
	public override void Reset()
	{
		base.Reset();

		_current = 0;
		_endTime = null;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Evaluates only <see cref="MessageTypes.OrderRegister"/> and <see cref="MessageTypes.OrderReplace"/> messages;
	/// every other message type returns <see langword="false"/>. The message's <see cref="Message.LocalTime"/> is
	/// used as the clock: when it is unset (<c>default</c>) the message is ignored and the method returns
	/// <see langword="false"/> (no-timestamp guard). Window handling proceeds as follows:
	/// <list type="bullet">
	/// <item>No open window: a new window is opened with its end at <c>time + Interval</c>, the count is set to 1,
	/// and the method returns <see langword="false"/>.</item>
	/// <item>Inside the current window (<c>time &lt; endTime</c>): the count is incremented; if it reaches the limit
	/// (<c>count &gt;= Count</c>) the window is closed and the method returns <see langword="true"/> (the rule
	/// activates); otherwise it returns <see langword="false"/>.</item>
	/// <item>Window elapsed: a fresh window is started (end at <c>time + Interval</c>, count reset to 1) and the
	/// method returns <see langword="false"/>.</item>
	/// </list>
	/// A <see langword="true"/> result drives the configured <see cref="RiskRule.Action"/>.
	/// </remarks>
	public override bool ProcessMessage(Message message)
	{
		switch (message.Type)
		{
			case MessageTypes.OrderRegister:
			case MessageTypes.OrderReplace:
			{
				var time = message.LocalTime;

				if (time == default)
				{
					//LogWarning("Time is null. Msg={0}", message);
					return false;
				}

				if (_endTime == null)
				{
					_endTime = time + Interval;
					_current = 1;

					//LogDebug("EndTime={0}", _endTime);
					return false;
				}

				if (time < _endTime)
				{
					_current++;

					//LogDebug("Count={0} Msg={1}", _current, message);

					if (_current >= Count)
					{
						//LogInfo("Count={0} EndTime={1}", _current, _endTime);

						_endTime = null;
						return true;
					}
				}
				else
				{
					_endTime = time + Interval;
					_current = 1;

					//LogDebug("EndTime={0}", _endTime);
				}

				return false;
			}
		}

		return false;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Calls the base implementation (which persists <see cref="RiskRule.Action"/>) and then stores this rule's
	/// own configuration: <see cref="Count"/> and <see cref="Interval"/>.
	/// </remarks>
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(Count), Count);
		storage.SetValue(nameof(Interval), Interval);
	}

	/// <inheritdoc />
	/// <remarks>
	/// Calls the base implementation (which restores <see cref="RiskRule.Action"/>) and then reads this rule's
	/// own configuration back: <see cref="Count"/> and <see cref="Interval"/>.
	/// </remarks>
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Count = storage.GetValue<int>(nameof(Count));
		Interval = storage.GetValue<TimeSpan>(nameof(Interval));
	}
}
