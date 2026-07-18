namespace StockSharp.Algo.Risk;

/// <summary>
/// Risk-rule, tracking position lifetime.
/// </summary>
/// <remarks>
/// A stateful member of the universal risk-rule family, extended with a time dimension: rather than comparing a single
/// monitored value against a threshold, it measures how long each position stays open. Every open position is tracked
/// by a key of its <see cref="SecurityId"/> and portfolio name and mapped to the <see cref="Message.LocalTime"/> at
/// which the position was first observed open. A <see cref="PositionChangeMessage"/> whose
/// <see cref="PositionChangeTypes.CurrentValue"/> is <c>0</c> closes and untracks the position; the first non-zero
/// observation records its open time; a later observation of the same position activates the rule - and clears its
/// tracking - once the elapsed time reaches the limit (<c>LocalTime - openTime &gt;= Time</c>). Incoming
/// <see cref="MessageTypes.Time"/> messages sweep every tracked position and activate the rule when any has been open
/// for at least <see cref="Time"/>. On activation the configured <see cref="RiskRule.Action"/> is enforced downstream.
/// </remarks>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.PositionTimeKey,
	Description = LocalizedStrings.RulePositionTimeKey,
	GroupName = LocalizedStrings.PositionsKey)]
public class RiskPositionTimeRule : RiskRule
{
	private readonly Dictionary<(SecurityId secId, string pfName), DateTime> _posOpenTime = [];
	private TimeSpan _time;

	/// <summary>
	/// Position lifetime.
	/// </summary>
	/// <remarks>
	/// The maximum length of time a position may remain open before the rule activates. Must be non-negative; the setter
	/// rejects values less than <see cref="TimeSpan.Zero"/>.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TimeKey,
		Description = LocalizedStrings.PositionTimeDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 0)]
	public TimeSpan Time
	{
		get => _time;
		set
		{
			if (_time == value)
				return;

			if (value < TimeSpan.Zero)
				throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue);

			_time = value;
			UpdateTitle();
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// Returns the configured <see cref="Time"/> span formatted as text, used as the rule's display label.
	/// </remarks>
	protected override string GetTitle() => _time.To<string>();

	/// <inheritdoc />
	/// <remarks>
	/// Clears every tracked position open time after invoking the base reset, so no position remains under observation
	/// and age evaluation restarts cleanly; the configured <see cref="Time"/> is unaffected.
	/// </remarks>
	public override void Reset()
	{
		base.Reset();
		_posOpenTime.Clear();
	}

	/// <inheritdoc />
	/// <remarks>
	/// Maintains per-position open-time state and activates once a position has been open for at least
	/// <see cref="Time"/>. For a <see cref="MessageTypes.PositionChange"/> message the monitored value is read from
	/// <see cref="PositionChangeTypes.CurrentValue"/>: a missing value is ignored; a value of <c>0</c> marks the
	/// position closed, so its entry is removed and the rule does not activate; the first non-zero value seeds the open
	/// time from the message <see cref="Message.LocalTime"/> without activating; and a subsequent non-zero value
	/// activates the rule and clears the entry once <c>LocalTime - openTime &gt;= Time</c>, otherwise it leaves the
	/// entry in place. For a <see cref="MessageTypes.Time"/> message every tracked position is swept and those whose age
	/// has reached <see cref="Time"/> (<c>LocalTime - openTime &gt;= Time</c>) are collected and removed, activating the
	/// rule when at least one position was swept. Any other message type returns <see langword="false"/>.
	/// </remarks>
	public override bool ProcessMessage(Message message)
	{
		switch (message.Type)
		{
			case MessageTypes.PositionChange:
			{
				var posMsg = (PositionChangeMessage)message;
				var currValue = posMsg.TryGetDecimal(PositionChangeTypes.CurrentValue);

				if (currValue == null)
					return false;

				var key = (posMsg.SecurityId, posMsg.PortfolioName);

				if (currValue == 0)
				{
					_posOpenTime.Remove(key);
					return false;
				}

				if (!_posOpenTime.TryGetValue(key, out var openTime))
				{
					_posOpenTime.Add(key, posMsg.LocalTime);
					return false;
				}

				var diff = posMsg.LocalTime - openTime;

				if (diff < Time)
					return false;

				_posOpenTime.Remove(key);
				return true;
			}

			case MessageTypes.Time:
			{
				List<(SecurityId, string)> removingPos = null;

				foreach (var pair in _posOpenTime)
				{
					var diff = message.LocalTime - pair.Value;

					if (diff < Time)
						continue;

					removingPos ??= [];

					removingPos.Add(pair.Key);
				}

				removingPos?.ForEach(t => _posOpenTime.Remove(t));

				return removingPos != null;
			}
		}

		return false;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Calls the base implementation to persist the <see cref="RiskRule.Action"/>, then stores the configured
	/// <see cref="Time"/> under the <c>Time</c> key.
	/// </remarks>
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(Time), Time);
	}

	/// <inheritdoc />
	/// <remarks>
	/// Calls the base implementation to restore the <see cref="RiskRule.Action"/>, then reads the configured
	/// <see cref="Time"/> from the <c>Time</c> key.
	/// </remarks>
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Time = storage.GetValue<TimeSpan>(nameof(Time));
	}
}
