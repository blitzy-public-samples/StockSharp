namespace StockSharp.Algo.Risk;

/// <summary>
/// Risk-rule, tracking total commission for order registrations.
/// </summary>
/// <remarks>
/// A specialization of <see cref="RiskTransactionCommissionRule"/> that accumulates commission only from
/// <see cref="ExecutionMessage"/> instances carrying order information (order registrations and updates).
/// All threshold behaviour is inherited unchanged from the base class: each matching execution's commission
/// is added to a running total; a configured commission limit of zero disables the rule; a positive limit
/// acts as an upper bound (the rule activates once the accumulated commission is greater than or equal to the
/// limit); and a negative limit acts as a lower bound (the rule activates once the accumulated commission is
/// less than or equal to the limit). When the rule activates, its configured risk action is executed.
/// </remarks>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.OrderCommissionKey,
	Description = LocalizedStrings.RiskOrderCommissionKey,
	GroupName = LocalizedStrings.OrdersKey)]
public class RiskOrderCommissionRule : RiskTransactionCommissionRule
{
	/// <inheritdoc />
	/// <remarks>
	/// Matches <see cref="ExecutionMessage"/> instances that carry order information (reported by
	/// <c>HasOrderInfo()</c>), so that commission on order registrations and updates contributes to the
	/// running total, while trade-only executions are ignored by this rule.
	/// </remarks>
	protected override bool IsMatch(ExecutionMessage execMsg)
		=> execMsg.HasOrderInfo();
}
