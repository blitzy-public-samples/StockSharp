namespace StockSharp.Algo.Risk;

/// <summary>
/// The message adapter, automatically controlling risk rules.
/// </summary>
/// <remarks>
/// This wrapper consults the composed <see cref="IRiskManager"/> on both message directions, but the
/// evaluation is path-specific rather than applied uniformly to every message:
/// <list type="bullet">
/// <item><description>
/// Outbound (send-in) via <see cref="OnSendInMessageAsync"/>: while trading is blocked by a prior
/// <see cref="RiskActions.StopTrading"/> activation, <see cref="MessageTypes.OrderRegister"/> and
/// <see cref="MessageTypes.OrderReplace"/> messages are rejected with a failed
/// <see cref="ExecutionMessage"/> and returned before any rule evaluation (early return); all other
/// outbound messages are offered to the manager's rules.
/// </description></item>
/// <item><description>
/// Inbound (new-out) via <see cref="OnInnerAdapterNewOutMessageAsync"/>: every message except
/// <see cref="MessageTypes.Reset"/> is offered to the manager's rules; a <see cref="MessageTypes.Reset"/>
/// message is passed straight through without risk evaluation.
/// </description></item>
/// </list>
/// For every rule the manager reports as triggered, a warning is logged (the
/// <see cref="LocalizedStrings.ActivatingRiskRule"/> message) and the rule's configured
/// <see cref="RiskActions"/> value is applied as a concrete effect on the message flow:
/// <list type="bullet">
/// <item><description>
/// <see cref="RiskActions.ClosePositions"/> produces an <see cref="OrderGroupCancelMessage"/> in
/// <see cref="OrderGroupCancelModes.ClosePositions"/> mode, delegating to the inner adapter to
/// flatten open positions.
/// </description></item>
/// <item><description>
/// <see cref="RiskActions.StopTrading"/> blocks trading and logs the transition: subsequent
/// <see cref="MessageTypes.OrderRegister"/> and <see cref="MessageTypes.OrderReplace"/> messages
/// are rejected with a failed execution (reason "trading disabled") instead of being forwarded.
/// Trading is automatically unblocked (and the transition logged) as soon as a later processed
/// message triggers no rules.
/// </description></item>
/// <item><description>
/// <see cref="RiskActions.CancelOrders"/> raises a looped-back <see cref="OrderGroupCancelMessage"/>
/// (through <c>LoopBack</c>) to cancel the active orders.
/// </description></item>
/// </list>
/// Any other (unrecognised) <see cref="RiskActions"/> value causes an
/// <see cref="InvalidOperationException"/> to be thrown. On the inbound path, a message produced by an
/// activated rule is looped back into this adapter and raised as a new outgoing message before the
/// original message is forwarded to the base implementation.
/// </remarks>
public class RiskMessageAdapter : MessageAdapterWrapper
{
	private readonly IRiskManager _riskManager;
	private bool _isTradingBlocked;

