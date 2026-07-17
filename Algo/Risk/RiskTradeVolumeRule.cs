namespace StockSharp.Algo.Risk;

/// <summary>
/// Risk-rule, tracking trade volume.
/// </summary>
/// <remarks>
/// Applies the universal risk-rule pattern to own-trade executions: it inspects incoming
/// <see cref="MessageTypes.Execution"/> messages, keeps only those that carry trade information
/// (an <see cref="ExecutionMessage"/> describing a completed own trade), extracts the executed trade
/// volume, and activates when that volume is greater than or equal to (&gt;=) the configured
/// <see cref="Volume"/> threshold. On activation the configured <see cref="RiskRule.Action"/>
/// (for example, cancel orders, stop trading, or close positions) is enforced by the risk manager.
/// </remarks>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.TradeVolumeKey,
	Description = LocalizedStrings.RiskTradeVolumeKey,
	GroupName = LocalizedStrings.TradesKey)]
public class RiskTradeVolumeRule : RiskRule
{
	private decimal _volume;

	/// <summary>
	/// Trade volume.
	/// </summary>
	/// <remarks>
	/// Inclusive trade-volume threshold: the rule activates as soon as an own trade's executed volume
	/// reaches or exceeds this value. The value must be non-negative; assigning a negative number throws
	/// <see cref="ArgumentOutOfRangeException"/>, and changing it refreshes the rule <see cref="RiskRule.Title"/>.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VolumeKey,
		Description = LocalizedStrings.TradeVolumeDescKey,
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
	/// Returns the configured <see cref="Volume"/> threshold formatted as a string; used as the rule's
	/// display label.
	/// </remarks>
	protected override string GetTitle() => _volume.To<string>();

	/// <inheritdoc />
	/// <remarks>
	/// Ignores every message whose <see cref="Message.Type"/> is not <see cref="MessageTypes.Execution"/>
	/// (returns <see langword="false"/>). The message is treated as an <see cref="ExecutionMessage"/>; when it
	/// does not carry trade information the rule does not activate. Otherwise it activates (returns
	/// <see langword="true"/>) when the executed <see cref="ExecutionMessage.TradeVolume"/> is greater than or
	/// equal to (&gt;=) the configured <see cref="Volume"/> threshold.
	/// </remarks>
	public override bool ProcessMessage(Message message)
	{
		if (message.Type != MessageTypes.Execution)
			return false;

		var execMsg = (ExecutionMessage)message;

		if (!execMsg.HasTradeInfo())
			return false;

		return execMsg.TradeVolume >= Volume;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Persists the <see cref="Volume"/> threshold in addition to the base <see cref="RiskRule"/> settings.
	/// </remarks>
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(Volume), Volume);
	}

	/// <inheritdoc />
	/// <remarks>
	/// Restores the <see cref="Volume"/> threshold in addition to the base <see cref="RiskRule"/> settings.
	/// </remarks>
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Volume = storage.GetValue<decimal>(nameof(Volume));
	}
}
