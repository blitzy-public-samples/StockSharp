namespace StockSharp.BusinessEntities;

public partial class Security
{
	private DateTime _localTime;

	/// <summary>
	/// Local time of the last instrument change.
	/// </summary>
	/// <remarks>
	/// Obsolete. Recorded the local time at which the instrument was last changed. This timestamp was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; instrument changes are now delivered as discrete, timestamped
	/// <see cref="Level1ChangeMessage"/> updates routed through the message pipeline. Retained only for
	/// backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[Browsable(false)]
	[XmlIgnore]
	[Obsolete("Use Level1ChangeMessage.")]
	public DateTime LocalTime
	{
		get => _localTime;
		set
		{
			_localTime = value;
			Notify();
		}
	}

	private decimal? _stepPrice;

	//[DataMember]
	/// <summary>
	/// Step price.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the cash value of a single price step, used to convert tick movements into money. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.StepPrice"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StepPriceKey,
		Description = LocalizedStrings.StepPriceDescKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 200)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? StepPrice
	{
		get => _stepPrice;
		set
		{
			if (_stepPrice == value)
				return;

			_stepPrice = value;
			Notify();
		}
	}

	private ITickTradeMessage _lastTick;

	/// <summary>
	/// Information about the last trade. If during the session on the instrument there were no trades, the value equals to <see langword="null" />.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the details of the most recent trade printed on the instrument. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.LastTrade"/> (together with <see cref="Level1Fields.LastTradePrice"/>, <see cref="Level1Fields.LastTradeVolume"/> and <see cref="Level1Fields.LastTradeTime"/>) carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[TypeConverter(typeof(ExpandableObjectConverter))]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LastTradeKey,
		Description = LocalizedStrings.LastTradeDescKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 201)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public ITickTradeMessage LastTick
	{
		get => _lastTick;
		set
		{
			if (_lastTick == value)
				return;

			_lastTick = value;
			Notify();

			if (value == null)
				return;

			if (value.ServerTime != default)
				LastChangeTime = value.ServerTime;
		}
	}

	private decimal? _openPrice;

	//[DataMember]
	/// <summary>
	/// First trade price for the session.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the session's opening (first) trade price. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.OpenPrice"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FirstTradePriceKey,
		Description = LocalizedStrings.FirstTradePriceForSessionKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 202)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? OpenPrice
	{
		get => _openPrice;
		set
		{
			if (_openPrice == value)
				return;

			_openPrice = value;
			Notify();
		}
	}

	private decimal? _closePrice;

	//[DataMember]
	/// <summary>
	/// Last trade price for the previous session.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the previous session's closing (last) trade price. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.ClosePrice"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LastTradePriceKey,
		Description = LocalizedStrings.LastTradePriceDescKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 203)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? ClosePrice
	{
		get => _closePrice;
		set
		{
			if (_closePrice == value)
				return;

			_closePrice = value;
			Notify();
		}
	}

	private decimal? _lowPrice;

	//[DataMember]
	/// <summary>
	/// Lowest price for the session.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the lowest trade price seen during the session. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.LowPrice"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LowPriceKey,
		Description = LocalizedStrings.LowPriceForSessionKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 204)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? LowPrice
	{
		get => _lowPrice;
		set
		{
			if (_lowPrice == value)
				return;

			_lowPrice = value;
			Notify();
		}
	}

	private decimal? _highPrice;

	//[DataMember]
	/// <summary>
	/// Highest price for the session.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the highest trade price seen during the session. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.HighPrice"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.HighestPriceKey,
		Description = LocalizedStrings.HighestPriceForSessionKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 205)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? HighPrice
	{
		get => _highPrice;
		set
		{
			if (_highPrice == value)
				return;

			_highPrice = value;
			Notify();
		}
	}

	private QuoteChange? _bestBid;

	//[DataMember]
	/// <summary>
	/// Best bid in market depth.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the best (highest) buy quote at the top of the order book. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.BestBid"/> (together with <see cref="Level1Fields.BestBidPrice"/> and <see cref="Level1Fields.BestBidVolume"/>) carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BestBidKey,
		Description = LocalizedStrings.BestBidDescKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 206)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public QuoteChange? BestBid
	{
		get => _bestBid;
		set
		{
			//TODO: решить другим методом, OnEquals не тормозит, медленно работает GUI
			//PYH: Тормозит OnEquals

			//if (_bestBid == value)
			//	return;

			_bestBid = value;
			Notify();
		}
	}

	private QuoteChange? _bestAsk;

	//[DataMember]
	/// <summary>
	/// Best ask in market depth.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the best (lowest) sell quote at the top of the order book. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.BestAsk"/> (together with <see cref="Level1Fields.BestAskPrice"/> and <see cref="Level1Fields.BestAskVolume"/>) carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BestAskKey,
		Description = LocalizedStrings.BestAskDescKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 207)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public QuoteChange? BestAsk
	{
		get => _bestAsk;
		set
		{
			// if (_bestAsk == value)
			//	return;

			_bestAsk = value;
			Notify();
		}
	}

	private SecurityStates? _state;

	//[DataMember]
	//[Enum]
	/// <summary>
	/// Current state of security.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the current trading state of the instrument (for example active or halted). This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.State"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StateKey,
		Description = LocalizedStrings.SecurityStateKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 209)]
	[XmlIgnore]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public SecurityStates? State
	{
		get => _state;
		set
		{
			if (_state == value)
				return;

			_state = value;
			Notify();
		}
	}

	private decimal? _minPrice;

	//[DataMember]
	/// <summary>
	/// Lower price limit.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the lower price limit permitted for the instrument. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.MinPrice"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PriceMinKey,
		Description = LocalizedStrings.PriceMinLimitKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 210)]
	[XmlIgnore]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? MinPrice
	{
		get => _minPrice;
		set
		{
			if (_minPrice == value)
				return;

			_minPrice = value;
			Notify();
		}
	}

	private decimal? _maxPrice;

	//[DataMember]
	/// <summary>
	/// Upper price limit.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the upper price limit permitted for the instrument. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.MaxPrice"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PriceMaxKey,
		Description = LocalizedStrings.PriceMaxLimitKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 211)]
	[XmlIgnore]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? MaxPrice
	{
		get => _maxPrice;
		set
		{
			if (_maxPrice == value)
				return;

			_maxPrice = value;
			Notify();
		}
	}

	private decimal? _marginBuy;

	//[DataMember]
	/// <summary>
	/// Initial margin to buy.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the initial margin required to open a long position. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.MarginBuy"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarginBuyKey,
		Description = LocalizedStrings.MarginBuyDescKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 212)]
	[XmlIgnore]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? MarginBuy
	{
		get => _marginBuy;
		set
		{
			if (_marginBuy == value)
				return;

			_marginBuy = value;
			Notify();
		}
	}

	private decimal? _marginSell;

	//[DataMember]
	/// <summary>
	/// Initial margin to sell.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the initial margin required to open a short position. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.MarginSell"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarginSellKey,
		Description = LocalizedStrings.MarginSellDescKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 213)]
	[XmlIgnore]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? MarginSell
	{
		get => _marginSell;
		set
		{
			if (_marginSell == value)
				return;

			_marginSell = value;
			Notify();
		}
	}

	private decimal? _impliedVolatility;

	//[DataMember]
	/// <summary>
	/// Volatility (implied).
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the option's implied volatility. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.ImpliedVolatility"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IVKey,
		Description = LocalizedStrings.ImpliedVolatilityKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.DerivativesKey,
		Order = 104)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? ImpliedVolatility
	{
		get => _impliedVolatility;
		set
		{
			if (_impliedVolatility == value)
				return;

			_impliedVolatility = value;
			Notify();
		}
	}

	private decimal? _historicalVolatility;

	//[DataMember]
	/// <summary>
	/// Volatility (historical).
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the instrument's historical volatility. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.HistoricalVolatility"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.HVKey,
		Description = LocalizedStrings.HistoricalVolatilityKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.DerivativesKey,
		Order = 105)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? HistoricalVolatility
	{
		get => _historicalVolatility;
		set
		{
			if (_historicalVolatility == value)
				return;

			_historicalVolatility = value;
			Notify();
		}
	}

	private decimal? _theorPrice;

	//[DataMember]
	/// <summary>
	/// Theoretical price.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the option's theoretical (model) price. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.TheorPrice"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TheorPriceKey,
		Description = LocalizedStrings.TheoreticalPriceKey,
		GroupName = LocalizedStrings.DerivativesKey,
		Order = 106)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? TheorPrice
	{
		get => _theorPrice;
		set
		{
			if (_theorPrice == value)
				return;

			_theorPrice = value;
			Notify();
		}
	}

	private decimal? _delta;

	//[DataMember]
	/// <summary>
	/// Option delta.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the option delta, the sensitivity of its price to the underlying. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.Delta"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DeltaKey,
		Description = LocalizedStrings.OptionDeltaKey,
		GroupName = LocalizedStrings.DerivativesKey,
		Order = 107)]
	[XmlIgnore]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? Delta
	{
		get => _delta;
		set
		{
			if (_delta == value)
				return;

			_delta = value;
			Notify();
		}
	}

	private decimal? _gamma;

	//[DataMember]
	/// <summary>
	/// Option gamma.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the option gamma, the rate of change of delta. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.Gamma"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.GammaKey,
		Description = LocalizedStrings.OptionGammaKey,
		GroupName = LocalizedStrings.DerivativesKey,
		Order = 108)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? Gamma
	{
		get => _gamma;
		set
		{
			if (_gamma == value)
				return;

			_gamma = value;
			Notify();
		}
	}

	private decimal? _vega;

	//[DataMember]
	/// <summary>
	/// Option vega.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the option vega, the sensitivity to volatility. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.Vega"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VegaKey,
		Description = LocalizedStrings.OptionVegaKey,
		GroupName = LocalizedStrings.DerivativesKey,
		Order = 109)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? Vega
	{
		get => _vega;
		set
		{
			if (_vega == value)
				return;

			_vega = value;
			Notify();
		}
	}

	private decimal? _theta;

	//[DataMember]
	/// <summary>
	/// Option theta.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the option theta, its time decay. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.Theta"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ThetaKey,
		Description = LocalizedStrings.OptionThetaKey,
		GroupName = LocalizedStrings.DerivativesKey,
		Order = 110)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? Theta
	{
		get => _theta;
		set
		{
			if (_theta == value)
				return;

			_theta = value;
			Notify();
		}
	}

	private decimal? _rho;

	//[DataMember]
	/// <summary>
	/// Option rho.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the option rho, the sensitivity to interest rates. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.Rho"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RhoKey,
		Description = LocalizedStrings.OptionRhoKey,
		GroupName = LocalizedStrings.DerivativesKey,
		Order = 111)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? Rho
	{
		get => _rho;
		set
		{
			if (_rho == value)
				return;

			_rho = value;
			Notify();
		}
	}

	private decimal? _openInterest;

	//[DataMember]
	/// <summary>
	/// Number of open positions (open interest).
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the number of open positions (open interest) for the instrument. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.OpenInterest"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OpenInterestKey,
		Description = LocalizedStrings.OpenInterestDescKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 220)]
	[XmlIgnore]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? OpenInterest
	{
		get => _openInterest;
		set
		{
			if (_openInterest == value)
				return;

			_openInterest = value;
			Notify();
		}
	}

	private DateTime _lastChangeTime;

	//[StatisticsCategory]
	/// <summary>
	/// Time of the last instrument change.
	/// </summary>
	/// <remarks>
	/// Obsolete. Recorded the server time at which the instrument was last changed. This timestamp was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; instrument changes are now delivered as discrete, timestamped
	/// <see cref="Level1ChangeMessage"/> updates routed through the message pipeline. Retained only for
	/// backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	[XmlIgnore]
	public DateTime LastChangeTime
	{
		get => _lastChangeTime;
		set
		{
			_lastChangeTime = value;
			Notify();
		}
	}

	private decimal? _bidsVolume;

	//[DataMember]
	/// <summary>
	/// Total volume in all buy orders.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the total volume resting across all buy orders. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.BidsVolume"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BidsVolumeKey,
		Description = LocalizedStrings.BidsVolumeDescKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 221)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? BidsVolume
	{
		get => _bidsVolume;
		set
		{
			_bidsVolume = value;
			Notify();
		}
	}

	private int? _bidsCount;

	//[DataMember]
	/// <summary>
	/// Number of buy orders.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the number of buy orders in the book. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.BidsCount"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BidsKey,
		Description = LocalizedStrings.BidsCountDescKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 222)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public int? BidsCount
	{
		get => _bidsCount;
		set
		{
			_bidsCount = value;
			Notify();
		}
	}

	private decimal? _asksVolume;

	//[DataMember]
	/// <summary>
	/// Total volume in all sell orders.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the total volume resting across all sell orders. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.AsksVolume"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AsksVolumeKey,
		Description = LocalizedStrings.AsksVolumeDescKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 223)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? AsksVolume
	{
		get => _asksVolume;
		set
		{
			_asksVolume = value;
			Notify();
		}
	}

	private int? _asksCount;

	//[DataMember]
	/// <summary>
	/// Number of sell orders.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the number of sell orders in the book. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.AsksCount"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AsksKey,
		Description = LocalizedStrings.AsksCountDescKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 224)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public int? AsksCount
	{
		get => _asksCount;
		set
		{
			_asksCount = value;
			Notify();
		}
	}

	private int? _tradesCount;

	//[DataMember]
	/// <summary>
	/// Number of trades.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the number of trades executed on the instrument. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.TradesCount"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TradesOfKey,
		Description = LocalizedStrings.LimitOrderTifKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 225)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public int? TradesCount
	{
		get => _tradesCount;
		set
		{
			_tradesCount = value;
			Notify();
		}
	}

	private decimal? _highBidPrice;

	//[DataMember]
	/// <summary>
	/// Maximum bid during the session.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the highest bid price seen during the session. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.HighBidPrice"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BidMaxKey,
		Description = LocalizedStrings.BidMaxDescKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 226)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? HighBidPrice
	{
		get => _highBidPrice;
		set
		{
			_highBidPrice = value;
			Notify();
		}
	}

	private decimal? _lowAskPrice;

	//[DataMember]
	/// <summary>
	/// Minimum ask during the session.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the lowest ask price seen during the session. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.LowAskPrice"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[XmlIgnore]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AskMinKey,
		Description = LocalizedStrings.AskMinDescKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 227)]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? LowAskPrice
	{
		get => _lowAskPrice;
		set
		{
			_lowAskPrice = value;
			Notify();
		}
	}

	private decimal? _yield;

	//[DataMember]
	/// <summary>
	/// Yield.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the instrument's yield. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.Yield"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.YieldKey,
		Description = LocalizedStrings.YieldKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 228)]
	[XmlIgnore]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? Yield
	{
		get => _yield;
		set
		{
			_yield = value;
			Notify();
		}
	}

	private decimal? _vwap;

	//[DataMember]
	/// <summary>
	/// Average price.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the volume-weighted average price (VWAP). This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.VWAP"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AveragePriceKey,
		Description = LocalizedStrings.AveragePriceKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 229)]
	[XmlIgnore]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? VWAP
	{
		get => _vwap;
		set
		{
			_vwap = value;
			Notify();
		}
	}

	private decimal? _settlementPrice;

	//[DataMember]
	/// <summary>
	/// Settlement price.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the instrument's settlement price. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.SettlementPrice"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SettlementPriceKey,
		Description = LocalizedStrings.SettlementPriceDescKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 230)]
	[XmlIgnore]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? SettlementPrice
	{
		get => _settlementPrice;
		set
		{
			_settlementPrice = value;
			Notify();
		}
	}

	private decimal? _averagePrice;

	//[DataMember]
	/// <summary>
	/// Average price per session.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the average trade price over the session. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.AveragePrice"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AveragePriceKey,
		Description = LocalizedStrings.AveragePriceDescKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 231)]
	[XmlIgnore]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? AveragePrice
	{
		get => _averagePrice;
		set
		{
			_averagePrice = value;
			Notify();
		}
	}

	private decimal? _volume;

	//[DataMember]
	/// <summary>
	/// Volume per session.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the total traded volume over the session. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.Volume"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VolumeKey,
		Description = LocalizedStrings.VolumeDescKey,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 232)]
	[XmlIgnore]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? Volume
	{
		get => _volume;
		set
		{
			_volume = value;
			Notify();
		}
	}

	private decimal? _turnover;

	/// <summary>
	/// Turnover.
	/// </summary>
	/// <remarks>
	/// Obsolete. Held the total money turnover on the instrument. This Level1 value was historically exposed as a mutable property on the
	/// <see cref="Security"/> entity; live market state is now delivered through the message pipeline as
	/// <see cref="Level1Fields.Turnover"/> carried inside a discrete, timestamped <see cref="Level1ChangeMessage"/>. Retained
	/// only for backward compatibility. Use <see cref="Level1ChangeMessage"/> instead.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TurnoverKey,
		Description = LocalizedStrings.TurnoverKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.StatisticsKey,
		Order = 232)]
	[XmlIgnore]
	[Browsable(false)]
	[Obsolete("Use Level1ChangeMessage.")]
	public decimal? Turnover
	{
		get => _turnover;
		set
		{
			_turnover = value;
			Notify();
		}
	}
}