	/// <summary>
	/// Initializes a new instance of the <see cref="RiskMessageAdapter"/>.
	/// </summary>
	/// <param name="innerAdapter">The adapter, to which messages will be directed.</param>
	/// <param name="riskManager">Risk control manager.</param>
	/// <remarks>
	/// The supplied <paramref name="riskManager"/> is adopted as a child log source: its Parent is
	/// set to this adapter when not already assigned, so risk-rule activations are reported through
	/// this adapter's log hierarchy.
	/// </remarks>
	public RiskMessageAdapter(IMessageAdapter innerAdapter, IRiskManager riskManager)
		: base(innerAdapter)
	{
		_riskManager = riskManager ?? throw new ArgumentNullException(nameof(riskManager));
		_riskManager.Parent ??= this;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Outbound (send-in) path. While trading is blocked by a prior
	/// <see cref="RiskActions.StopTrading"/> activation, incoming
	/// <see cref="MessageTypes.OrderRegister"/> and <see cref="MessageTypes.OrderReplace"/>
	/// messages are short-circuited: each <paramref name="message"/> is answered with a failed
	/// <see cref="ExecutionMessage"/> (<see cref="OrderStates.Failed"/>, carrying the original
	/// transaction id) and is not forwarded to the inner adapter. Otherwise the message is run
	/// through risk processing; if a rule produces a replacement message it is sent in place of the
	/// original before delegating to the base implementation.
	/// </remarks>
	protected override async ValueTask OnSendInMessageAsync(Message message, CancellationToken cancellationToken)
	{
		// Check if trading is blocked and reject order registration/modification
		if (_isTradingBlocked)
		{
			switch (message.Type)
			{
				case MessageTypes.OrderRegister:
				{
					var regMsg = (OrderRegisterMessage)message;
					await RaiseNewOutMessageAsync(new ExecutionMessage
					{
						OriginalTransactionId = regMsg.TransactionId,
						DataTypeEx = DataType.Transactions,
						ServerTime = DateTime.UtcNow,
						HasOrderInfo = true,
						OrderState = OrderStates.Failed,
						Error = new InvalidOperationException(LocalizedStrings.TradingDisabled)
					}, cancellationToken);
					return;
				}
				case MessageTypes.OrderReplace:
				{
					var replaceMsg = (OrderReplaceMessage)message;
					await RaiseNewOutMessageAsync(new ExecutionMessage
					{
						OriginalTransactionId = replaceMsg.TransactionId,
						DataTypeEx = DataType.Transactions,
						ServerTime = DateTime.UtcNow,
						HasOrderInfo = true,
						OrderState = OrderStates.Failed,
						Error = new InvalidOperationException(LocalizedStrings.TradingDisabled)
					}, cancellationToken);
					return;
				}
			}
		}

		var extra = await ProcessRiskAsync(message, cancellationToken);

		if (extra is not null)
			message = extra;

		await base.OnSendInMessageAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	/// <remarks>
	/// Inbound (new-out) path. Every <paramref name="message"/> except
	/// <see cref="MessageTypes.Reset"/> is run through risk processing; if a rule produces a
	/// message it is looped back into this adapter and raised as a new outgoing message before the
	/// original message is passed to the base implementation.
	/// </remarks>
	protected override async ValueTask OnInnerAdapterNewOutMessageAsync(Message message, CancellationToken cancellationToken)
	{
		if (message.Type != MessageTypes.Reset)
		{
			var extra = await ProcessRiskAsync(message, cancellationToken);
			if (extra is not null)
			{
				extra.LoopBack(this);
				await RaiseNewOutMessageAsync(extra, cancellationToken);
			}
		}

		await base.OnInnerAdapterNewOutMessageAsync(message, cancellationToken);
	}

	/// <summary>
	/// Runs a single message through the risk manager and applies the effect of every activated rule.
	/// </summary>
	/// <remarks>
	/// Offers <paramref name="message"/> to <see cref="IRiskManager.ProcessRules"/> and iterates the rules it
	/// reports as triggered. For each triggered rule a warning is logged (the
	/// <see cref="LocalizedStrings.ActivatingRiskRule"/> message) and its <see cref="RiskActions"/> value is
	/// acted on: <see cref="RiskActions.ClosePositions"/> yields an <see cref="OrderGroupCancelMessage"/> in
	/// <see cref="OrderGroupCancelModes.ClosePositions"/> mode as the returned replacement;
	/// <see cref="RiskActions.StopTrading"/> sets the trading-blocked flag and logs the transition;
	/// <see cref="RiskActions.CancelOrders"/> raises a looped-back <see cref="OrderGroupCancelMessage"/>
	/// immediately; and any other value throws an <see cref="InvalidOperationException"/>. After processing, if
	/// trading was blocked and no rule triggered on this message, the block is cleared and the unblock is logged.
	/// </remarks>
	/// <param name="message">The message to evaluate against the configured rules.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A replacement message to forward in place of the original (currently only for
	/// <see cref="RiskActions.ClosePositions"/>), or <see langword="null"/> when nothing needs to be substituted.</returns>
	private async ValueTask<Message> ProcessRiskAsync(Message message, CancellationToken cancellationToken)
	{
		Message retVal = null;
		var triggeredRules = _riskManager.ProcessRules(message).ToArray();

		foreach (var rule in triggeredRules)
		{
			LogWarning(LocalizedStrings.ActivatingRiskRule,
				rule.GetType().GetDisplayName(), rule.Title, rule.Action);

			switch (rule.Action)
			{
				case RiskActions.ClosePositions:
				{
					// Delegate closing positions to the inner adapter
					retVal = new OrderGroupCancelMessage
					{
						TransactionId = TransactionIdGenerator.GetNextId(),
						Mode = OrderGroupCancelModes.ClosePositions,
					};
					break;
				}
				case RiskActions.StopTrading:
				{
					_isTradingBlocked = true;
					LogInfo(LocalizedStrings.TradingDisabled);
					break;
				}
				case RiskActions.CancelOrders:
					await RaiseNewOutMessageAsync(new OrderGroupCancelMessage { TransactionId = TransactionIdGenerator.GetNextId() }.LoopBack(this), cancellationToken);
					break;
				default:
					throw new InvalidOperationException(rule.Action.To<string>());
			}
		}

		// Check if trading should be unblocked: if no rules triggered, clear the flag
		if (_isTradingBlocked && triggeredRules.Length == 0)
		{
			_isTradingBlocked = false;
			LogInfo("Trading unblocked - risk limits no longer exceeded.");
		}

		return retVal;
	}

	/// <summary>
	/// Create a copy of <see cref="RiskMessageAdapter"/>.
	/// </summary>
	/// <returns>Copy.</returns>
	/// <remarks>
	/// Both the wrapped inner adapter and the composed <see cref="IRiskManager"/> are cloned, so the
	/// copy controls risk independently of this instance.
	/// </remarks>
	public override IMessageAdapter Clone()
	{
		return new RiskMessageAdapter(InnerAdapter.TypedClone(), _riskManager.Clone());
	}
}