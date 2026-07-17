namespace StockSharp.Algo.Risk;

/// <summary>
/// Risk-rule, tracking error count.
/// </summary>
/// <remarks>
/// Concrete <see cref="RiskRule"/> that counts error notifications. Following the universal risk-rule
/// pattern it filters <see cref="MessageTypes.Error"/> messages, maintains a cumulative counter across
/// processed messages, and activates once that counter reaches or exceeds the configured
/// <see cref="Count"/> threshold (evaluated as <c>++current &gt;= Count</c>, incrementing before the
/// comparison). On activation the configured <see cref="RiskRule.Action"/> is enforced. The running
/// counter is cleared by <see cref="Reset"/>; the threshold itself is persisted through
/// <see cref="Save"/> and <see cref="Load"/>.
/// </remarks>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ErrorKey,
	Description = LocalizedStrings.RiskErrorKey,
	GroupName = LocalizedStrings.StrategyKey)]
public class RiskErrorRule : RiskRule
{
	private int _current;

	private int _count;

	/// <summary>
	/// Error count.
	/// </summary>
	/// <remarks>
	/// The number of error messages that must accumulate before the rule activates. Must be non-negative —
	/// the setter rejects negative values with <see cref="ArgumentOutOfRangeException"/>, ignores redundant
	/// assignments, and refreshes the display <see cref="RiskRule.Title"/>.
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
	/// Returns the configured <see cref="Count"/> threshold as the rule's display label.
	/// </remarks>
	protected override string GetTitle() => Count.To<string>();

	/// <inheritdoc />
	/// <remarks>
	/// Clears the running error counter back to zero after invoking the base reset, leaving the configured
	/// <see cref="Count"/> intact so the rule can be reused for a fresh evaluation window.
	/// </remarks>
	public override void Reset()
	{
		base.Reset();

		_current = 0;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Ignores every message that is not an <see cref="MessageTypes.Error"/> message (returning
	/// <see langword="false"/>). For each error message it increments the cumulative counter and returns
	/// <see langword="true"/> once the counter reaches or exceeds <see cref="Count"/> (evaluated as
	/// <c>++current &gt;= Count</c>).
	/// </remarks>
	public override bool ProcessMessage(Message message)
	{
		if (message.Type != MessageTypes.Error)
			return false;

		return ++_current >= Count;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Persists the <see cref="Count"/> threshold after the base implementation stores the shared
	/// <see cref="RiskRule.Action"/>.
	/// </remarks>
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(Count), Count);
	}

	/// <inheritdoc />
	/// <remarks>
	/// Restores the <see cref="Count"/> threshold after the base implementation loads the shared
	/// <see cref="RiskRule.Action"/>.
	/// </remarks>
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Count = storage.GetValue<int>(nameof(Count));
	}
}