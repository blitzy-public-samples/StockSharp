namespace StockSharp.Algo.Risk;

/// <summary>
/// Risk-rule, tracking commission size.
/// </summary>
/// <remarks>
/// Follows the universal risk-rule pattern (see <see cref="RiskRule"/>): it filters money
/// <see cref="MessageTypes.PositionChange"/> messages, extracts the commission value reported by that
/// change (<see cref="PositionChangeTypes.Commission"/>), treats a zero configured <see cref="Commission"/>
/// limit as disabled, and compares the current reported value - not a running total - against the limit.
/// The sign of the limit selects the comparison direction: a positive limit acts as an upper bound
/// (activates when <c>value &gt;= Commission</c>), while a negative limit acts as a lower bound
/// (activates when <c>value &lt;= Commission</c>). When the rule activates, the configured
/// <see cref="RiskRule.Action"/> is enforced. This is distinct from
/// <see cref="RiskTransactionCommissionRule"/>, which accumulates commission from
/// <see cref="MessageTypes.Execution"/> messages instead of reading the position's currently reported value.
/// </remarks>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CommissionKey,
	Description = LocalizedStrings.RiskCommissionKey,
	GroupName = LocalizedStrings.PnLKey)]
public class RiskCommissionRule : RiskRule
{
	private decimal _commission;

	/// <summary>
	/// Commission size.
	/// </summary>
	/// <remarks>
	/// The commission limit that activates this rule. Its magnitude sets the threshold and its sign selects the
	/// comparison direction: a positive value is an upper bound and a negative value is a lower bound. A value of
	/// zero disables the rule. Changing this value refreshes the display <see cref="RiskRule.Title"/>.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CommissionKey,
		Description = LocalizedStrings.CommissionDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 0)]
	public decimal Commission
	{
		get => _commission;
		set
		{
			if (_commission == value)
				return;

			_commission = value;
			UpdateTitle();
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// Returns the configured <see cref="Commission"/> limit rendered as text, used as the rule's display label.
	/// </remarks>
	protected override string GetTitle() => _commission.To<string>();

	/// <inheritdoc />
	/// <remarks>
	/// Evaluation order:
	/// (1) if the configured <see cref="Commission"/> limit is zero the rule is disabled and returns
	/// <see langword="false"/> immediately - the zero check happens first;
	/// (2) only <see cref="MessageTypes.PositionChange"/> messages are considered, so any other message type
	/// returns <see langword="false"/>;
	/// (3) only money (portfolio-level PnL) position changes are considered, so a non-money change returns
	/// <see langword="false"/>;
	/// (4) the current commission value is read from <see cref="PositionChangeTypes.Commission"/>, and a
	/// missing value returns <see langword="false"/>.
	/// The current reported value is then compared against the limit: for a positive limit the rule activates
	/// when <c>value &gt;= Commission</c> (upper bound); for a negative limit it activates when
	/// <c>value &lt;= Commission</c> (lower bound).
	/// </remarks>
	public override bool ProcessMessage(Message message)
	{
		if (Commission == 0)
			return false; // No limit when Commission is 0

		if (message.Type != MessageTypes.PositionChange)
			return false;

		var pfMsg = (PositionChangeMessage)message;

		if (!pfMsg.IsMoney())
			return false;

		var currValue = pfMsg.TryGetDecimal(PositionChangeTypes.Commission);

		if (currValue == null)
			return false;

		// Handle both positive (upper bound) and negative (lower bound) commission limits
		if (Commission > 0)
			return currValue >= Commission;
		else
			return currValue <= Commission;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Persists the configured <see cref="Commission"/> limit after the base rule saves its
	/// <see cref="RiskRule.Action"/>.
	/// </remarks>
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(Commission), Commission);
	}

	/// <inheritdoc />
	/// <remarks>
	/// Restores the configured <see cref="Commission"/> limit after the base rule loads its
	/// <see cref="RiskRule.Action"/>.
	/// </remarks>
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Commission = storage.GetValue<decimal>(nameof(Commission));
	}
}
