namespace StockSharp.Algo.Risk;

/// <summary>
/// Risk-rule, tracking orders execution frequency.
/// </summary>
/// <remarks>
/// Limits the frequency of own trades (executions). It counts <see cref="ExecutionMessage"/> messages that
/// carry trade information (<c>HasTradeInfo()</c>) and activates when that running count reaches
/// <see cref="Count"/> within a sliding <see cref="Interval"/> time window. Only
/// <see cref="MessageTypes.Execution"/> messages representing own-trade fills are considered; all other
/// message types are ignored. The first counted trade opens a window ending at <c>LocalTime + Interval</c>;
/// each further trade arriving before the window closes increments the running count, and once
/// <c>current &gt;= Count</c> (default 10) the rule fires, closes the current window, and begins counting
/// afresh on the next trade. A trade arriving after the window has elapsed simply starts a new window.
/// When the rule activates, the configured <see cref="RiskRule.Action"/> is enforced.
/// </remarks>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.TradeFreqKey,
	Description = LocalizedStrings.RiskTradeFreqKey,
	GroupName = LocalizedStrings.TradesKey)]
public class RiskTradeFreqRule : RiskRule
{
	private DateTime? _endTime;
	private int _current;

	/// <inheritdoc />
	/// <remarks>
	/// Produces the display label by combining the configured <see cref="Count"/> and <see cref="Interval"/>
	/// (formatted as <c>Count -&gt; Interval</c>).
	/// </remarks>
	protected override string GetTitle() => Count + " -> " + Interval;

	private int _count = 10;

	/// <summary>
	/// Number of trades.
	/// </summary>
	/// <remarks>
	/// Maximum number of own trades permitted within a single <see cref="Interval"/> window before the rule
	/// activates. Must be at least 1 (the setter rejects lower values); defaults to 10.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.LimitOrderTifKey,
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
	/// Interval, during which trades quantity will be monitored.
	/// </summary>
	/// <remarks>
	/// Length of the sliding time window over which trades are counted. Must be non-negative (the setter
	/// rejects values below <c>TimeSpan.Zero</c>).
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.TradesIntervalKey,
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
	/// Clears the sliding-window state after invoking the base reset: the running trade count is set back to
	/// zero and the open window is discarded, so counting restarts on the next qualifying trade.
	/// </remarks>
	public override void Reset()
	{
		base.Reset();

		_current = 0;
		_endTime = null;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Evaluated for every incoming <see cref="Message"/>. Messages whose type is not
	/// <see cref="MessageTypes.Execution"/> are ignored, as are execution messages that do not carry trade
	/// information (<c>HasTradeInfo()</c> is <see langword="false"/>) and messages with no local time
	/// (default timestamp). For a qualifying own trade: when no window is open the trade opens one ending at
	/// <c>LocalTime + Interval</c> and sets the running count to 1; when the trade falls within the current
	/// window the running count is incremented and, as soon as <c>current &gt;= Count</c>, the rule closes the
	/// window and returns <see langword="true"/> to activate; when the trade falls on or after the window end
	/// a fresh window is opened with a running count of 1. All non-activating paths return
	/// <see langword="false"/>.
	/// </remarks>
	public override bool ProcessMessage(Message message)
	{
		if (message.Type != MessageTypes.Execution)
			return false;

		var execMsg = (ExecutionMessage)message;

		if (!execMsg.HasTradeInfo())
			return false;

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

	/// <inheritdoc />
	/// <remarks>
	/// Persists this rule's configuration after the base settings: the <see cref="Count"/> and
	/// <see cref="Interval"/> values are written to the supplied storage.
	/// </remarks>
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(Count), Count);
		storage.SetValue(nameof(Interval), Interval);
	}

	/// <inheritdoc />
	/// <remarks>
	/// Restores this rule's configuration after the base settings: the <see cref="Count"/> and
	/// <see cref="Interval"/> values are read back from the supplied storage.
	/// </remarks>
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Count = storage.GetValue<int>(nameof(Count));
		Interval = storage.GetValue<TimeSpan>(nameof(Interval));
	}
}
