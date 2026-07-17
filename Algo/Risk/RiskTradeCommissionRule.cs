namespace StockSharp.Algo.Risk;

/// <summary>
/// Risk-rule, tracking total commission for own trades.
/// </summary>
/// <remarks>
/// A <see cref="RiskTransactionCommissionRule"/> specialization that accumulates commission only from
/// trade-info <see cref="ExecutionMessage"/> instances (own trades). It inherits the running-total
/// accumulation and the threshold semantics of the base rule: a zero limit disables the rule, a
/// positive limit acts as an upper bound (the rule activates once the accumulated commission reaches
/// or exceeds the limit), and a negative limit acts as a lower bound (the rule activates once the
/// accumulated commission reaches or falls below the limit). When the limit is breached the configured
/// risk action fires.
/// </remarks>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.TradeCommissionKey,
	Description = LocalizedStrings.RiskTradeCommissionKey,
	GroupName = LocalizedStrings.PnLKey)]
public class RiskTradeCommissionRule : RiskTransactionCommissionRule
{
	/// <inheritdoc />
	/// <remarks>
	/// Matches executions that carry trade information (<c>HasTradeInfo()</c>), that is, own-trade
	/// reports, so their commission contributes to the running total tracked by
	/// <see cref="RiskTransactionCommissionRule"/>. Executions without trade information (for example,
	/// order-only transaction reports) are ignored by this rule.
	/// </remarks>
	protected override bool IsMatch(ExecutionMessage execMsg)
		=> execMsg.HasTradeInfo();
}
