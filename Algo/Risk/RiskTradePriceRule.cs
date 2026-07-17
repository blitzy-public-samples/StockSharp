namespace StockSharp.Algo.Risk;

/// <summary>
/// Risk-rule, tracking trade price.
/// </summary>
/// <remarks>
/// A value-threshold risk rule (see <see cref="RiskRule"/> for the shared contract): it inspects incoming
/// <see cref="MessageTypes.Execution"/> messages, ignores those that do not carry own-trade information,
/// extracts the executed <see cref="ExecutionMessage.TradePrice"/>, and activates when that price is greater
/// than or equal to (&gt;=) the configured <see cref="Price"/> threshold. When the rule activates,
/// <see cref="IRiskManager"/> reports it as triggered and its configured <see cref="RiskRule.Action"/> is
/// enforced downstream by <see cref="RiskMessageAdapter"/> (or the calling code), not by the manager itself.
/// </remarks>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.TradePriceKey,
	Description = LocalizedStrings.RiskTradePriceKey,
	GroupName = LocalizedStrings.TradesKey)]
public class RiskTradePriceRule : RiskRule
{
	private decimal _price;

	/// <summary>
	/// Trade price.
	/// </summary>
	/// <remarks>
	/// Inclusive own-trade price threshold. The rule activates when the executed trade price is
	/// greater than or equal to (&gt;=) this value. Must be non-negative; the setter rejects negative values.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PriceKey,
		Description = LocalizedStrings.TradePriceDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 0)]
	public decimal Price
	{
		get => _price;
		set
		{
			if (_price == value)
				return;

			if (value < 0)
				throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue);

			_price = value;
			UpdateTitle();
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// The title is the configured <see cref="Price"/> threshold formatted as a string.
	/// </remarks>
	protected override string GetTitle() => _price.To<string>();

	/// <inheritdoc />
	/// <remarks>
	/// Returns <see langword="false"/> for any message whose <see cref="Message.Type"/> is not
	/// <see cref="MessageTypes.Execution"/>, and for any <see cref="ExecutionMessage"/> that does not carry
	/// trade information (<c>HasTradeInfo()</c> is <see langword="false"/>). For a qualifying own trade, the rule
	/// activates (returns <see langword="true"/>) when <see cref="ExecutionMessage.TradePrice"/> is greater than
	/// or equal to (&gt;=) the configured <see cref="Price"/> threshold.
	/// </remarks>
	public override bool ProcessMessage(Message message)
	{
		if (message.Type != MessageTypes.Execution)
			return false;

		var execMsg = (ExecutionMessage)message;

		if (!execMsg.HasTradeInfo())
			return false;

		return execMsg.TradePrice >= Price;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Persists the <see cref="Price"/> threshold in addition to the base <see cref="RiskRule"/> settings.
	/// </remarks>
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(Price), Price);
	}

	/// <inheritdoc />
	/// <remarks>
	/// Restores the <see cref="Price"/> threshold in addition to the base <see cref="RiskRule"/> settings.
	/// </remarks>
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Price = storage.GetValue<decimal>(nameof(Price));
	}
}
