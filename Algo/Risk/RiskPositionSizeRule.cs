namespace StockSharp.Algo.Risk;

/// <summary>
/// Risk-rule, tracking position size.
/// </summary>
/// <remarks>
/// Concrete risk rule that follows the universal risk-rule pattern: it filters incoming
/// <see cref="PositionChangeMessage"/> messages (type <see cref="MessageTypes.PositionChange"/>) and
/// extracts the monitored <see cref="PositionChangeTypes.CurrentValue"/>. Unlike the money-oriented rules
/// (PnL and commission), it deliberately applies no <c>IsMoney()</c> filter, so it evaluates position
/// changes rather than money changes. The signed <see cref="Position"/> threshold selects the behaviour:
/// a value of <c>0</c> disables the rule; a positive threshold is a long cap that activates when the
/// current value is &gt;= <see cref="Position"/>; a negative threshold is a short cap that activates when
/// the current value is &lt;= <see cref="Position"/>. When the rule activates, the configured
/// <see cref="Action"/> is enforced.
/// </remarks>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.PositionKey,
	Description = LocalizedStrings.RulePositionKey,
	GroupName = LocalizedStrings.PositionsKey)]
public class RiskPositionSizeRule : RiskRule
{
	private decimal _position;

	/// <summary>
	/// Position size.
	/// </summary>
	/// <remarks>
	/// Signed size threshold that arms the rule. A positive value defines a long cap (activates when the
	/// monitored value is &gt;= this threshold), a negative value defines a short cap (activates when the
	/// monitored value is &lt;= this threshold), and <c>0</c> disables the rule. Negative values are valid
	/// short caps and are not rejected. The setter ignores redundant assignments and refreshes the display
	/// label through <see cref="RiskRule.UpdateTitle"/>.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PositionKey,
		Description = LocalizedStrings.PositionSizeKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 0)]
	public decimal Position
	{
		get => _position;
		set
		{
			if (_position == value)
				return;

			_position = value;
			UpdateTitle();
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// Returns the configured <see cref="Position"/> threshold formatted as a string for use as the
	/// rule's display label.
	/// </remarks>
	protected override string GetTitle() => _position.To<string>();

	/// <inheritdoc />
	/// <remarks>
	/// Evaluates only <see cref="MessageTypes.PositionChange"/> messages; any other message type returns
	/// <see langword="false"/>. The message is treated as a <see cref="PositionChangeMessage"/> and its
	/// <see cref="PositionChangeTypes.CurrentValue"/> is read with <c>TryGetDecimal</c>; when that value is
	/// absent the rule returns <see langword="false"/>. No <c>IsMoney()</c> filter is applied, so position
	/// (not money) changes are assessed. When <see cref="Position"/> is <c>0</c> the rule is disabled and
	/// returns <see langword="false"/>. A positive threshold (long cap) activates when the current value is
	/// &gt;= <see cref="Position"/>; a negative threshold (short cap) activates when the current value is
	/// &lt;= <see cref="Position"/>.
	/// </remarks>
	public override bool ProcessMessage(Message message)
	{
		if (message.Type != MessageTypes.PositionChange)
			return false;

		var posMsg = (PositionChangeMessage)message;
		var currValue = posMsg.TryGetDecimal(PositionChangeTypes.CurrentValue);

		if (currValue == null)
			return false;

		if (Position == 0)
			return false; // No limit when Position is 0

		if (Position > 0)
			return currValue >= Position;
		else
			return currValue <= Position;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Persists the <see cref="Position"/> threshold under the <c>Position</c> key after the base rule
	/// saves its <see cref="Action"/>.
	/// </remarks>
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(Position), Position);
	}

	/// <inheritdoc />
	/// <remarks>
	/// Restores the <see cref="Position"/> threshold from the <c>Position</c> key after the base rule
	/// loads its <see cref="Action"/>.
	/// </remarks>
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Position = storage.GetValue<decimal>(nameof(Position));
	}
}
