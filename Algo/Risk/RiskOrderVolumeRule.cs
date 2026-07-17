namespace StockSharp.Algo.Risk;

/// <summary>
/// Risk-rule, tracking order volume.
/// </summary>
/// <remarks>
/// Part of the pre-trade risk engine. This rule follows the universal risk-rule pattern: it filters
/// incoming messages by type, extracts the order volume, and compares it against the configured
/// inclusive <see cref="Volume"/> threshold. It evaluates order-submission messages of type
/// <see cref="MessageTypes.OrderRegister"/> and order-amendment messages of type
/// <see cref="MessageTypes.OrderReplace"/>: a registration activates the rule when the submitted
/// order volume is &gt;= the threshold; a replacement activates the rule only when the new order
/// volume is strictly positive (&gt; 0) and &gt;= the threshold. All other message types leave the
/// rule inactive. When the rule activates, the configured <see cref="RiskRule.Action"/> is taken
/// (for example, cancel orders, stop trading, or close positions).
/// </remarks>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.OrderVolume2Key,
	Description = LocalizedStrings.RiskOrderVolumeKey,
	GroupName = LocalizedStrings.OrdersKey)]
public class RiskOrderVolumeRule : RiskRule
{
	private decimal _volume;

	/// <summary>
	/// Order volume.
	/// </summary>
	/// <remarks>
	/// The inclusive volume threshold that a submitted or replaced order's volume is compared against.
	/// Must be non-negative; the setter rejects negative values. Because the comparison is inclusive
	/// (&gt;=), a threshold of zero is reached by every qualifying order that the rule evaluates.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VolumeKey,
		Description = LocalizedStrings.OrderVolumeKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 0)]
	public decimal Volume
	{
		get => _volume;
		set
		{
			if (_volume == value)
				return;

			if (value < 0)
				throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue);

			_volume = value;
			UpdateTitle();
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// The rule's display label is the configured <see cref="Volume"/> threshold formatted as a string.
	/// </remarks>
	protected override string GetTitle() => _volume.To<string>();

	/// <inheritdoc />
	/// <remarks>
	/// Returns <see langword="true"/> (activating the rule) based on the message type:
	/// for <see cref="MessageTypes.OrderRegister"/> (an <see cref="OrderRegisterMessage"/>), when the
	/// submitted order volume is &gt;= <see cref="Volume"/>;
	/// for <see cref="MessageTypes.OrderReplace"/> (an <see cref="OrderReplaceMessage"/>), when the new
	/// order volume is strictly positive (&gt; 0) and &gt;= <see cref="Volume"/>. The strictly-positive
	/// guard prevents a replacement that does not specify a new volume from triggering the rule.
	/// Every other message type returns <see langword="false"/>.
	/// </remarks>
	public override bool ProcessMessage(Message message)
	{
		switch (message.Type)
		{
			case MessageTypes.OrderRegister:
			{
				var orderReg = (OrderRegisterMessage)message;
				return orderReg.Volume >= Volume;
			}

			case MessageTypes.OrderReplace:
			{
				var orderReplace = (OrderReplaceMessage)message;
				return orderReplace.Volume > 0 && orderReplace.Volume >= Volume;
			}

			default:
				return false;
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// Persists the <see cref="Volume"/> threshold in addition to the base rule settings.
	/// </remarks>
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(Volume), Volume);
	}

	/// <inheritdoc />
	/// <remarks>
	/// Restores the <see cref="Volume"/> threshold in addition to the base rule settings.
	/// </remarks>
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Volume = storage.GetValue<decimal>(nameof(Volume));
	}
}
