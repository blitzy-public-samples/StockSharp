namespace StockSharp.Algo.Risk;

/// <summary>
/// The base class for risk-rules, tracking commission for own transactions.
/// </summary>
/// <remarks>
/// Common base for the commission-tracking risk rules (<see cref="RiskOrderCommissionRule"/> and
/// <see cref="RiskTradeCommissionRule"/>), following the universal risk-rule pattern. It filters on
/// <see cref="MessageTypes.Execution"/> messages, uses the abstract <see cref="IsMatch(ExecutionMessage)"/>
/// extension point to select the matching own-transaction executions, and accumulates their commission
/// into a running total that is compared against the configured <see cref="Commission"/> limit.
/// A zero limit disables the rule; a positive limit is an upper bound (activates when
/// <c>total &gt;= Commission</c>); a negative limit is a lower bound (activates when
/// <c>total &lt;= Commission</c>). When the rule activates, the configured <see cref="RiskActions"/>
/// action (exposed through <see cref="RiskRule.Action"/>) is enforced. The running total is reset to zero
/// by <see cref="Reset"/>.
/// </remarks>
public abstract class RiskTransactionCommissionRule : RiskRule
{
	private decimal _commission;
	private decimal _totalCommission;

	/// <summary>
	/// Commission limit.
	/// </summary>
	/// <remarks>
	/// The accumulated-commission threshold that the running total is compared against. The sign selects the
	/// comparison direction (a positive value is an upper bound and a negative value a lower bound), while a
	/// value of zero disables the rule. Changing the value refreshes the display <see cref="RiskRule.Title"/>
	/// through <see cref="RiskRule.UpdateTitle"/>.
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
	/// The label is the configured <see cref="Commission"/> limit rendered as a string.
	/// </remarks>
	protected override string GetTitle() => _commission.To<string>();

	/// <inheritdoc />
	/// <remarks>
	/// Clears the accumulated running total so evaluation restarts from zero; the configured
	/// <see cref="Commission"/> limit and <see cref="RiskRule.Action"/> are retained.
	/// </remarks>
	public override void Reset()
	{
		base.Reset();

		_totalCommission = 0m;
	}

	/// <summary>
	/// Determine whether the commission is applicable to this rule.
	/// </summary>
	/// <param name="execMsg"><see cref="ExecutionMessage"/></param>
	/// <returns>Check result.</returns>
	/// <remarks>
	/// Extension point implemented by derived rules to decide which executions are counted: return
	/// <see langword="true"/> for an <see cref="ExecutionMessage"/> whose commission should contribute to the
	/// running total. For example, <see cref="RiskOrderCommissionRule"/> matches order-info executions while
	/// <see cref="RiskTradeCommissionRule"/> matches trade-info executions.
	/// </remarks>
	protected abstract bool IsMatch(ExecutionMessage execMsg);

	/// <inheritdoc />
	/// <remarks>
	/// Evaluates a single execution and updates the accumulated commission. The method ignores any message
	/// whose <see cref="Message.Type"/> is not <see cref="MessageTypes.Execution"/>; it also ignores the
	/// message when it is not an <see cref="ExecutionMessage"/>, when <see cref="IsMatch(ExecutionMessage)"/>
	/// returns <see langword="false"/>, or when the execution carries no decimal commission. Otherwise the
	/// execution commission is added to the running total. A <see cref="Commission"/> limit of zero disables
	/// the rule so it never activates; a positive limit activates the rule once the running total reaches it
	/// (<c>total &gt;= Commission</c>); a negative limit activates the rule once the running total falls to it
	/// (<c>total &lt;= Commission</c>).
	/// </remarks>
	public override bool ProcessMessage(Message message)
	{
		if (message.Type != MessageTypes.Execution)
			return false;

		if (message is not ExecutionMessage execMsg ||
			!IsMatch(execMsg) ||
			execMsg.Commission is not decimal commission)
			return false;

		_totalCommission += commission;

		if (Commission == 0)
			return false; // No limit when Commission is 0

		if (Commission > 0)
			return _totalCommission >= Commission;
		else
			return _totalCommission <= Commission;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Persists the <see cref="Commission"/> limit after the base rule persists its <see cref="RiskRule.Action"/>.
	/// </remarks>
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.Set(nameof(Commission), Commission);
	}

	/// <inheritdoc />
	/// <remarks>
	/// Restores the <see cref="Commission"/> limit after the base rule restores its <see cref="RiskRule.Action"/>.
	/// </remarks>
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Commission = storage.GetValue<decimal>(nameof(Commission));
	}
}
