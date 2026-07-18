namespace StockSharp.Algo.Risk;

/// <summary>
/// Risk-rule, tracking profit-loss.
/// </summary>
/// <remarks>
/// Follows the universal risk-rule pattern described on <see cref="RiskRule"/>, specialized for portfolio
/// profit-and-loss. It filters money <see cref="PositionChangeMessage"/> updates (<see cref="MessageTypes.PositionChange"/>)
/// and reads the current PnL from <see cref="PositionChangeTypes.CurrentValue"/>. The first observed value only seeds an
/// internal baseline and never triggers. The configured <see cref="PnL"/> threshold is a <see cref="Unit"/> whose sign
/// selects the direction: a positive threshold is a profit target that activates when the threshold value is
/// <c>&lt;=</c> the current PnL; a negative threshold is a loss limit that activates when the threshold value is
/// <c>&gt;=</c> the current PnL; and a threshold of <c>0</c> never triggers. An <see cref="UnitTypes.Absolute"/>
/// threshold is compared against the current PnL directly, whereas a relative threshold is offset from the seeded
/// baseline through <see cref="Unit"/> addition. On activation the configured <see cref="RiskRule.Action"/> fires.
/// </remarks>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.PnLKey,
	Description = LocalizedStrings.RulePnLKey,
	GroupName = LocalizedStrings.PnLKey)]
public class RiskPnLRule : RiskRule
{
	private decimal? _initValue;

	/// <inheritdoc />
	/// <remarks>
	/// Clears the seeded baseline (the first observed PnL) in addition to the base reset, so the next observation
	/// re-seeds the baseline. This is the stateful extension point that lets relative thresholds restart cleanly.
	/// </remarks>
	public override void Reset()
	{
		base.Reset();
		_initValue = null;
	}

	private Unit _pnL = new();

	/// <summary>
	/// Profit-loss.
	/// </summary>
	/// <remarks>
	/// The threshold expressed as a <see cref="Unit"/>. When its type is <see cref="UnitTypes.Absolute"/> the value is
	/// compared against the current PnL directly; otherwise it is treated as a relative offset added to the seeded
	/// baseline. The sign selects the direction: a positive value is a profit target, a negative value is a loss limit,
	/// and <c>0</c> disables the rule. The value must not be <see langword="null"/>; the setter throws
	/// <see cref="ArgumentNullException"/> otherwise.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PnLKey,
		Description = LocalizedStrings.PnLKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 0)]
	public Unit PnL
	{
		get => _pnL;
		set
		{
			if (_pnL == value)
				return;

			_pnL = value ?? throw new ArgumentNullException(nameof(value));
			UpdateTitle();
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// Returns the configured <see cref="PnL"/> threshold rendered as text, used as the rule's display label.
	/// </remarks>
	protected override string GetTitle() => _pnL?.To<string>();

	/// <inheritdoc />
	/// <remarks>
	/// Only money portfolio updates are considered: a message whose type is not
	/// <see cref="MessageTypes.PositionChange"/> is ignored, as is a <see cref="PositionChangeMessage"/> that is not
	/// money or that carries no <see cref="PositionChangeTypes.CurrentValue"/> (all return <see langword="false"/>).
	/// The first qualifying observation only seeds the internal baseline and returns <see langword="false"/>.
	/// Thereafter, for an <see cref="UnitTypes.Absolute"/> threshold a positive <see cref="PnL"/> activates when
	/// <c>PnL &lt;= current</c> (profit target reached) and a negative <see cref="PnL"/> activates when
	/// <c>PnL &gt;= current</c> (loss limit reached); for a relative threshold the seeded baseline is added first, so a
	/// positive <see cref="PnL"/> activates when <c>(baseline + PnL) &lt;= current</c> and a negative <see cref="PnL"/>
	/// activates when <c>(baseline + PnL) &gt;= current</c>. A threshold of <c>0</c> never activates in either mode.
	/// Returning <see langword="true"/> drives the configured <see cref="RiskRule.Action"/>.
	/// </remarks>
	public override bool ProcessMessage(Message message)
	{
		if (message.Type != MessageTypes.PositionChange)
			return false;

		var pfMsg = (PositionChangeMessage)message;

		if (!pfMsg.IsMoney())
			return false;

		var currValue = pfMsg.TryGetDecimal(PositionChangeTypes.CurrentValue);

		if (currValue == null)
			return false;

		if (_initValue == null)
		{
			_initValue = currValue.Value;
			return false;
		}

		if (PnL.Type == UnitTypes.Absolute)
		{
			if (PnL.Value > 0)
				return PnL.Value <= currValue.Value;
			else if (PnL.Value < 0)
				return PnL.Value >= currValue.Value;
			else
				return false; // PnL.Value == 0: never activate
		}

		if (PnL.Value > 0)
			return (_initValue + PnL) <= currValue.Value;
		else if (PnL.Value < 0)
			return (_initValue + PnL) >= currValue.Value;
		else
			return false; // PnL.Value == 0: never activate
	}

	/// <inheritdoc />
	/// <remarks>
	/// Persists the <see cref="PnL"/> threshold (as a <see cref="Unit"/>) after the base rule saves its
	/// <see cref="RiskRule.Action"/>.
	/// </remarks>
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(PnL), PnL);
	}

	/// <inheritdoc />
	/// <remarks>
	/// Restores the <see cref="PnL"/> threshold (as a <see cref="Unit"/>) after the base rule loads its
	/// <see cref="RiskRule.Action"/>.
	/// </remarks>
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		PnL = storage.GetValue<Unit>(nameof(PnL));
	}
}
