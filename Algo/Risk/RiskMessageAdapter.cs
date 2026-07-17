namespace StockSharp.Algo.Risk;

/// <summary>
/// The message adapter, automatically controlling risk rules.
/// </summary>
/// <remarks>
/// This wrapper consults the composed <see cref="IRiskManager"/> on both message directions:
/// outbound messages travelling to the inner adapter through <see cref="OnSendInMessageAsync"/>,
/// and inbound messages surfaced through <see cref="OnInnerAdapterNewOutMessageAsync"/>. Each
/// message is offered to the manager's rules, and for every rule reported as triggered the rule's
/// configured <see cref="RiskActions"/> value is enforced as a concrete effect on the message flow:
/// <list type="bullet">
/// <item><description>
/// <see cref="RiskActions.ClosePositions"/> emits an <see cref="OrderGroupCancelMessage"/> in
/// <see cref="OrderGroupCancelModes.ClosePositions"/> mode, delegating to the inner adapter to
/// flatten open positions.
/// </description></item>
/// <item><description>
/// <see cref="RiskActions.StopTrading"/> blocks trading: subsequent
/// <see cref="MessageTypes.OrderRegister"/> and <see cref="MessageTypes.OrderReplace"/> messages
/// are rejected with a failed execution (reason "trading disabled") instead of being forwarded,
/// until a later message triggers no rules, at which point trading is automatically unblocked.
/// </description></item>
/// <item><description>
/// <see cref="RiskActions.CancelOrders"/> emits a looped-back <see cref="OrderGroupCancelMessage"/>
/// to cancel the active orders.
/// </description></item>
/// </list>
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