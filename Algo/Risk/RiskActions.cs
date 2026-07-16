namespace StockSharp.Algo.Risk;

/// <summary>
/// Types of actions.
/// </summary>
/// <remarks>
/// Each value is one of the mutually exclusive responses that a triggered risk rule can request through its
/// <see cref="IRiskRule.Action"/> property. When the owning rule activates, the requested action is enforced by
/// <see cref="RiskMessageAdapter"/>; a single action is applied per triggered rule and the actions do not combine.
/// </remarks>
public enum RiskActions
{
	/// <summary>
	/// Close positions.
	/// </summary>
	/// <remarks>
	/// When a rule requesting this action activates, <see cref="RiskMessageAdapter"/> emits an order-group cancel
	/// instruction in "close positions" mode (an <see cref="OrderGroupCancelMessage"/> whose
	/// <see cref="OrderGroupCancelMessage.Mode"/> is <see cref="OrderGroupCancelModes.ClosePositions"/>), directing
	/// the inner adapter to flatten and close open positions.
	/// </remarks>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ClosePositionsKey)]
	ClosePositions,

	/// <summary>
	/// Stop trading.
	/// </summary>
	/// <remarks>
	/// When a rule requesting this action activates, <see cref="RiskMessageAdapter"/> blocks trading by setting an
	/// internal trading-blocked flag, so that subsequent <see cref="OrderRegisterMessage"/> and
	/// <see cref="OrderReplaceMessage"/> messages are rejected with a failed execution (reason "trading disabled").
	/// Trading is automatically unblocked once a later message triggers no rules.
	/// </remarks>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopTradingKey)]
	StopTrading,

	/// <summary>
	/// Cancel orders.
	/// </summary>
	/// <remarks>
	/// When a rule requesting this action activates, <see cref="RiskMessageAdapter"/> emits an
	/// <see cref="OrderGroupCancelMessage"/> (looped back to the adapter) that cancels the active orders.
	/// </remarks>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CancelOrdersKey)]
	CancelOrders,
}