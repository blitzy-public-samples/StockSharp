namespace StockSharp.Algo.Risk;

/// <summary>
/// Risk-rule, tracking slippage size.
/// </summary>
/// <remarks>
/// Follows the universal risk-rule pattern: it filters incoming <see cref="MessageTypes.Execution"/>
/// messages, reads the reported <see cref="ExecutionMessage.Slippage"/>, and compares it against the
/// configured <see cref="Slippage"/> threshold. A threshold of <c>0</c> disables the rule. A positive
/// threshold is a strict upper bound: the rule activates when the actual slippage is strictly greater
/// (<c>&gt;</c>) than the threshold. A negative threshold is a strict lower bound: the rule activates
/// when the actual slippage is strictly less (<c>&lt;</c>) than the threshold. Unlike the price, volume,
/// and commission rules, which use an inclusive (<c>&gt;=</c>) comparison, this rule uses a strict
/// (<c>&gt;</c>/<c>&lt;</c>) comparison, so a slippage exactly equal to the threshold does not activate
/// it. When the rule activates, the configured <see cref="RiskRule.Action"/> is applied.
/// </remarks>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SlippageKey,
	Description = LocalizedStrings.RiskSlippageKey,
	GroupName = LocalizedStrings.OrdersKey)]
public class RiskSlippageRule : RiskRule
{
	private decimal _slippage;

	/// <summary>
	/// Slippage size.
	/// </summary>
	/// <remarks>
	/// Signed slippage threshold, expressed in the same units as <see cref="ExecutionMessage.Slippage"/>.
	/// A positive value is a strict upper bound (the rule activates when the actual slippage is strictly
	/// greater), a negative value is a strict lower bound (the rule activates when the actual slippage is
	/// strictly less), and <c>0</c> disables the rule. Changing the value refreshes the inherited
	/// <see cref="RiskRule.Title"/> through <see cref="RiskRule.UpdateTitle"/>.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		Description = LocalizedStrings.SlippageSizeKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 0)]
	public decimal Slippage
	{
		get => _slippage;
		set
		{
			if (_slippage == value)
				return;

			_slippage = value;
			UpdateTitle();
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// The title is the configured <see cref="Slippage"/> threshold rendered as text.
	/// </remarks>
	protected override string GetTitle() => _slippage.To<string>();

	/// <inheritdoc />
	/// <remarks>
	/// Only <see cref="MessageTypes.Execution"/> messages are considered; any other
	/// <see cref="Message.Type"/> returns <see langword="false"/>. The message is read as an
	/// <see cref="ExecutionMessage"/>, and when its <see cref="ExecutionMessage.Slippage"/> is
	/// <see langword="null"/> the rule does not activate. A <see cref="Slippage"/> threshold of <c>0</c>
	/// disables the rule. Otherwise the comparison is strict: a positive threshold activates the rule only
	/// when the actual slippage is strictly greater (<c>currValue &gt; Slippage</c>), and a negative
	/// threshold activates it only when the actual slippage is strictly less (<c>currValue &lt; Slippage</c>).
	/// Because the comparison is strict (<c>&gt;</c>/<c>&lt;</c>) rather than inclusive
	/// (<c>&gt;=</c>/<c>&lt;=</c>), a slippage exactly equal to the threshold never activates the rule.
	/// </remarks>
	public override bool ProcessMessage(Message message)
	{
		if (message.Type != MessageTypes.Execution)
			return false;

		var execMsg = (ExecutionMessage)message;
		var currValue = execMsg.Slippage;

		if (currValue == null)
			return false;

		if (Slippage == 0)
			return false; // No limit when Slippage is 0

		if (Slippage > 0)
			return currValue > Slippage;
		else
			return currValue < Slippage;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Calls the base implementation to persist <see cref="RiskRule.Action"/>, then stores the
	/// <see cref="Slippage"/> threshold under the <c>Slippage</c> key.
	/// </remarks>
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(Slippage), Slippage);
	}

	/// <inheritdoc />
	/// <remarks>
	/// Calls the base implementation to restore <see cref="RiskRule.Action"/>, then reads the
	/// <see cref="Slippage"/> threshold from the <c>Slippage</c> key.
	/// </remarks>
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Slippage = storage.GetValue<decimal>(nameof(Slippage));
	}
}
