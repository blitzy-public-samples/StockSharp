namespace StockSharp.Algo.Risk;

/// <summary>
/// Risk-rule, tracking order price.
/// </summary>
/// <remarks>
/// Follows the universal risk-rule shape: it filters on order-registration and order-replacement messages,
/// reads the requested order price, and compares it against the inclusive <see cref="Price"/> threshold.
/// A registration (<see cref="MessageTypes.OrderRegister"/>, an <see cref="OrderRegisterMessage"/>) activates
/// the rule when its price is <c>&gt;=</c> the threshold. A replacement (<see cref="MessageTypes.OrderReplace"/>,
/// an <see cref="OrderReplaceMessage"/>) activates only when its replacement price is strictly positive
/// (<c>&gt; 0</c>) and <c>&gt;=</c> the threshold. When the rule activates, the configured
/// <see cref="RiskRule.Action"/> is applied.
/// </remarks>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.OrderPrice2Key,
	Description = LocalizedStrings.RiskOrderPriceKey,
	GroupName = LocalizedStrings.OrdersKey)]
public class RiskOrderPriceRule : RiskRule
{
	private decimal _price;

	/// <summary>
	/// Order price.
	/// </summary>
	/// <remarks>
	/// The inclusive price threshold compared against incoming order prices. Must be non-negative; assigning a
	/// negative value throws <see cref="ArgumentOutOfRangeException"/>. Changing it refreshes the rule title.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PriceKey,
		Description = LocalizedStrings.OrderPriceKey,
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
	/// Produces the rule label from the current <see cref="Price"/> threshold rendered as a string.
	/// </remarks>
	protected override string GetTitle() => _price.To<string>();

	/// <inheritdoc />
	/// <remarks>
	/// Evaluates the incoming message by type. For <see cref="MessageTypes.OrderRegister"/> (an
	/// <see cref="OrderRegisterMessage"/>) the rule activates when the order price is <c>&gt;=</c>
	/// <see cref="Price"/>. For <see cref="MessageTypes.OrderReplace"/> (an <see cref="OrderReplaceMessage"/>)
	/// the rule activates only when the replacement price is strictly positive (<c>&gt; 0</c>) and <c>&gt;=</c>
	/// <see cref="Price"/>; a non-positive replacement price never activates the rule. Any other message type
	/// returns <see langword="false"/>.
	/// </remarks>
	public override bool ProcessMessage(Message message)
	{
		switch (message.Type)
		{
			case MessageTypes.OrderRegister:
			{
				var orderReg = (OrderRegisterMessage)message;
				return orderReg.Price >= Price;
			}

			case MessageTypes.OrderReplace:
			{
				var orderReplace = (OrderReplaceMessage)message;
				return orderReplace.Price > 0 && orderReplace.Price >= Price;
			}

			default:
				return false;
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// Persists the <see cref="Price"/> threshold in addition to the base rule settings.
	/// </remarks>
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(Price), Price);
	}

	/// <inheritdoc />
	/// <remarks>
	/// Restores the <see cref="Price"/> threshold in addition to the base rule settings.
	/// </remarks>
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Price = storage.GetValue<decimal>(nameof(Price));
	}
}
